using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
namespace Game.EditorTools
{
    // Creates the HDRP settings, sky and retained materials used by the space scenes and player builds.
    public static class HDRPSetupEditor
    {
        public const string Folder = "Assets/Resources/Graphics";

        [MenuItem("Tools/Group Game/Configure improving graphics")]
        public static void Configure()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();

            // Camera-relative rendering is a compiled HDRP shader option, not an asset toggle.
            if (ShaderConfig.s_CameraRelativeRendering != 1)
            {
                throw new InvalidOperationException("HDRP shader config must enable CameraRelativeRendering before setup.");
            }

            var asset = LoadOrCreate<HDRenderPipelineAsset>(Folder + "/SpaceHDRP.asset");
            var serialized = new SerializedObject(asset);
            SetBool(serialized, "enableSRPBatcher", true);
            SetBool(serialized, "m_RenderPipelineSettings.supportRayTracing", false);
            SetBool(serialized, "m_RenderPipelineSettings.supportSSR", true);
            SetBool(serialized, "m_RenderPipelineSettings.supportSSRTransparent", false);
            SetBool(serialized, "m_RenderPipelineSettings.supportVolumetrics", true);
            SetBool(serialized, "m_RenderPipelineSettings.supportVolumetricClouds", false);
            const string dynamicResolutionPath = "m_RenderPipelineSettings.dynamicResolutionSettings.";
            SetBool(serialized, dynamicResolutionPath + "enabled", true);
            SetBool(serialized, dynamicResolutionPath + "enableDLSS", true);
            SetBool(serialized, dynamicResolutionPath + "DLSSUseOptimalSettings", true);
            serialized.FindProperty(dynamicResolutionPath + "DLSSPerfQualitySetting").intValue = (int)UnityEngine.NVIDIA.DLSSQuality.MaximumQuality;
            serialized.FindProperty(dynamicResolutionPath + "minPercentage").floatValue = 67f;
            serialized.FindProperty(dynamicResolutionPath + "maxPercentage").floatValue = 100f;
            serialized.FindProperty(dynamicResolutionPath + "dynResType").intValue = (int)DynamicResolutionType.Hardware;
            serialized.FindProperty(dynamicResolutionPath + "upsampleFilter").intValue = (int)DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = asset;

            for (int qualityLevel = 0; qualityLevel < QualitySettings.names.Length; qualityLevel++)
            {
                QualitySettings.SetQualityLevel(qualityLevel, false);
                QualitySettings.renderPipeline = asset;
                QualitySettings.vSyncCount = 0;
            }

            GraphicsSettings.useScriptableRenderPipelineBatching = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.enableFrameTimingStats = true;
            var template = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/LitTemplate.mat");
            if (template == null)
            {
                template = new Material(Shader.Find("HDRP/Lit")) { enableInstancing = true };
                HDMaterial.ValidateMaterial(template);
                AssetDatabase.CreateAsset(template, Folder + "/LitTemplate.mat");
            }

            var profile = LoadOrCreate<VolumeProfile>(Folder + "/SpaceVolume.asset");
            Get<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1.8f);
            bloom.intensity.Override(0.2f);
            var exposure = Get<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(13f);
            var shadows = Get<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(250f);
            var screenSpaceReflections = Get<ScreenSpaceReflection>(profile);
            screenSpaceReflections.enabled.Override(true);
            screenSpaceReflections.enabledTransparent.Override(false);
            screenSpaceReflections.tracing.Override(RayCastingMode.RayMarching);
            screenSpaceReflections.quality.Override(0);
            screenSpaceReflections.rayMaxIterations = 24;
            screenSpaceReflections.minSmoothness = 0.7f;

            // HDRP 13's fullResolution flag controls ray-traced reflections only.
            // Raster SSR uses its supported low-quality preset instead of a false half-res claim.
            var fogSettings = Get<Fog>(profile);
            fogSettings.enabled.Override(true);
            fogSettings.enableVolumetricFog.Override(true);
            fogSettings.quality.Override(1);
            fogSettings.meanFreePath.Override(18000f);
            fogSettings.baseHeight.Override(-1000f);
            fogSettings.maximumHeight.Override(1000f);
            fogSettings.depthExtent.Override(250f);
            fogSettings.directionalLightsOnly.Override(true);
            fogSettings.albedo.Override(new Color(0.11f, 0.16f, 0.24f));
            var visual = Get<VisualEnvironment>(profile);
            visual.skyType.Override((int)SkyType.HDRI);
            var sky = Get<HDRISky>(profile);
            sky.hdriSky.Override(CreateSky());
            sky.exposure.Override(12f);
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(asset);
            PrepareBuildMaterials();
            AssetDatabase.SaveAssets();
            Debug.Log("[GraphicsSetup] HDRP 13.1.8, camera-relative shaders, SRP batching, DLSS Quality / FSR1, physical sunlight, ACES, bounded SSR/fog. Screen-space lens flares and FSR2 are unavailable in this Editor; bloom is the optical highlight alternative.");
        }

        public static void PrepareBuildMaterials()
        {
            // Resources retain the shader-feature combinations also created dynamically at runtime.
            // Without these representatives, a player build may strip specular/emission/cloud variants.
            const string fighterPath = Folder + "/FighterVariant.mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArvisMag_Assets/BlockySpaceShips/Fihter/FbxModel/Materials/BlockyShip_Fiter_mat.mat");
            if (AssetDatabase.LoadAssetAtPath<Material>(fighterPath) == null)
            {
                var fighter = Game.Graphics.GraphicsMaterials.ConvertSurface(source);
                fighter.name = "Retained fighter shader variant";
                AssetDatabase.CreateAsset(fighter, fighterPath);
            }

            var retainedFighter = AssetDatabase.LoadAssetAtPath<Material>(fighterPath);

            // A persistent texture reference also works in headless setup, where whiteTexture can be null.
            retainedFighter.SetTexture("_MaskMap", source.GetTexture("_SpecGlossMap"));
            HDMaterial.ValidateMaterial(retainedFighter);
            EditorUtility.SetDirty(retainedFighter);
            const string cloudPath = Folder + "/CloudVariant.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(cloudPath) == null)
            {
                var cloud = new Material(Shader.Find("HDRP/Lit"));
                cloud.SetFloat("_SurfaceType", 1f);
                cloud.SetFloat("_BlendMode", 0f);
                cloud.SetFloat("_TransparentZWrite", 0f);
                cloud.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f);
                HDMaterial.ValidateMaterial(cloud);
                AssetDatabase.CreateAsset(cloud, cloudPath);
            }

            AssetDatabase.SaveAssets();
        }

        private static void SetBool(SerializedObject target, string path, bool value)
        {
            var property = target.FindProperty(path);
            if (property == null)
            {
                throw new InvalidOperationException("Unsupported HDRP property: " + path);
            }

            property.boolValue = value;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            return component;
        }

        private static Cubemap CreateSky()
        {
            string path = Folder + "/OrbitalSky.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (existing != null)
            {
                return existing;
            }

            const int size = 512;
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = "Continuous orbital starfield", wrapMode = TextureWrapMode.Clamp };

            // Evaluate the same directional field on every face: no unrelated face images or box seams.
            var random = new System.Random(731);
            var stars = new Vector3[1400];

            for (int starIndex = 0; starIndex < stars.Length; starIndex++)
            {
                float verticalCoordinate = (float)random.NextDouble() * 2f - 1f;
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                stars[starIndex] = new Vector3(Mathf.Sqrt(1 - verticalCoordinate * verticalCoordinate) * Mathf.Cos(angle), verticalCoordinate, Mathf.Sqrt(1 - verticalCoordinate * verticalCoordinate) * Mathf.Sin(angle));
            }

            for (int face = 0; face < 6; face++)
            {
                var colors = new Color[size * size];

                for (int pixelY = 0; pixelY < size; pixelY++)
                {
                    for (int pixelX = 0; pixelX < size; pixelX++)
                    {
                        float horizontalCoordinate = (pixelX + 0.5f) / size * 2f - 1f;
                        float verticalCoordinate2 = (pixelY + 0.5f) / size * 2f - 1f;
                        Vector3 sampleDirection = Direction(face, horizontalCoordinate, verticalCoordinate2).normalized;
                        float band = Mathf.Pow(Mathf.Max(0, 1f - Mathf.Abs(Vector3.Dot(sampleDirection, new Vector3(0.25f, 0.9f, 0.35f).normalized))), 18f);
                        float cloud = Mathf.PerlinNoise(sampleDirection.x * 4f + 17f, sampleDirection.z * 4f + 21f) * Mathf.PerlinNoise(sampleDirection.y * 7f + 31f, sampleDirection.x * 7f + 13f);
                        colors[pixelY * size + pixelX] = new Color(0.0012f, 0.0016f, 0.0028f) + new Color(0.025f, 0.034f, 0.055f) * band * cloud;
                    }
                }

                // Deposit stars by projection, rather than an O(pixels * stars) loop.
                foreach (var star in stars)
                {
                    if (!Project(face, star, out float horizontalCoordinate2, out float verticalCoordinate3))
                    {
                        continue;
                    }

                    int pixelX2 = Mathf.Clamp((int)((horizontalCoordinate2 + 1f) * 0.5f * size), 0, size - 1);
                    int pixelY2 = Mathf.Clamp((int)((verticalCoordinate3 + 1f) * 0.5f * size), 0, size - 1);
                    colors[pixelY2 * size + pixelX2] += new Color(0.35f, 0.42f, 0.55f) * (float)(0.2 + random.NextDouble() * 1.7);
                }

                cube.SetPixels(colors, (CubemapFace)face);
            }

            cube.Apply(true, false);
            AssetDatabase.CreateAsset(cube, path);
            return cube;
        }

        private static Vector3 Direction(int faceIndex, float horizontalCoordinate, float verticalCoordinate)
        {
            switch (faceIndex)
            {
                case 0:
                    return new Vector3(1, -verticalCoordinate, -horizontalCoordinate);
                case 1:
                    return new Vector3(-1, -verticalCoordinate, horizontalCoordinate);
                case 2:
                    return new Vector3(horizontalCoordinate, 1, verticalCoordinate);
                case 3:
                    return new Vector3(horizontalCoordinate, -1, -verticalCoordinate);
                case 4:
                    return new Vector3(horizontalCoordinate, -verticalCoordinate, 1);
                default:
                    return new Vector3(-horizontalCoordinate, -verticalCoordinate, -1);
            }
        }

        private static bool Project(int faceIndex, Vector3 direction, out float horizontalCoordinate, out float verticalCoordinate)
        {
            horizontalCoordinate = verticalCoordinate = 0;
            float dominantAxisMagnitude = faceIndex < 2 ? Mathf.Abs(direction.x) : faceIndex < 4 ? Mathf.Abs(direction.y) : Mathf.Abs(direction.z);
            if (dominantAxisMagnitude < Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)))
            {
                return false;
            }

            switch (faceIndex)
            {
                case 0:
                    if (direction.x < 0)
                    {
                        return false;
                    }
                    horizontalCoordinate = -direction.z / dominantAxisMagnitude;
                    verticalCoordinate = -direction.y / dominantAxisMagnitude;
                    break;
                case 1:
                    if (direction.x > 0)
                    {
                        return false;
                    }
                    horizontalCoordinate = direction.z / dominantAxisMagnitude;
                    verticalCoordinate = -direction.y / dominantAxisMagnitude;
                    break;
                case 2:
                    if (direction.y < 0)
                    {
                        return false;
                    }
                    horizontalCoordinate = direction.x / dominantAxisMagnitude;
                    verticalCoordinate = direction.z / dominantAxisMagnitude;
                    break;
                case 3:
                    if (direction.y > 0)
                    {
                        return false;
                    }
                    horizontalCoordinate = direction.x / dominantAxisMagnitude;
                    verticalCoordinate = -direction.z / dominantAxisMagnitude;
                    break;
                case 4:
                    if (direction.z < 0)
                    {
                        return false;
                    }
                    horizontalCoordinate = direction.x / dominantAxisMagnitude;
                    verticalCoordinate = -direction.y / dominantAxisMagnitude;
                    break;
                default:
                    if (direction.z > 0)
                    {
                        return false;
                    }
                    horizontalCoordinate = -direction.x / dominantAxisMagnitude;
                    verticalCoordinate = -direction.y / dominantAxisMagnitude;
                    break;
            }

            return true;
        }
    }
}
