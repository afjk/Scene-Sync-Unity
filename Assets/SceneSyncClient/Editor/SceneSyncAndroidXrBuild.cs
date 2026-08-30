using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;

namespace SceneSync.UnityClient.Editor
{
    /// <summary>
    /// GameCI entry point for the Android XR debug APK matrix.
    /// </summary>
    public static class SceneSyncAndroidXrBuild
    {
        public const string BuildMethod =
            "SceneSync.UnityClient.Editor.SceneSyncAndroidXrBuild.Build";

        private const string TargetArgument = "-sceneSyncXrTarget";
        private const string OutputArgument = "-sceneSyncOutput";

        private static readonly SceneSyncAndroidXrBuildProfile[] ProfileList =
        {
            new SceneSyncAndroidXrBuildProfile(
                "quest3",
                "Meta Quest 3 Debug",
                "scene-sync-unity-quest3-debug.apk",
                "com.unity.xr.meta-openxr",
                "com.unity.openxr.featureset.meta",
                29,
                GraphicsDeviceType.Vulkan,
                new[]
                {
                    "com.unity.openxr.feature.input.handtracking",
                    "com.unity.openxr.feature.metaquest",
                    "com.unity.openxr.feature.arfoundation-meta-anchor",
                    "com.unity.openxr.feature.meta-boundary-visibility",
                    "com.unity.openxr.feature.arfoundation-meta-bounding-boxes",
                    "com.unity.openxr.feature.arfoundation-meta-session",
                    "com.unity.openxr.feature.meta-colocation-discovery",
                    "com.unity.openxr.feature.arfoundation-meta-camera",
                    "com.unity.openxr.feature.input.handinteraction",
                    "com.unity.openxr.feature.input.metaquestplus",
                }),
            new SceneSyncAndroidXrBuildProfile(
                "pico4-ultra",
                "PICO 4 Ultra Debug",
                "scene-sync-unity-pico4-ultra-debug.apk",
                "com.unity.xr.openxr.picoxr",
                "com.picoxr.openxr.features",
                26,
                GraphicsDeviceType.OpenGLES3,
                new[]
                {
                    "com.unity.openxr.feature.pico",
                    "com.unity.openxr.pico.features",
                    "com.pico.openxr.feature.passthrough",
                    "com.unity.openxr.feature.input.handinteraction",
                    "com.unity.openxr.feature.input.PICO4touch",
                    "com.unity.openxr.feature.input.PICO4Ultratouch",
                }),
            new SceneSyncAndroidXrBuildProfile(
                "vive-focus-vision",
                "VIVE Focus Vision Debug",
                "scene-sync-unity-vive-focus-vision-debug.apk",
                "com.htc.upm.vive.openxr",
                "com.htc.vive.openxr.featureset.vivexr",
                29,
                GraphicsDeviceType.OpenGLES3,
                new[]
                {
                    "vive.openxr.feature.compositionlayer",
                    "vive.openxr.feature.hand.tracking",
                    "vive.openxr.feature.passthrough",
                    "com.unity.openxr.feature.vivefocus3",
                    "com.unity.openxr.feature.input.handinteraction",
                    "vive.openxr.feature.focus3controller",
                }),
            new SceneSyncAndroidXrBuildProfile(
                "android-xr",
                "Android XR Debug",
                "scene-sync-unity-android-xr-debug.apk",
                "com.unity.xr.androidxr-openxr",
                "com.unity.openxr.featureset.android",
                24,
                GraphicsDeviceType.Vulkan,
                new[]
                {
                    "com.unity.openxr.feature.androidxr-support",
                    "com.unity.openxr.feature.arfoundation-androidxr-anchor",
                    "com.unity.openxr.feature.arfoundation-androidxr-camera",
                    "com.unity.openxr.feature.arfoundation-androidxr-face",
                    "com.unity.openxr.feature.arfoundation-androidxr-occlusion",
                    "com.unity.openxr.feature.arfoundation-androidxr-plane",
                    "com.unity.openxr.feature.arfoundation-androidxr-raycast",
                    "com.unity.openxr.feature.arfoundation-androidxr-session",
                    "com.unity.openxr.feature.androidxr-display-utilities",
                    "com.unity.openxr.feature.androidxr-hand-mesh-data",
                    "com.unity.openxr.feature.input.handinteraction",
                    "com.unity.openxr.feature.input.khrsimpleprofile",
                }),
        };

        private static readonly IReadOnlyDictionary<string, SceneSyncAndroidXrBuildProfile>
            ProfilesByTarget = ProfileList.ToDictionary(
                profile => profile.Target,
                StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyList<SceneSyncAndroidXrBuildProfile> ProfileView =
            Array.AsReadOnly(ProfileList);

        public static IReadOnlyList<SceneSyncAndroidXrBuildProfile> Profiles => ProfileView;

        public static SceneSyncAndroidXrBuildProfile GetProfile(string target)
        {
            if (!string.IsNullOrWhiteSpace(target)
                && ProfilesByTarget.TryGetValue(target, out var profile))
            {
                return profile;
            }

            throw new ArgumentException(
                "Unknown Android XR build target '" + target + "'. Supported targets: "
                + string.Join(", ", ProfileList.Select(item => item.Target)) + ".",
                nameof(target));
        }

        public static void Build()
        {
            var profile = GetProfile(GetCommandLineValue(TargetArgument));
            var output = GetCommandLineValue(OutputArgument);
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.Combine("build", profile.ApkFileName);
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android,
                    BuildTarget.Android))
            {
                throw new BuildFailedException(
                    "Android Build Support is not installed or the Android target could not be activated.");
            }

            ConfigurePlayer(profile);
            ConfigureOpenXr(profile);
            SceneSyncProjectSetup.EnsureRuntimeRenderingConfiguration();
            ConfigureShaderStripping();
            AssetDatabase.SaveAssets();

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new BuildFailedException("No enabled Unity scenes were found for the player build.");
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new BuildFailedException("The Unity project root could not be resolved.");
            }
            output = Path.GetFullPath(
                Path.IsPathRooted(output) ? output : Path.Combine(projectRoot, output));
            var outputDirectory = Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new BuildFailedException("The APK output directory could not be resolved: " + output);
            }
            Directory.CreateDirectory(outputDirectory);

            Debug.Log(
                "[SceneSyncClient] Building " + profile.DisplayName
                + " to " + output
                + " with feature set " + profile.FeatureSetId + ".");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            });

            var summary = report.summary;
            Debug.Log(
                "[SceneSyncClient] " + profile.DisplayName
                + " result=" + summary.result
                + ", errors=" + summary.totalErrors
                + ", warnings=" + summary.totalWarnings
                + ", size=" + summary.totalSize
                + ", time=" + summary.totalTime + ".");

            if (summary.result != BuildResult.Succeeded || !File.Exists(output))
            {
                throw new BuildFailedException(
                    profile.DisplayName + " failed with " + summary.totalErrors + " build errors.");
            }
        }

        private static void ConfigurePlayer(SceneSyncAndroidXrBuildProfile profile)
        {
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion =
                (AndroidSdkVersions)profile.MinimumApiLevel;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { profile.GraphicsApi });
        }

        private static void ConfigureShaderStripping()
        {
            if (GraphicsSettings.TryGetRenderPipelineSettings<URPShaderStrippingSetting>(
                    out var shaderStrippingSettings))
            {
                shaderStrippingSettings.stripUnusedPostProcessingVariants = true;
            }
        }

        private static void ConfigureOpenXr(SceneSyncAndroidXrBuildProfile profile)
        {
            var installedPackages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            if (!installedPackages.Any(package => package.name == profile.RequiredPackage))
            {
                throw new BuildFailedException(
                    profile.DisplayName + " requires package " + profile.RequiredPackage + ".");
            }

            const BuildTargetGroup group = BuildTargetGroup.Android;
            FeatureHelpers.RefreshFeatures(group);
            OpenXRFeatureSetManager.activeBuildTarget = group;
            OpenXRFeatureSetManager.InitializeFeatureSets();

            var featureSets = OpenXRFeatureSetManager.FeatureSetsForBuildTarget(group);
            var selectedFeatureSet = featureSets.FirstOrDefault(
                set => string.Equals(
                    set.featureSetId,
                    profile.FeatureSetId,
                    StringComparison.Ordinal));
            if (selectedFeatureSet == null || !selectedFeatureSet.isInstalled)
            {
                throw new BuildFailedException(
                    "OpenXR feature set " + profile.FeatureSetId
                    + " is not installed for " + profile.DisplayName + ".");
            }

            foreach (var featureSet in featureSets)
            {
                featureSet.isEnabled = ReferenceEquals(featureSet, selectedFeatureSet);
            }
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                throw new BuildFailedException("Android OpenXR settings could not be loaded.");
            }

            foreach (var feature in settings.GetFeatures())
            {
                if (feature != null)
                {
                    feature.enabled = false;
                    EditorUtility.SetDirty(feature);
                }
            }

            var missingFeatures = new List<string>();
            foreach (var featureId in profile.FeatureIds)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
                if (feature == null)
                {
                    missingFeatures.Add(featureId);
                    continue;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }

            if (missingFeatures.Count > 0)
            {
                throw new BuildFailedException(
                    profile.DisplayName + " is missing OpenXR features: "
                    + string.Join(", ", missingFeatures) + ".");
            }

            settings.renderMode = OpenXRSettings.RenderMode.MultiPass;
            EditorUtility.SetDirty(settings);
        }

        private static string GetCommandLineValue(string argumentName)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return index + 1 < arguments.Length ? arguments[index + 1] : null;
                }

                var prefix = argumentName + "=";
                if (arguments[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index].Substring(prefix.Length);
                }
            }

            return null;
        }
    }

    public sealed class SceneSyncAndroidXrBuildProfile
    {
        public SceneSyncAndroidXrBuildProfile(
            string target,
            string displayName,
            string apkFileName,
            string requiredPackage,
            string featureSetId,
            int minimumApiLevel,
            GraphicsDeviceType graphicsApi,
            string[] featureIds)
        {
            Target = target;
            DisplayName = displayName;
            ApkFileName = apkFileName;
            RequiredPackage = requiredPackage;
            FeatureSetId = featureSetId;
            MinimumApiLevel = minimumApiLevel;
            GraphicsApi = graphicsApi;
            FeatureIds = featureIds;
        }

        public string Target { get; }
        public string DisplayName { get; }
        public string ApkFileName { get; }
        public string RequiredPackage { get; }
        public string FeatureSetId { get; }
        public int MinimumApiLevel { get; }
        public GraphicsDeviceType GraphicsApi { get; }
        public string[] FeatureIds { get; }
    }
}
