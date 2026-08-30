using System;
using System.Collections.Generic;
using Afjk.SceneSync;
using Afjk.SceneSync.Rapier;
using SceneSync.UnityClient;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SceneSync.UnityClient.Editor
{
    public static class SceneSyncProjectSetup
    {
        private const string ClientRoot = "Assets/SceneSyncClient";
        private const string SceneFolder = ClientRoot + "/Scenes";
        private const string SettingsFolder = ClientRoot + "/Settings";
        private const string ScenePath = SceneFolder + "/SceneSyncClient.unity";
        private const string RigPrefabPath =
            "Packages/com.styly.styly-xr-rig/Runtime/STYLY XR Rig.prefab";
        private const string SourceRendererPath =
            "Packages/com.styly.styly-xr-rig/Runtime/Settings/STYLY_Mobile_Renderer.asset";
        private const string SourceRenderPipelinePath =
            "Packages/com.styly.styly-xr-rig/Runtime/Settings/STYLY_Mobile_RPAsset.asset";
        private const string RendererPath = SettingsFolder + "/SceneSyncMobileRenderer.asset";
        private const string RenderPipelineAssetPath = SettingsFolder + "/SceneSyncMobileRPAsset.asset";
        private const string RuntimeShaderVariantsPath =
            SettingsFolder + "/SceneSyncRuntimeShaders.shadervariants";
        private const string GsplatUrpFeatureTypeName = "Gsplat.GsplatURPFeature";
        private static readonly string[] RequiredRuntimeShaderNames =
        {
            "Standard",
            "Unlit/Texture",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Shader Graphs/glTF-pbrMetallicRoughness",
            "Shader Graphs/glTF-unlit",
            "Shader Graphs/glTF-pbrSpecularGlossiness",
            "Shader Graphs/glTF-pbrMetallicRoughness-Clearcoat",
            "Gsplat/Standard",
            "Gsplat/Global",
        };
        private static readonly string[] GltfAlphaTestShaderNames =
        {
            "Shader Graphs/glTF-pbrMetallicRoughness",
            "Shader Graphs/glTF-unlit",
            "Shader Graphs/glTF-pbrSpecularGlossiness",
            "Shader Graphs/glTF-pbrMetallicRoughness-Clearcoat",
        };
        private const string DefaultRapierScenePhysicsJson =
            "{\"version\":1,\"enabled\":true,\"duration\":10,\"worldOptions\":{" +
            "\"gravity\":[0,-9.81,0],\"ground\":null,\"timestep\":0.016666666666666666}}";

        [MenuItem("Tools/Scene Sync XR Client/Create Minimal Project Setup")]
        public static void ConfigureAndCreateScene()
        {
            EnsureFolder(ClientRoot, "Scenes");
            EnsureFolder(ClientRoot, "Settings");
            ConfigureProject();
            CreateScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SceneSyncClient] Minimal project setup created: {ScenePath}");
        }

        [MenuItem("Tools/Scene Sync XR Client/Build PICO APK")]
        public static void BuildPicoApk()
        {
            EnsureRuntimeRenderingConfiguration();
            AssetDatabase.SaveAssets();

            var enabledScenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    enabledScenes.Add(scene.path);
                }
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = enabledScenes.ToArray(),
                locationPathName = "Build/pico.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            Debug.Log(
                "[SceneSyncClient] PICO build result=" + report.summary.result
                + ", errors=" + report.summary.totalErrors
                + ", warnings=" + report.summary.totalWarnings
                + ", size=" + report.summary.totalSize
                + ", time=" + report.summary.totalTime);
        }

        [MenuItem("Tools/Scene Sync XR Client/Add or Update Connection Menu")]
        public static void AddOrUpdateConnectionMenu()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var runtime = FindRoot(scene, "SceneSyncRuntime");
            var rig = FindRoot(scene, "STYLY XR Rig");
            if (runtime == null || rig == null)
            {
                throw new MissingReferenceException(
                    "SceneSyncRuntime or STYLY XR Rig was not found in the client scene.");
            }

            var controller = runtime.GetComponent<SceneSyncClientController>();
            var camera = rig.GetComponentInChildren<Camera>(true);
            if (controller == null || camera == null)
            {
                throw new MissingReferenceException(
                    "The Scene Sync controller or XR camera was not found in the client scene.");
            }

            controller.ConnectOnStart = false;
            CreateConnectionMenu(scene, controller, camera);
            EnsureXrEventSystem(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[SceneSyncClient] Connection menu added or updated: " + ScenePath);
        }

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "afjk";
            PlayerSettings.productName = "Scene Sync XR Client";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;

            EnsureRuntimeRenderingConfiguration();
        }

        public static void EnsureRuntimeRenderingConfiguration()
        {
            var renderPipeline = GetOrCreateRenderPipeline();
            GraphicsSettings.defaultRenderPipeline = renderPipeline;

            var originalQualityLevel = QualitySettings.GetQualityLevel();
            for (var qualityLevel = 0; qualityLevel < QualitySettings.names.Length; qualityLevel++)
            {
                QualitySettings.SetQualityLevel(qualityLevel, false);
                QualitySettings.renderPipeline = renderPipeline;
            }
            QualitySettings.SetQualityLevel(originalQualityLevel, false);

            EnsureRuntimeShadersIncluded();
            EnsureRuntimeShaderVariantsIncluded();
        }

        private static void EnsureRuntimeShadersIncluded()
        {
            var settingsObjects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settingsObjects == null || settingsObjects.Length == 0)
            {
                throw new MissingReferenceException("GraphicsSettings.asset could not be loaded.");
            }

            var serializedSettings = new SerializedObject(settingsObjects[0]);
            var shaders = serializedSettings.FindProperty("m_AlwaysIncludedShaders");
            if (shaders == null || !shaders.isArray)
            {
                throw new MissingMemberException("GraphicsSettings.m_AlwaysIncludedShaders was not found.");
            }

            foreach (var shaderName in RequiredRuntimeShaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    throw new MissingReferenceException("Required runtime shader was not found: " + shaderName);
                }

                var alreadyIncluded = false;
                for (var index = 0; index < shaders.arraySize; index++)
                {
                    if (shaders.GetArrayElementAtIndex(index).objectReferenceValue == shader)
                    {
                        alreadyIncluded = true;
                        break;
                    }
                }

                if (alreadyIncluded)
                {
                    continue;
                }

                var newIndex = shaders.arraySize;
                shaders.InsertArrayElementAtIndex(newIndex);
                shaders.GetArrayElementAtIndex(newIndex).objectReferenceValue = shader;
            }

            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureRuntimeShaderVariantsIncluded()
        {
            EnsureFolder(ClientRoot, "Settings");

            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(
                RuntimeShaderVariantsPath);
            if (collection == null)
            {
                collection = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(collection, RuntimeShaderVariantsPath);
            }

            collection.Clear();
            foreach (var shaderName in GltfAlphaTestShaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning(
                        "[SceneSyncClient] Runtime alpha-test shader was not found: " + shaderName);
                    continue;
                }

                AddShaderVariant(
                    collection,
                    new ShaderVariantCollection.ShaderVariant(
                        shader,
                        PassType.ScriptableRenderPipeline,
                        "_ALPHATEST_ON"));
                AddShaderVariant(
                    collection,
                    new ShaderVariantCollection.ShaderVariant(
                        shader,
                        PassType.ShadowCaster,
                        "_ALPHATEST_ON"));
            }
            EditorUtility.SetDirty(collection);

            var settingsObjects = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/GraphicsSettings.asset");
            if (settingsObjects == null || settingsObjects.Length == 0)
            {
                throw new MissingReferenceException("GraphicsSettings.asset could not be loaded.");
            }

            var graphicsSettings = new SerializedObject(settingsObjects[0]);
            var preloadedShaders = graphicsSettings.FindProperty("m_PreloadedShaders");
            if (preloadedShaders == null || !preloadedShaders.isArray)
            {
                throw new MissingMemberException(
                    "GraphicsSettings.m_PreloadedShaders was not found.");
            }

            for (var index = 0; index < preloadedShaders.arraySize; index++)
            {
                if (preloadedShaders.GetArrayElementAtIndex(index).objectReferenceValue == collection)
                {
                    graphicsSettings.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
            }

            var newIndex = preloadedShaders.arraySize;
            preloadedShaders.InsertArrayElementAtIndex(newIndex);
            preloadedShaders.GetArrayElementAtIndex(newIndex).objectReferenceValue = collection;
            graphicsSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddShaderVariant(
            ShaderVariantCollection collection,
            ShaderVariantCollection.ShaderVariant variant)
        {
            if (!collection.Contains(variant))
            {
                collection.Add(variant);
            }
        }

        private static UniversalRenderPipelineAsset GetOrCreateRenderPipeline()
        {
            var sourceRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SourceRendererPath);
            if (sourceRenderer == null)
            {
                throw new MissingReferenceException($"STYLY renderer was not found: {SourceRendererPath}");
            }

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = UnityEngine.Object.Instantiate(sourceRenderer);
                renderer.name = "SceneSyncMobileRenderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            EnsureGaussianSplatRendererFeature(renderer);

            var sourcePipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SourceRenderPipelinePath);
            if (sourcePipeline == null)
            {
                throw new MissingReferenceException(
                    $"STYLY render pipeline was not found: {SourceRenderPipelinePath}");
            }

            var renderPipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineAssetPath);
            if (renderPipeline == null)
            {
                renderPipeline = UnityEngine.Object.Instantiate(sourcePipeline);
                renderPipeline.name = "SceneSyncMobileRPAsset";
                AssetDatabase.CreateAsset(renderPipeline, RenderPipelineAssetPath);
            }
            else
            {
                // Keep PICO/STYLY-specific render scale, MSAA, color precision and alpha
                // behavior in sync. UniversalRenderPipelineAsset.Create(renderer) starts from
                // generic defaults and breaks the headset passthrough background.
                EditorUtility.CopySerialized(sourcePipeline, renderPipeline);
                renderPipeline.name = "SceneSyncMobileRPAsset";
            }

            var serializedPipeline = new SerializedObject(renderPipeline);
            var rendererDataList = serializedPipeline.FindProperty("m_RendererDataList");
            if (rendererDataList == null || !rendererDataList.isArray)
            {
                throw new MissingMemberException(
                    "UniversalRenderPipelineAsset.m_RendererDataList was not found.");
            }
            rendererDataList.arraySize = 1;
            rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            var defaultRendererIndex = serializedPipeline.FindProperty("m_DefaultRendererIndex");
            if (defaultRendererIndex != null)
            {
                defaultRendererIndex.intValue = 0;
            }
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderPipeline);
            return renderPipeline;
        }

        private static void EnsureGaussianSplatRendererFeature(UniversalRendererData renderer)
        {
            if (renderer == null)
            {
                throw new MissingReferenceException($"Scene Sync renderer was not found: {RendererPath}");
            }

            foreach (var feature in renderer.rendererFeatures)
            {
                if (feature != null && feature.GetType().FullName == GsplatUrpFeatureTypeName)
                {
                    return;
                }
            }

            var featureType = Type.GetType(GsplatUrpFeatureTypeName + ", Gsplat", false);
            if (featureType == null || !typeof(ScriptableRendererFeature).IsAssignableFrom(featureType))
            {
                throw new InvalidOperationException(
                    "UnitySplats is installed, but GsplatURPFeature could not be loaded.");
            }

            var gsplatFeature = ScriptableObject.CreateInstance(featureType) as ScriptableRendererFeature;
            if (gsplatFeature == null)
            {
                throw new InvalidOperationException("Failed to create the UnitySplats URP renderer feature.");
            }

            gsplatFeature.name = "Gsplat URP Feature";
            gsplatFeature.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(gsplatFeature, renderer);
            renderer.rendererFeatures.Add(gsplatFeature);
            EditorUtility.SetDirty(renderer);
        }

        private static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null)
            {
                throw new MissingReferenceException($"STYLY XR Rig prefab was not found: {RigPrefabPath}");
            }

            var rigInstance = PrefabUtility.InstantiatePrefab(rigPrefab, scene) as GameObject;
            if (rigInstance == null)
            {
                throw new MissingReferenceException("Failed to instantiate the STYLY XR Rig prefab.");
            }
            rigInstance.name = "STYLY XR Rig";

            var sceneSyncRoot = new GameObject("SceneSyncRoot");
            SceneManager.MoveGameObjectToScene(sceneSyncRoot, scene);

            var remoteObjects = new GameObject("RemoteObjects");
            remoteObjects.transform.SetParent(sceneSyncRoot.transform, false);

            var remoteAvatars = new GameObject("RemoteAvatars");
            SceneManager.MoveGameObjectToScene(remoteAvatars, scene);

            var runtime = new GameObject("SceneSyncRuntime");
            SceneManager.MoveGameObjectToScene(runtime, scene);
            var manager = runtime.AddComponent<SceneSyncManager>();
            var controller = runtime.AddComponent<SceneSyncClientController>();

            manager.PresenceUrl = "wss://afjk.jp/presence";
            manager.ConfiguredRoom = string.Empty;
            manager.Nickname = "XR Client";
            manager.AutoConnect = false;
            manager.SyncHierarchy = false;
            manager.IncludeManagerChildren = false;
            manager.TemporaryRoot = remoteObjects.transform;
            manager.PlaybackClockFollowPolicy = SceneSyncPlaybackClockFollowPolicy.FollowerOnly;
            manager.AllowPlaybackClockControl = false;

            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("_syncRoot").objectReferenceValue = sceneSyncRoot.transform;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            controller.Configure(manager, string.Empty, "XR Client");
            controller.ConnectOnStart = false;

            var physicsMetadata = runtime.AddComponent<SceneSyncPhysicsMetadata>();
            physicsMetadata.ConfigureScenePhysics(DefaultRapierScenePhysicsJson);

            var rapierBridge = runtime.AddComponent<SceneSyncRapierBridge>();
            rapierBridge.PlaybackClockManager = manager;
            rapierBridge.BodyRoot = remoteObjects.transform;
            rapierBridge.AutoRun = true;
            rapierBridge.UseSceneClock = true;
            rapierBridge.RequireSceneClock = false;
            rapierBridge.PreferManagerPlaybackClock = true;
            rapierBridge.PreserveMotionOnRebuild = false;

            var rapierGuard = runtime.AddComponent<SceneSyncRapierPlatformGuard>();
            rapierGuard.Configure(rapierBridge);

            var rapierInteraction = runtime.AddComponent<SceneSyncRapierInteractionController>();
            rapierInteraction.Bridge = rapierBridge;
            rapierInteraction.TargetCamera = rigInstance.GetComponentInChildren<Camera>(true);

            CreateConnectionMenu(
                scene,
                controller,
                rigInstance.GetComponentInChildren<Camera>(true));
            EnsureXrEventSystem(scene);

            var lightObject = new GameObject("Directional Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = runtime;
        }

        private static void CreateConnectionMenu(
            Scene scene,
            SceneSyncClientController controller,
            Camera camera)
        {
            var existingPanel = FindRoot(scene, "ConnectionPanel3D");
            if (existingPanel != null)
            {
                UnityEngine.Object.DestroyImmediate(existingPanel);
            }

            var panelRoot = new GameObject(
                "ConnectionPanel3D",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(TrackedDeviceGraphicRaycaster),
                typeof(SceneSyncConnectionPanel));
            SceneManager.MoveGameObjectToScene(panelRoot, scene);

            var rootRect = panelRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(780f, 500f);
            rootRect.localScale = Vector3.one * 0.001f;
            panelRoot.transform.SetPositionAndRotation(
                new Vector3(0f, 1.4f, 1.2f),
                Quaternion.identity);

            var canvas = panelRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 100;

            var scaler = panelRoot.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            scaler.referencePixelsPerUnit = 100f;

            var fullPanel = CreateImage(
                "FullPanel",
                rootRect,
                new Vector2(780f, 500f),
                Vector2.zero,
                new Color(0.035f, 0.055f, 0.09f, 0.96f));
            CreateText(
                "Title",
                fullPanel.rectTransform,
                "Scene Sync",
                38,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Vector2(430f, 55f),
                new Vector2(-135f, 215f),
                Color.white);

            var minimizeButton = CreateButton(
                "MinimizeButton",
                fullPanel.rectTransform,
                "Minimize",
                new Vector2(145f, 44f),
                new Vector2(292f, 215f),
                new Color(0.16f, 0.2f, 0.29f, 1f));

            CreateText(
                "ConnectionLabel",
                fullPanel.rectTransform,
                "Connection",
                25,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Vector2(180f, 45f),
                new Vector2(-275f, 150f),
                new Color(0.7f, 0.8f, 0.93f, 1f));
            var connectionStatus = CreateText(
                "ConnectionStatus",
                fullPanel.rectTransform,
                "Disconnected",
                25,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Vector2(430f, 45f),
                new Vector2(90f, 150f),
                Color.white);

            CreateText(
                "RoomLabel",
                fullPanel.rectTransform,
                "Room",
                25,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Vector2(180f, 58f),
                new Vector2(-275f, 82f),
                new Color(0.7f, 0.8f, 0.93f, 1f));
            var roomInput = CreateInputField(
                "RoomInput",
                fullPanel.rectTransform,
                "LAN (automatic) or room code",
                new Vector2(500f, 58f),
                new Vector2(95f, 82f));

            CreateText(
                "NicknameLabel",
                fullPanel.rectTransform,
                "Nickname",
                25,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Vector2(180f, 58f),
                new Vector2(-275f, 12f),
                new Color(0.7f, 0.8f, 0.93f, 1f));
            var nicknameInput = CreateInputField(
                "NicknameInput",
                fullPanel.rectTransform,
                "Device name",
                new Vector2(500f, 58f),
                new Vector2(95f, 12f));

            CreateText(
                "KeyboardHint",
                fullPanel.rectTransform,
                "Point and trigger to edit. Text input uses the system keyboard.",
                20,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Vector2(690f, 38f),
                new Vector2(0f, -48f),
                new Color(0.62f, 0.7f, 0.82f, 1f));
            var errorText = CreateText(
                "ErrorText",
                fullPanel.rectTransform,
                string.Empty,
                21,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Vector2(690f, 44f),
                new Vector2(0f, -90f),
                new Color(1f, 0.48f, 0.4f, 1f));

            var connectionButton = CreateButton(
                "ConnectionButton",
                fullPanel.rectTransform,
                "Connect",
                new Vector2(270f, 58f),
                new Vector2(205f, -166f),
                new Color(0.08f, 0.43f, 0.9f, 1f));

            var minimizedPanel = CreateImage(
                "MinimizedPanel",
                rootRect,
                new Vector2(300f, 112f),
                Vector2.zero,
                new Color(0.035f, 0.055f, 0.09f, 0.96f));
            var restoreButton = CreateButton(
                "RestoreButton",
                minimizedPanel.rectTransform,
                string.Empty,
                new Vector2(284f, 96f),
                Vector2.zero,
                new Color(0.08f, 0.28f, 0.52f, 1f));
            var minimizedStatus = CreateText(
                "MinimizedStatus",
                restoreButton.GetComponent<RectTransform>(),
                "Scene Sync\nDisconnected",
                25,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                new Vector2(270f, 86f),
                Vector2.zero,
                Color.white);
            minimizedPanel.gameObject.SetActive(false);

            var panel = panelRoot.GetComponent<SceneSyncConnectionPanel>();
            panel.Configure(
                controller,
                camera,
                fullPanel.gameObject,
                minimizedPanel.gameObject,
                roomInput,
                nicknameInput,
                connectionButton,
                minimizeButton,
                restoreButton,
                connectionStatus,
                minimizedStatus,
                errorText);
        }

        private static void EnsureXrEventSystem(Scene scene)
        {
            EventSystem eventSystem = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                eventSystem = root.GetComponentInChildren<EventSystem>(true);
                if (eventSystem != null)
                {
                    break;
                }
            }

            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject(
                    "EventSystem",
                    typeof(EventSystem),
                    typeof(XRUIInputModule));
                SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
                return;
            }

            if (eventSystem.GetComponent<XRUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
            }
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }
            return null;
        }

        private static Image CreateImage(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 position,
            Color color)
        {
            var gameObject = CreateUiObject(name, parent, size, position);
            var image = gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(
            string name,
            RectTransform parent,
            string value,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Vector2 size,
            Vector2 position,
            Color color)
        {
            var gameObject = CreateUiObject(name, parent, size, position);
            var text = gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static InputField CreateInputField(
            string name,
            RectTransform parent,
            string placeholderValue,
            Vector2 size,
            Vector2 position)
        {
            var background = CreateImage(
                name,
                parent,
                size,
                position,
                new Color(0.11f, 0.14f, 0.21f, 1f));
            var inputField = background.gameObject.AddComponent<InputField>();
            inputField.targetGraphic = background;
            inputField.characterLimit = 80;
            inputField.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            inputField.shouldActivateOnSelect = true;
            inputField.shouldHideMobileInput = false;

            var text = CreateText(
                "Text",
                background.rectTransform,
                string.Empty,
                25,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                size - new Vector2(28f, 8f),
                Vector2.zero,
                Color.white);
            text.supportRichText = false;
            text.raycastTarget = false;

            var placeholder = CreateText(
                "Placeholder",
                background.rectTransform,
                placeholderValue,
                23,
                FontStyle.Italic,
                TextAnchor.MiddleLeft,
                size - new Vector2(28f, 8f),
                Vector2.zero,
                new Color(0.52f, 0.59f, 0.7f, 1f));
            placeholder.raycastTarget = false;

            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            return inputField;
        }

        private static Button CreateButton(
            string name,
            RectTransform parent,
            string label,
            Vector2 size,
            Vector2 position,
            Color color)
        {
            var image = CreateImage(name, parent, size, position, color);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.2f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.18f, 0.2f, 0.24f, 0.7f);
            button.colors = colors;

            if (!string.IsNullOrEmpty(label))
            {
                CreateText(
                    "Label",
                    image.rectTransform,
                    label,
                    24,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    size - new Vector2(8f, 8f),
                    Vector2.zero,
                    Color.white);
            }
            return button;
        }

        private static GameObject CreateUiObject(
            string name,
            RectTransform parent,
            Vector2 size,
            Vector2 position)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return gameObject;
        }

        private static void EnsureFolder(string parent, string child)
        {
            var folder = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }

    internal sealed class SceneSyncBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            SceneSyncProjectSetup.EnsureRuntimeRenderingConfiguration();
            AssetDatabase.SaveAssets();
        }
    }
}
