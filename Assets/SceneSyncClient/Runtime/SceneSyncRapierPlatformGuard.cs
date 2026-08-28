using Afjk.SceneSync.Rapier;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SceneSync.UnityClient
{
    /// <summary>
    /// Rapier native pluginが同梱されていないplatformではsimulationだけを無効化する。
    /// SceneSyncPhysicsMetadataは残るため、physics metadataの受信・保持は継続する。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class SceneSyncRapierPlatformGuard : MonoBehaviour
    {
        [SerializeField] private SceneSyncRapierBridge bridge;

        public SceneSyncRapierBridge Bridge => bridge;
        public bool IsNativeRuntimeSupported => IsSupportedPlatform;

        public void Configure(SceneSyncRapierBridge targetBridge)
        {
            bridge = targetBridge;
            ApplyPlatformSupport();
        }

        private void Awake()
        {
            ApplyPlatformSupport();
        }

        private void ApplyPlatformSupport()
        {
            if (bridge == null)
            {
                bridge = GetComponent<SceneSyncRapierBridge>();
            }

            if (bridge == null || IsSupportedPlatform)
            {
                return;
            }

            bridge.enabled = false;
            Debug.LogWarning(
                "[SceneSyncClient] Rapier native runtime is unavailable on this platform. " +
                "Physics metadata remains synchronized, but local simulation is disabled.",
                this);
        }

        private static bool IsSupportedPlatform
        {
            get
            {
#if UNITY_ANDROID
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
                return RuntimeInformation.ProcessArchitecture == Architecture.X64;
#else
                return false;
#endif
            }
        }
    }
}
