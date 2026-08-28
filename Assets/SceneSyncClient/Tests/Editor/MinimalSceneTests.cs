using System.Linq;
using Afjk.SceneSync;
using Afjk.SceneSync.Rapier;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SceneSync.UnityClient.Tests.Editor
{
    public sealed class MinimalSceneTests
    {
        private const string ScenePath = "Assets/SceneSyncClient/Scenes/SceneSyncClient.unity";

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
            Assert.That(controller.ConnectOnStart, Is.True);
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
    }
}
