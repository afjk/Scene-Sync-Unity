using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afjk.SceneSync;
using UnityEngine;

namespace SceneSync.UnityClient
{
    /// <summary>
    /// Applies the viewer-only SceneSync policy and owns headset lifecycle reconnects.
    /// A platform-independent 3D connection UI can call Connect and Disconnect.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SceneSyncManager))]
    public sealed class SceneSyncClientController : MonoBehaviour
    {
        [SerializeField] private SceneSyncManager sceneSyncManager;
        [SerializeField] private string room = "";
        [SerializeField] private string nickname = "XR Client";
        [SerializeField] private bool connectOnStart = true;
        [SerializeField, Min(0f)] private float resumeReconnectDelay = 0.5f;

        private bool connectionInProgress;
        private bool reconnectAfterResume;
        private Coroutine reconnectCoroutine;
        private Task<SceneSyncGzipCarrierCompatibility.CacheNormalizationResult> cachePreparationTask;
        private readonly HashSet<string> gzipCarrierRecoveries = new HashSet<string>();

        public SceneSyncManager Manager => sceneSyncManager;
        public string Room => sceneSyncManager != null ? sceneSyncManager.Room : room;
        public string ConfiguredRoom => room;
        public string Nickname => nickname;
        public bool ConnectOnStart
        {
            get => connectOnStart;
            set => connectOnStart = value;
        }
        public bool IsConnected => sceneSyncManager != null && sceneSyncManager.IsConnected;
        public bool IsConnecting => connectionInProgress;

        public event Action<bool> ConnectionChanged;

        private void Reset()
        {
            sceneSyncManager = GetComponent<SceneSyncManager>();
        }

        private void Awake()
        {
            if (sceneSyncManager == null)
            {
                sceneSyncManager = GetComponent<SceneSyncManager>();
            }

            ApplyViewerPolicy();
            sceneSyncManager.OnConnected += HandleConnected;
            sceneSyncManager.OnDisconnected += HandleDisconnected;
            sceneSyncManager.OnObjectAdded += HandleObjectAdded;

            var cacheDirectory = SceneSyncGzipCarrierCompatibility.GetPersistentCacheDirectory(
                Application.persistentDataPath);
            cachePreparationTask = SceneSyncGzipCarrierCompatibility.NormalizePersistentCacheAsync(
                cacheDirectory);
        }

        private void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }
        }

        private void OnDestroy()
        {
            if (sceneSyncManager == null)
            {
                return;
            }

            sceneSyncManager.OnConnected -= HandleConnected;
            sceneSyncManager.OnDisconnected -= HandleDisconnected;
            sceneSyncManager.OnObjectAdded -= HandleObjectAdded;
        }

        public void Configure(SceneSyncManager manager, string configuredRoom, string configuredNickname)
        {
            sceneSyncManager = manager;
            SetConnectionSettings(configuredRoom, configuredNickname);
            ApplyViewerPolicy();
        }

        public void SetConnectionSettings(string configuredRoom, string configuredNickname)
        {
            room = configuredRoom?.Trim() ?? string.Empty;
            nickname = string.IsNullOrWhiteSpace(configuredNickname)
                ? "XR Client"
                : configuredNickname.Trim();
            ApplyViewerPolicy();
        }

        public void Connect()
        {
            _ = ConnectAsync();
        }

        public async Task ConnectAsync()
        {
            if (sceneSyncManager == null || connectionInProgress || sceneSyncManager.IsConnected)
            {
                return;
            }

            ApplyViewerPolicy();
            connectionInProgress = true;

            try
            {
                if (cachePreparationTask != null)
                {
                    var normalization = await cachePreparationTask;
                    cachePreparationTask = null;
                    if (normalization.NormalizedFiles > 0)
                    {
                        Debug.Log(
                            "[SceneSyncClient] Normalized gzip GLB cache files before connecting: "
                            + normalization.NormalizedFiles,
                            this);
                    }
                    if (normalization.Errors > 0)
                    {
                        Debug.LogWarning(
                            "[SceneSyncClient] Some gzip GLB cache files could not be normalized: errors="
                            + normalization.Errors + ", lastError=" + normalization.LastError,
                            this);
                    }
                }

                await sceneSyncManager.Connect();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                connectionInProgress = false;
            }
        }

        public void Disconnect()
        {
            reconnectAfterResume = false;
            StopReconnectCoroutine();

            if (sceneSyncManager != null)
            {
                sceneSyncManager.Disconnect();
            }
        }

        private void ApplyViewerPolicy()
        {
            if (sceneSyncManager == null)
            {
                return;
            }

            sceneSyncManager.ConfiguredRoom = room;
            sceneSyncManager.Nickname = nickname;
            sceneSyncManager.AutoConnect = false;
            sceneSyncManager.SyncHierarchy = false;
            sceneSyncManager.PlaybackClockFollowPolicy = SceneSyncPlaybackClockFollowPolicy.FollowerOnly;
            sceneSyncManager.AllowPlaybackClockControl = false;
        }

        private void OnApplicationPause(bool paused)
        {
            // The Editor reports focus changes as application pauses while CLI
            // automation is inspecting Play Mode. Headset lifecycle reconnects
            // are only required in a player build.
            if (Application.isEditor)
            {
                return;
            }

            if (paused)
            {
                reconnectAfterResume = IsConnected || connectionInProgress || connectOnStart;
                StopReconnectCoroutine();

                if (sceneSyncManager != null && (IsConnected || connectionInProgress))
                {
                    sceneSyncManager.Disconnect();
                }

                return;
            }

            if (reconnectAfterResume && isActiveAndEnabled)
            {
                StopReconnectCoroutine();
                reconnectCoroutine = StartCoroutine(ReconnectAfterDelay());
            }
        }

        private IEnumerator ReconnectAfterDelay()
        {
            if (resumeReconnectDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(resumeReconnectDelay);
            }

            reconnectCoroutine = null;
            reconnectAfterResume = false;
            Connect();
        }

        private void StopReconnectCoroutine()
        {
            if (reconnectCoroutine == null)
            {
                return;
            }

            StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = null;
        }

        private void HandleConnected()
        {
            connectionInProgress = false;
            ConnectionChanged?.Invoke(true);
        }

        private void HandleDisconnected()
        {
            connectionInProgress = false;
            ConnectionChanged?.Invoke(false);
        }

        private void HandleObjectAdded(string objectId, GameObject sceneObject)
        {
            if (sceneObject == null)
            {
                return;
            }

            var upgradedMaterials = new HashSet<Material>();
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");

            foreach (var renderer in sceneObject.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !upgradedMaterials.Add(material) || material.shader == null)
                    {
                        continue;
                    }

                    var shaderName = material.shader.name;
                    if (shaderName == "Standard" && litShader != null)
                    {
                        UpgradeLegacyMaterial(material, litShader);
                    }
                    else if (shaderName == "Unlit/Texture" && unlitShader != null)
                    {
                        UpgradeLegacyMaterial(material, unlitShader);
                    }
                }
            }

            TryRecoverGzipGaussianSplat(objectId, sceneObject);
        }

        private void TryRecoverGzipGaussianSplat(string objectId, GameObject sceneObject)
        {
            if (sceneObject.GetComponentInChildren<SceneSyncGaussianSplatMarker>(true) != null
                || string.IsNullOrWhiteSpace(objectId)
                || !gzipCarrierRecoveries.Add(objectId))
            {
                return;
            }

            var wireMetadata = sceneObject.GetComponent<SceneSyncWireMetadata>();
            var assetJson = wireMetadata != null ? wireMetadata.AssetJson : null;
            if (!string.Equals(
                    SceneSyncWireJson.ExtractString(assetJson, "carrierEncoding"),
                    "gzip",
                    StringComparison.OrdinalIgnoreCase))
            {
                gzipCarrierRecoveries.Remove(objectId);
                return;
            }

            StartCoroutine(RecoverGzipGaussianSplat(objectId, sceneObject, assetJson));
        }

        private IEnumerator RecoverGzipGaussianSplat(
            string objectId,
            GameObject sceneObject,
            string assetJson)
        {
            var cacheDirectory = SceneSyncGzipCarrierCompatibility.GetPersistentCacheDirectory(
                Application.persistentDataPath);
            var assetId = SceneSyncWireJson.ExtractString(assetJson, "assetId");
            var meshPath = SceneSyncWireJson.ExtractString(assetJson, "meshPath");
            byte[] glbBytes = null;
            string loadError = null;

            var loadTask = Task.Run(() =>
            {
                SceneSyncGzipCarrierCompatibility.TryLoadCachedGlb(
                    cacheDirectory,
                    assetId,
                    meshPath,
                    out glbBytes,
                    out loadError);
            });

            while (!loadTask.IsCompleted)
            {
                yield return null;
            }

            if (loadTask.IsFaulted)
            {
                loadError = loadTask.Exception?.GetBaseException().Message;
            }

            if (sceneObject == null)
            {
                gzipCarrierRecoveries.Remove(objectId);
                yield break;
            }

            if (glbBytes == null)
            {
                Debug.LogWarning(
                    "[SceneSyncClient] Failed to recover gzip GLB carrier: objectId=" + objectId
                    + ", error=" + loadError,
                    sceneObject);
                gzipCarrierRecoveries.Remove(objectId);
                yield break;
            }

            if (!SceneSyncGaussianSplatBackend.IsGaussianSplatGlb(glbBytes, out var splatInfo))
            {
                gzipCarrierRecoveries.Remove(objectId);
                yield break;
            }

            var visual = SceneSyncGaussianSplatBackend.CreateVisual(glbBytes, splatInfo);
            if (!visual.Ok || visual.Visual == null)
            {
                Debug.LogWarning(
                    "[SceneSyncClient] Failed to render recovered gzip Gaussian Splat: objectId="
                    + objectId + ", reason=" + visual.Reason,
                    sceneObject);
                gzipCarrierRecoveries.Remove(objectId);
                yield break;
            }

            RemoveFallbackPrimitiveVisual(sceneObject);

            var importedGlbRoot = new GameObject("ImportedGlbRoot");
            importedGlbRoot.transform.SetParent(sceneObject.transform, worldPositionStays: false);
            importedGlbRoot.transform.localPosition = Vector3.zero;
            importedGlbRoot.transform.localRotation =
                SceneSyncWireJson.ExtractString(assetJson, "visualBasis") != "unity"
                    ? Quaternion.Euler(0f, 180f, 0f)
                    : Quaternion.identity;
            importedGlbRoot.transform.localScale = Vector3.one;
            visual.Visual.transform.SetParent(importedGlbRoot.transform, worldPositionStays: false);

            Debug.Log(
                "[SceneSyncClient] Recovered gzip Gaussian Splat: objectId=" + objectId
                + ", source=" + visual.Source
                + ", backend=" + visual.BackendName
                + ", points=" + visual.PointCount,
                sceneObject);
            gzipCarrierRecoveries.Remove(objectId);
        }

        private static void RemoveFallbackPrimitiveVisual(GameObject sceneObject)
        {
            foreach (var renderer in sceneObject.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
                Destroy(renderer);
            }

            foreach (var collider in sceneObject.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }

            foreach (var meshFilter in sceneObject.GetComponentsInChildren<MeshFilter>(true))
            {
                Destroy(meshFilter);
            }
        }

        private static void UpgradeLegacyMaterial(Material material, Shader targetShader)
        {
            var color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            var textureScale = material.HasProperty("_MainTex")
                ? material.GetTextureScale("_MainTex")
                : Vector2.one;
            var textureOffset = material.HasProperty("_MainTex")
                ? material.GetTextureOffset("_MainTex")
                : Vector2.zero;

            material.shader = targetShader;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (texture != null && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", textureScale);
                material.SetTextureOffset("_BaseMap", textureOffset);
            }

            if (color.a < 0.999f)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                SetFloatIfPresent(material, "_Surface", 1f);
                SetFloatIfPresent(material, "_Blend", 0f);
                SetFloatIfPresent(material, "_SrcBlend", 5f);
                SetFloatIfPresent(material, "_DstBlend", 10f);
                SetFloatIfPresent(material, "_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = 3000;
            }
            else
            {
                material.SetOverrideTag("RenderType", "Opaque");
                SetFloatIfPresent(material, "_Surface", 0f);
                SetFloatIfPresent(material, "_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = -1;
            }
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
    }
}
