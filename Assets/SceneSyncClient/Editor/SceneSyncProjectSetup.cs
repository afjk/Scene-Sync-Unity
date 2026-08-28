using System;
using Afjk.SceneSync;
using Afjk.SceneSync.Rapier;
using SceneSync.UnityClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

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
        private const string RendererPath = SettingsFolder + "/SceneSyncMobileRenderer.asset";
        private const string RenderPipelineAssetPath = SettingsFolder + "/SceneSyncMobileRPAsset.asset";
        private const string GsplatUrpFeatureTypeName = "Gsplat.GsplatURPFeature";
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

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "afjk";
            PlayerSettings.productName = "Scene Sync XR Client";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;

            var renderPipeline = GetOrCreateRenderPipeline();

            GraphicsSettings.defaultRenderPipeline = renderPipeline;

            var originalQualityLevel = QualitySettings.GetQualityLevel();
            for (var qualityLevel = 0; qualityLevel < QualitySettings.names.Length; qualityLevel++)
            {
                QualitySettings.SetQualityLevel(qualityLevel, false);
                QualitySettings.renderPipeline = renderPipeline;
            }
            QualitySettings.SetQualityLevel(originalQualityLevel, false);
        }

        private static UniversalRenderPipelineAsset GetOrCreateRenderPipeline()
        {
            var existingPipeline =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RenderPipelineAssetPath);
            if (existingPipeline != null)
            {
                var existingRenderer =
                    AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                EnsureGaussianSplatRendererFeature(existingRenderer);
                return existingPipeline;
            }

            var sourceRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SourceRendererPath);
            if (sourceRenderer == null)
            {
                throw new MissingReferenceException($"STYLY renderer was not found: {SourceRendererPath}");
            }

            var renderer = UnityEngine.Object.Instantiate(sourceRenderer);
            renderer.name = "SceneSyncMobileRenderer";
            AssetDatabase.CreateAsset(renderer, RendererPath);
            EnsureGaussianSplatRendererFeature(renderer);

            var renderPipeline = UniversalRenderPipelineAsset.Create(renderer);
            renderPipeline.name = "SceneSyncMobileRPAsset";
            AssetDatabase.CreateAsset(renderPipeline, RenderPipelineAssetPath);
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
            controller.ConnectOnStart = true;

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

            var connectionPanel = new GameObject("ConnectionPanel3D");
            SceneManager.MoveGameObjectToScene(connectionPanel, scene);
            connectionPanel.transform.SetPositionAndRotation(
                new Vector3(0f, 1.4f, 1.2f),
                Quaternion.Euler(0f, 180f, 0f));
            connectionPanel.SetActive(false);

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

        private static void EnsureFolder(string parent, string child)
        {
            var folder = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
