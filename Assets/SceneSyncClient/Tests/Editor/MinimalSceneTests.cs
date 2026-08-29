using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Afjk.SceneSync;
using Afjk.SceneSync.Rapier;
using NUnit.Framework;
using SceneSync.UnityClient.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SceneSync.UnityClient.Tests.Editor
{
    public sealed class MinimalSceneTests
    {
        private const string ScenePath = "Assets/SceneSyncClient/Scenes/SceneSyncClient.unity";
        private const string RenderPipelinePath =
            "Assets/SceneSyncClient/Settings/SceneSyncMobileRPAsset.asset";
        private const string RuntimeShaderVariantsPath =
            "Assets/SceneSyncClient/Settings/SceneSyncRuntimeShaders.shadervariants";

        private static readonly string[] RequiredRuntimeShaders =
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

        [Test]
        public void AndroidXrCiProfiles_MatchGodotArtifactMatrix()
        {
            var profiles = SceneSyncAndroidXrBuild.Profiles;

            Assert.That(
                profiles.Select(profile => profile.Target),
                Is.EqualTo(new[]
                {
                    "quest3",
                    "pico4-ultra",
                    "vive-focus-vision",
                    "android-xr",
                }));
            Assert.That(
                profiles.Select(profile => profile.ApkFileName),
                Is.EqualTo(new[]
                {
                    "scene-sync-unity-quest3-debug.apk",
                    "scene-sync-unity-pico4-ultra-debug.apk",
                    "scene-sync-unity-vive-focus-vision-debug.apk",
                    "scene-sync-unity-android-xr-debug.apk",
                }));
            Assert.That(profiles.All(profile => profile.FeatureIds.Length > 0), Is.True);
            Assert.That(profiles.All(profile => profile.MinimumApiLevel >= 24), Is.True);

            const string workflowPath = ".github/workflows/build-android-xr.yml";
            Assert.That(File.Exists(workflowPath), Is.True, "Android XR workflow is missing.");
            var workflow = File.ReadAllText(workflowPath);
            Assert.That(workflow, Does.Contain("game-ci/unity-builder@v4"));
            Assert.That(workflow, Does.Contain("retention-days: 14"));
            Assert.That(workflow, Does.Contain("buildMethod: " + SceneSyncAndroidXrBuild.BuildMethod));
            foreach (var profile in profiles)
            {
                Assert.That(workflow, Does.Contain("target: " + profile.Target));
                Assert.That(workflow, Does.Contain("apk: " + profile.ApkFileName));
                Assert.That(workflow, Does.Contain("package_name: " + profile.RequiredPackage));
            }
        }

        [Test]
        public void MinimalScene_HasViewerOnlySceneSyncConfiguration()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();

            Assert.That(roots.Any(root => root.name == "STYLY XR Rig"), Is.True);
            Assert.That(roots.Any(root => root.name == "SceneSyncRoot"), Is.True);
            Assert.That(roots.Any(root => root.name == "RemoteAvatars"), Is.True);
            Assert.That(roots.Any(root => root.name == "ConnectionPanel3D"), Is.True);

            var runtime = roots.Single(root => root.name == "SceneSyncRuntime");
            var manager = runtime.GetComponent<SceneSyncManager>();
            var controller = runtime.GetComponent<SceneSyncClientController>();
            var physicsMetadata = runtime.GetComponent<SceneSyncPhysicsMetadata>();
            var rapierBridge = runtime.GetComponent<SceneSyncRapierBridge>();
            var rapierGuard = runtime.GetComponent<SceneSyncRapierPlatformGuard>();
            var rapierInteraction = runtime.GetComponent<SceneSyncRapierInteractionController>();

            Assert.That(manager, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(physicsMetadata, Is.Not.Null);
            Assert.That(physicsMetadata.HasScenePhysics, Is.True);
            Assert.That(rapierBridge, Is.Not.Null);
            Assert.That(rapierGuard, Is.Not.Null);
            Assert.That(rapierInteraction, Is.Not.Null);
            Assert.That(controller.Manager, Is.SameAs(manager));
            Assert.That(controller.ConfiguredRoom, Is.Empty);
            Assert.That(controller.ConnectOnStart, Is.False);
            Assert.That(
                SceneSyncPresenceUrl.BuildRoomUrl(manager.PresenceUrl, controller.ConfiguredRoom),
                Is.EqualTo(manager.PresenceUrl),
                "An empty room must omit the room query so the server assigns the LAN room.");
            Assert.That(manager.AutoConnect, Is.False);
            Assert.That(manager.SyncHierarchy, Is.False);
            Assert.That(manager.AllowPlaybackClockControl, Is.False);
            Assert.That(
                manager.PlaybackClockFollowPolicy,
                Is.EqualTo(SceneSyncPlaybackClockFollowPolicy.FollowerOnly));

            var sceneSyncRoot = roots.Single(root => root.name == "SceneSyncRoot");
            var remoteObjects = sceneSyncRoot.transform.Find("RemoteObjects");
            Assert.That(remoteObjects, Is.Not.Null);
            Assert.That(manager.TemporaryRoot, Is.SameAs(remoteObjects));
            Assert.That(rapierBridge.PlaybackClockManager, Is.SameAs(manager));
            Assert.That(rapierBridge.BodyRoot, Is.SameAs(remoteObjects));
            Assert.That(rapierBridge.AutoRun, Is.True);
            Assert.That(rapierBridge.UseSceneClock, Is.True);
            Assert.That(rapierBridge.RequireSceneClock, Is.False);
            Assert.That(rapierBridge.PreferManagerPlaybackClock, Is.True);
            Assert.That(rapierBridge.PreserveMotionOnRebuild, Is.False);
            Assert.That(rapierGuard.Bridge, Is.SameAs(rapierBridge));
            Assert.That(rapierInteraction.Bridge, Is.SameAs(rapierBridge));

            Assert.That(SceneSyncGaussianSplatBackend.IsAvailable, Is.True);
            Assert.That(SceneSyncGaussianSplatBackend.BackendName, Is.EqualTo("UnitySplats 1.2.0"));

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                "Assets/SceneSyncClient/Settings/SceneSyncMobileRenderer.asset");
            Assert.That(rendererData, Is.Not.Null);
            Assert.That(
                rendererData.rendererFeatures.Any(
                    feature => feature != null && feature.GetType().FullName == "Gsplat.GsplatURPFeature"),
                Is.True,
                "The active URP renderer must include the UnitySplats render feature.");

            var serializedManager = new SerializedObject(manager);
            var configuredSyncRoot = serializedManager.FindProperty("_syncRoot").objectReferenceValue;
            Assert.That(configuredSyncRoot, Is.SameAs(sceneSyncRoot.transform));
        }

        [Test]
        public void ConnectionMenu_UsesXrUiAndAndroidSystemKeyboard()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var panelRoot = roots.Single(root => root.name == "ConnectionPanel3D");
            var runtime = roots.Single(root => root.name == "SceneSyncRuntime");
            var eventSystemRoot = roots.Single(root => root.name == "EventSystem");

            var panel = panelRoot.GetComponent<SceneSyncConnectionPanel>();
            var canvas = panelRoot.GetComponent<Canvas>();
            var raycaster = panelRoot.GetComponent<TrackedDeviceGraphicRaycaster>();

            Assert.That(panelRoot.activeSelf, Is.True);
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.Controller, Is.SameAs(runtime.GetComponent<SceneSyncClientController>()));
            Assert.That(panel.TargetCamera, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(canvas.worldCamera, Is.SameAs(panel.TargetCamera));
            Assert.That(raycaster, Is.Not.Null);
            Assert.That(eventSystemRoot.GetComponent<EventSystem>(), Is.Not.Null);
            Assert.That(eventSystemRoot.GetComponent<XRUIInputModule>(), Is.Not.Null);

            Assert.That(panel.FullPanel, Is.Not.Null);
            Assert.That(panel.FullPanel.activeSelf, Is.True);
            Assert.That(panel.MinimizedPanel, Is.Not.Null);
            Assert.That(panel.MinimizedPanel.activeSelf, Is.False);
            Assert.That(panel.ConnectButton, Is.Not.Null);
            Assert.That(panel.DisconnectButton, Is.Not.Null);
            AssertSystemKeyboardInput(panel.RoomInput);
            AssertSystemKeyboardInput(panel.NicknameInput);
        }

        private static void AssertSystemKeyboardInput(InputField inputField)
        {
            Assert.That(inputField, Is.Not.Null);
            Assert.That(inputField.lineType, Is.EqualTo(InputField.LineType.SingleLine));
            Assert.That(inputField.keyboardType, Is.EqualTo(TouchScreenKeyboardType.ASCIICapable));
            Assert.That(inputField.shouldActivateOnSelect, Is.True);

            // The public getter intentionally returns true outside Android, so
            // inspect the serialized flag that the PICO player receives.
            var serializedInput = new SerializedObject(inputField);
            Assert.That(
                serializedInput.FindProperty("m_HideMobileInput").boolValue,
                Is.False,
                "The native Android keyboard must remain visible.");
        }

        [Test]
        public void RuntimeRendering_KeepsDynamicImportShadersAndSplatRenderer()
        {
            var expectedPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                RenderPipelinePath);
            var stylyPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(
                "Packages/com.styly.styly-xr-rig/Runtime/Settings/STYLY_Mobile_RPAsset.asset");
            Assert.That(expectedPipeline, Is.Not.Null);
            Assert.That(stylyPipeline, Is.Not.Null);
            Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(expectedPipeline));
            Assert.That(expectedPipeline.msaaSampleCount, Is.EqualTo(stylyPipeline.msaaSampleCount));
            Assert.That(expectedPipeline.renderScale, Is.EqualTo(stylyPipeline.renderScale));
            Assert.That(
                expectedPipeline.supportsCameraOpaqueTexture,
                Is.EqualTo(stylyPipeline.supportsCameraOpaqueTexture));

            var settingsObjects = AssetDatabase.LoadAllAssetsAtPath(
                "ProjectSettings/GraphicsSettings.asset");
            Assert.That(settingsObjects, Is.Not.Empty);

            var serializedSettings = new SerializedObject(settingsObjects[0]);
            var includedShaders = serializedSettings.FindProperty("m_AlwaysIncludedShaders");
            Assert.That(includedShaders, Is.Not.Null);

            foreach (var shaderName in RequiredRuntimeShaders)
            {
                var shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, "Required shader was not found: " + shaderName);
                Assert.That(
                    Enumerable.Range(0, includedShaders.arraySize).Any(
                        index => includedShaders.GetArrayElementAtIndex(index).objectReferenceValue == shader),
                    Is.True,
                    "Runtime-created material shader can be stripped from the player: " + shaderName);
            }

            var variants = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(
                RuntimeShaderVariantsPath);
            Assert.That(variants, Is.Not.Null);

            foreach (var shaderName in RequiredRuntimeShaders.Where(
                         name => name.StartsWith("Shader Graphs/glTF")))
            {
                var shader = Shader.Find(shaderName);
                var forwardAlphaTest = new ShaderVariantCollection.ShaderVariant(
                    shader,
                    PassType.ScriptableRenderPipeline,
                    "_ALPHATEST_ON");
                var shadowAlphaTest = new ShaderVariantCollection.ShaderVariant(
                    shader,
                    PassType.ShadowCaster,
                    "_ALPHATEST_ON");
                Assert.That(
                    variants.Contains(forwardAlphaTest),
                    Is.True,
                    "Runtime glTF alpha-test forward variant is missing: " + shaderName);
                Assert.That(
                    variants.Contains(shadowAlphaTest),
                    Is.True,
                    "Runtime glTF alpha-test shadow variant is missing: " + shaderName);
            }

            var preloadedShaders = serializedSettings.FindProperty("m_PreloadedShaders");
            Assert.That(preloadedShaders, Is.Not.Null);
            Assert.That(
                Enumerable.Range(0, preloadedShaders.arraySize).Any(
                    index => preloadedShaders.GetArrayElementAtIndex(index).objectReferenceValue == variants),
                Is.True,
                "The runtime glTF alpha-test variants must be preloaded in GraphicsSettings.");
        }

        [Test]
        public void GzipCarrierCompatibility_NormalizesCachedGlb()
        {
            var glb = Encoding.ASCII.GetBytes("glTF-compatible-test-payload");
            byte[] gzip;
            using (var output = new MemoryStream())
            {
                using (var compressor = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                {
                    compressor.Write(glb, 0, glb.Length);
                }
                gzip = output.ToArray();
            }

            Assert.That(SceneSyncGzipCarrierCompatibility.IsGzip(gzip), Is.True);
            Assert.That(
                SceneSyncGzipCarrierCompatibility.TryDecompress(gzip, out var restored, out var error),
                Is.True,
                error);
            Assert.That(restored, Is.EqualTo(glb));

            var directory = Path.Combine(
                Path.GetTempPath(),
                "SceneSyncGzipCarrierCompatibility-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "asset-test.glb");
                File.WriteAllBytes(path, gzip);

                var result = SceneSyncGzipCarrierCompatibility.NormalizePersistentCache(directory);

                Assert.That(result.Errors, Is.Zero, result.LastError);
                Assert.That(result.NormalizedFiles, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(glb));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
