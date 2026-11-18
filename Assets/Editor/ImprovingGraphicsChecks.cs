using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace Game.EditorTools
{
    // Validates gameplay graphics fixtures and the supported HDRP feature settings before building.
    public static class ImprovingGraphicsChecks
    {
        [MenuItem("Tools/Group Game/Validate improving graphics")]
        public static void Run()
        {
            GroupIntegrationSetup.Validate();
            RunFocused();
        }

        public static void RunFocused()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            FlightCombatChecks.Run();
            FloatingOriginChecks.Run();
            OriginNetworkChecks.Run();
            CockpitHudChecks.Run();
            OrbitalEnvironmentChecks.Run();
            GraphicsMaterialChecks.Run();

            // After gameplay fixtures pass, check the actual pipeline and volume assets used by the player.
            var asset = GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
            Require(asset != null, "HDRP pipeline must be assigned");
            var settings = asset.currentPlatformRenderPipelineSettings;
            Require(!settings.supportRayTracing && settings.supportSSR && settings.supportVolumetrics, "Bounded raster features");
            Require(settings.dynamicResolutionSettings.enableDLSS && settings.dynamicResolutionSettings.enabled, "Dynamic resolution and DLSS enabled");
            Require(settings.dynamicResolutionSettings.upsampleFilter == DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres, "Supported FSR 1 fallback");
            Require(ShaderConfig.s_CameraRelativeRendering == 1, "Camera-relative shader config");
            var profile = Resources.Load<VolumeProfile>("Graphics/SpaceVolume");
            Require(profile != null && profile.TryGet<Tonemapping>(out var tone) && tone.mode.value == TonemappingMode.ACES, "ACES profile");
            Require(profile.TryGet<HDShadowSettings>(out var shadows) && shadows.maxShadowDistance.value == 250f, "250m shadow range");
            Require(profile.TryGet<HDRISky>(out var sky) && sky.hdriSky.value != null, "Continuous cubemap sky");
            Require(Resources.Load<Material>("Graphics/LitTemplate") != null, "Build-retained lit shader");
            Require(Resources.Load<Shader>("Shaders/SpaceEffect") != null, "Build-retained effect shader");
            Debug.Log("IMPROVING GRAPHICS CHECKS: PASS pipeline, volume, upscaler fallback, camera-relative rendering, shadows and retained shaders");
        }

        public static void Build()
        {
            HDRPSetupEditor.PrepareBuildMaterials();
            SpaceAssetOptimizer.Optimize();
            Run();
            GroupIntegrationSetup.BuildWindows();
        }

        private static void Require(bool value, string message)
        {
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
