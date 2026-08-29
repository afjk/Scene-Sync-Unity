using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace SceneSync.UnityClient
{
    /// <summary>
    /// Normalizes gzip-encoded Scene Sync carrier files before the Scene Sync package
    /// attempts to interpret them as GLB. Some senders persist the compressed carrier
    /// bytes under a .glb cache name, which otherwise makes glTFast show a fallback cube.
    /// </summary>
    public static class SceneSyncGzipCarrierCompatibility
    {
        private const int MaxDecompressedBytes = 512 * 1024 * 1024;
        private const int CopyBufferSize = 80 * 1024;

        public static bool IsGzip(byte[] bytes)
        {
            return bytes != null
                && bytes.Length >= 2
                && bytes[0] == 0x1f
                && bytes[1] == 0x8b;
        }

        public static bool IsGlb(byte[] bytes)
        {
            return bytes != null
                && bytes.Length >= 4
                && bytes[0] == (byte)'g'
                && bytes[1] == (byte)'l'
                && bytes[2] == (byte)'T'
                && bytes[3] == (byte)'F';
        }

        public static bool TryDecompress(byte[] carrierBytes, out byte[] glbBytes, out string error)
        {
            glbBytes = null;
            error = null;

            if (!IsGzip(carrierBytes))
            {
                error = "carrier is not gzip data";
                return false;
            }

            try
            {
                using var input = new MemoryStream(carrierBytes, writable: false);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                var buffer = new byte[CopyBufferSize];

                while (true)
                {
                    var read = gzip.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    if (output.Length + read > MaxDecompressedBytes)
                    {
                        error = "decompressed carrier exceeds the 512 MiB safety limit";
                        return false;
                    }

                    output.Write(buffer, 0, read);
                }

                var result = output.ToArray();
                if (!IsGlb(result))
                {
                    error = "decompressed carrier is not a GLB file";
                    return false;
                }

                glbBytes = result;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static Task<CacheNormalizationResult> NormalizePersistentCacheAsync(string cacheDirectory)
        {
            return Task.Run(() => NormalizePersistentCache(cacheDirectory));
        }

        public static CacheNormalizationResult NormalizePersistentCache(string cacheDirectory)
        {
            var result = new CacheNormalizationResult();
            if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
            {
                return result;
            }

            try
            {
                foreach (var path in Directory.GetFiles(cacheDirectory, "*.glb"))
                {
                    byte[] storedBytes;
                    try
                    {
                        storedBytes = File.ReadAllBytes(path);
                    }
                    catch (Exception exception)
                    {
                        result.Errors++;
                        result.LastError = exception.Message;
                        continue;
                    }

                    if (!IsGzip(storedBytes))
                    {
                        continue;
                    }

                    if (!TryDecompress(storedBytes, out var glbBytes, out var error))
                    {
                        result.Errors++;
                        result.LastError = error;
                        continue;
                    }

                    try
                    {
                        File.WriteAllBytes(path, glbBytes);
                        result.NormalizedFiles++;
                    }
                    catch (Exception exception)
                    {
                        result.Errors++;
                        result.LastError = exception.Message;
                    }
                }
            }
            catch (Exception exception)
            {
                result.Errors++;
                result.LastError = exception.Message;
            }

            return result;
        }

        public static string GetPersistentCacheDirectory(string persistentDataPath)
        {
            return Path.Combine(persistentDataPath, "SceneSyncGlbCache");
        }

        public static string GetPersistentCachePath(
            string cacheDirectory,
            string prefix,
            string key)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory)
                || string.IsNullOrWhiteSpace(prefix)
                || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var invalid = Path.GetInvalidFileNameChars();
            var characters = key.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                if (Array.IndexOf(invalid, characters[index]) >= 0)
                {
                    characters[index] = '_';
                }
            }

            return Path.Combine(cacheDirectory, prefix + "-" + new string(characters) + ".glb");
        }

        public static bool TryLoadCachedGlb(
            string cacheDirectory,
            string assetId,
            string meshPath,
            out byte[] glbBytes,
            out string error)
        {
            glbBytes = null;
            error = "carrier cache file was not found";

            var candidates = new[]
            {
                GetPersistentCachePath(cacheDirectory, "asset", assetId),
                GetPersistentCachePath(cacheDirectory, "mesh", meshPath),
            };

            foreach (var path in candidates)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var bytes = File.ReadAllBytes(path);
                    if (IsGlb(bytes))
                    {
                        glbBytes = bytes;
                        error = null;
                        return true;
                    }

                    if (!TryDecompress(bytes, out glbBytes, out error))
                    {
                        continue;
                    }

                    // Normalize both asset and mesh cache aliases for the next launch.
                    foreach (var normalizedPath in candidates)
                    {
                        if (!string.IsNullOrWhiteSpace(normalizedPath) && File.Exists(normalizedPath))
                        {
                            File.WriteAllBytes(normalizedPath, glbBytes);
                        }
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
            }

            return false;
        }

        public sealed class CacheNormalizationResult
        {
            public int NormalizedFiles;
            public int Errors;
            public string LastError;
        }
    }
}
