using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace SceneSync.UnityClient
{
    /// <summary>
    /// Small world-space Scene Sync connection menu for an XR viewer.
    /// Unity's InputField opens TouchScreenKeyboard on Android, so PICO uses
    /// the operating system keyboard instead of an in-world keyboard.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class SceneSyncConnectionPanel : MonoBehaviour
    {
        private const string RoomPreferenceKey = "SceneSync.Connection.Room";
        private const string NicknamePreferenceKey = "SceneSync.Connection.Nickname";
        private const float PlacementDistanceMeters = 1.2f;
        private static readonly Color ConnectButtonColor = new(0.08f, 0.43f, 0.9f, 1f);
        private static readonly Color DisconnectButtonColor = new(0.55f, 0.16f, 0.17f, 1f);

        [SerializeField] private SceneSyncClientController controller;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private GameObject fullPanel;
        [SerializeField] private GameObject minimizedPanel;
        [SerializeField] private InputField roomInput;
        [SerializeField] private InputField nicknameInput;
        [FormerlySerializedAs("connectButton")]
        [SerializeField] private Button connectionButton;
        [SerializeField] private Button minimizeButton;
        [SerializeField] private Button restoreButton;
        [SerializeField] private Text connectionStatus;
        [SerializeField] private Text minimizedStatus;
        [SerializeField] private Text errorText;

        private string lastVisualState;
        private bool listenersRegistered;

        public SceneSyncClientController Controller => controller;
        public Camera TargetCamera => targetCamera;
        public GameObject FullPanel => fullPanel;
        public GameObject MinimizedPanel => minimizedPanel;
        public InputField RoomInput => roomInput;
        public InputField NicknameInput => nicknameInput;
        public Button ConnectionButton => connectionButton;
        public Text ConnectionStatus => connectionStatus;
        public bool IsMinimized => minimizedPanel != null && minimizedPanel.activeSelf;

        public void Configure(
            SceneSyncClientController configuredController,
            Camera configuredCamera,
            GameObject configuredFullPanel,
            GameObject configuredMinimizedPanel,
            InputField configuredRoomInput,
            InputField configuredNicknameInput,
            Button configuredConnectionButton,
            Button configuredMinimizeButton,
            Button configuredRestoreButton,
            Text configuredConnectionStatus,
            Text configuredMinimizedStatus,
            Text configuredErrorText)
        {
            controller = configuredController;
            targetCamera = configuredCamera;
            fullPanel = configuredFullPanel;
            minimizedPanel = configuredMinimizedPanel;
            roomInput = configuredRoomInput;
            nicknameInput = configuredNicknameInput;
            connectionButton = configuredConnectionButton;
            minimizeButton = configuredMinimizeButton;
            restoreButton = configuredRestoreButton;
            connectionStatus = configuredConnectionStatus;
            minimizedStatus = configuredMinimizedStatus;
            errorText = configuredErrorText;
        }

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<SceneSyncClientController>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            ConfigureNativeKeyboardFields();
            LoadFields();
            RegisterListeners();
            SetMinimized(false);
            RefreshVisuals(true);
        }

        private IEnumerator Start()
        {
            // XR tracking poses settle just after scene startup. Waiting two
            // frames prevents the menu from being placed using the prefab pose.
            yield return null;
            yield return null;
            Recenter();
        }

        private void OnEnable()
        {
            RegisterListeners();
            RefreshVisuals(true);
        }

        private void OnDisable()
        {
            UnregisterListeners();
        }

        private void Update()
        {
            RefreshVisuals(false);
        }

        public void OpenRoomKeyboard()
        {
            ActivateSystemKeyboard(roomInput);
        }

        public void OpenNicknameKeyboard()
        {
            ActivateSystemKeyboard(nicknameInput);
        }

        public void Recenter()
        {
            if (targetCamera == null)
            {
                return;
            }

            var forward = targetCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            transform.SetPositionAndRotation(
                targetCamera.transform.position + forward * PlacementDistanceMeters,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        public void SetMinimized(bool minimized)
        {
            if (fullPanel != null)
            {
                fullPanel.SetActive(!minimized);
            }
            if (minimizedPanel != null)
            {
                minimizedPanel.SetActive(minimized);
            }

            if (minimized)
            {
                roomInput?.DeactivateInputField();
                nicknameInput?.DeactivateInputField();
            }

            lastVisualState = null;
            RefreshVisuals(true);
        }

        private void ConfigureNativeKeyboardFields()
        {
            ConfigureNativeKeyboardField(roomInput);
            ConfigureNativeKeyboardField(nicknameInput);
        }

        private static void ConfigureNativeKeyboardField(InputField inputField)
        {
            if (inputField == null)
            {
                return;
            }

            inputField.lineType = InputField.LineType.SingleLine;
            inputField.inputType = InputField.InputType.Standard;
            inputField.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            inputField.shouldActivateOnSelect = true;
            inputField.shouldHideMobileInput = false;
        }

        private static void ActivateSystemKeyboard(InputField inputField)
        {
            if (inputField == null || !inputField.interactable)
            {
                return;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            }
            inputField.ActivateInputField();
        }

        private void LoadFields()
        {
            if (roomInput != null)
            {
                roomInput.text = PlayerPrefs.GetString(
                    RoomPreferenceKey,
                    controller != null ? controller.ConfiguredRoom : string.Empty);
            }

            if (nicknameInput != null)
            {
                var configuredNickname = controller != null ? controller.Nickname : string.Empty;
                var fallbackNickname = string.IsNullOrWhiteSpace(configuredNickname)
                    ? MakeDefaultNickname()
                    : configuredNickname;
                nicknameInput.text = PlayerPrefs.GetString(NicknamePreferenceKey, fallbackNickname);
            }
        }

        private static string MakeDefaultNickname()
        {
            var model = (SystemInfo.deviceModel ?? string.Empty).Trim();
            var lowerModel = model.ToLowerInvariant();
            if (lowerModel.Contains("pico") && lowerModel.Contains("ultra"))
            {
                return "PICO4Ultra";
            }
            if (lowerModel.Contains("pico"))
            {
                return "PICO";
            }
            return string.IsNullOrEmpty(model) ? "XR Client" : model;
        }

        private void RegisterListeners()
        {
            if (listenersRegistered)
            {
                return;
            }

            connectionButton?.onClick.AddListener(ToggleConnection);
            minimizeButton?.onClick.AddListener(Minimize);
            restoreButton?.onClick.AddListener(Restore);
            if (controller != null)
            {
                controller.ConnectionChanged += HandleConnectionChanged;
            }
            listenersRegistered = true;
        }

        private void UnregisterListeners()
        {
            if (!listenersRegistered)
            {
                return;
            }

            connectionButton?.onClick.RemoveListener(ToggleConnection);
            minimizeButton?.onClick.RemoveListener(Minimize);
            restoreButton?.onClick.RemoveListener(Restore);
            if (controller != null)
            {
                controller.ConnectionChanged -= HandleConnectionChanged;
            }
            listenersRegistered = false;
        }

        private void Connect()
        {
            if (controller == null || controller.IsConnected || controller.IsConnecting)
            {
                return;
            }

            var room = roomInput != null ? roomInput.text.Trim() : string.Empty;
            var nickname = nicknameInput != null ? nicknameInput.text.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(nickname))
            {
                SetError("Nickname is required.");
                return;
            }

            SetError(string.Empty);
            PlayerPrefs.SetString(RoomPreferenceKey, room);
            PlayerPrefs.SetString(NicknamePreferenceKey, nickname);
            PlayerPrefs.Save();

            controller.SetConnectionSettings(room, nickname);
            controller.Connect();
            RefreshVisuals(true);
        }

        private void ToggleConnection()
        {
            if (controller != null && controller.IsConnected)
            {
                Disconnect();
                return;
            }

            Connect();
        }

        private void Disconnect()
        {
            if (controller == null)
            {
                return;
            }

            controller.Disconnect();
            SetMinimized(false);
        }

        private void Minimize()
        {
            SetMinimized(true);
        }

        private void Restore()
        {
            SetMinimized(false);
        }

        private void HandleConnectionChanged(bool connected)
        {
            SetError(string.Empty);
            SetMinimized(connected);
        }

        private void SetError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message ?? string.Empty;
            }
        }

        private void RefreshVisuals(bool force)
        {
            var connected = controller != null && controller.IsConnected;
            var connecting = controller != null && controller.IsConnecting;
            var state = connected ? "Connected: " + controller.Room : connecting ? "Connecting..." : "Disconnected";
            var visualState = state + "|" + IsMinimized;
            if (!force && visualState == lastVisualState)
            {
                return;
            }
            lastVisualState = visualState;

            if (connectionStatus != null)
            {
                connectionStatus.text = state;
            }
            if (minimizedStatus != null)
            {
                minimizedStatus.text = connected ? "Scene Sync\n" + controller.Room : "Scene Sync\n" + state;
            }
            if (roomInput != null)
            {
                roomInput.interactable = !connected && !connecting;
            }
            if (nicknameInput != null)
            {
                nicknameInput.interactable = !connected && !connecting;
            }
            RefreshConnectionButton(connected, connecting);
        }

        private void RefreshConnectionButton(bool connected, bool connecting)
        {
            if (connectionButton == null)
            {
                return;
            }

            connectionButton.interactable = !connecting;
            var color = connected ? DisconnectButtonColor : ConnectButtonColor;
            var colors = connectionButton.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
            colors.selectedColor = colors.highlightedColor;
            connectionButton.colors = colors;

            var label = connectionButton.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = connected ? "Disconnect" : connecting ? "Connecting..." : "Connect";
            }
        }
    }
}
