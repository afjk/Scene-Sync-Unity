using System;
using System.Collections;
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
    }
}
