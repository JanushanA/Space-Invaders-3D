using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Applies graphics settings supported by this Unity version and the current graphics hardware.
namespace Game.Graphics
{
    [DefaultExecutionOrder(-900)]
    public sealed class GraphicsSettingsManager : MonoBehaviour
    {
        public const int TargetFrameRate = 120;

        public static string Upscaler => HDDynamicResolutionPlatformCapabilities.DLSSDetected ? "DLSS Quality" : "FSR 1 (HDRP 13 fallback)";
        private static GraphicsSettingsManager instance;

        // Equal material contents share conversion even when hit feedback created separate source instances.
        private readonly Dictionary<int, Material> converted = new Dictionary<int, Material>();
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private float scale = 100f;
        private float nextSample;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var root = new GameObject("Graphics settings");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<GraphicsSettingsManager>();
        }

        private void Awake()
        {
            Application.targetFrameRate = TargetFrameRate;
            QualitySettings.vSyncCount = 0;
            DynamicResolutionHandler.SetDynamicResScaler(() => scale, DynamicResScalePolicyType.ReturnsPercentage);
            SceneManager.sceneLoaded += SceneLoaded;
            ConfigureScene();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => ConfigureScene();

        public void ConfigureScene()
        {
            foreach (var camera in FindObjectsOfType<Camera>(true))
            {
                var data = camera.GetComponent<HDAdditionalCameraData>() ?? camera.gameObject.AddComponent<HDAdditionalCameraData>();
                data.allowDynamicResolution = true;
                data.allowDeepLearningSuperSampling = true;
                data.deepLearningSuperSamplingUseCustomQualitySettings = true;
                data.deepLearningSuperSamplingQuality = (uint)UnityEngine.NVIDIA.DLSSQuality.MaximumQuality;
                data.deepLearningSuperSamplingUseOptimalSettings = true;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                camera.nearClipPlane = 0.06f;
                camera.farClipPlane = 12000f;
            }

            Light primary = null;

            foreach (var light in FindObjectsOfType<Light>(true))
            {
                var data = light.GetComponent<HDAdditionalLightData>() ?? light.gameObject.AddComponent<HDAdditionalLightData>();
                if (light.type == LightType.Directional && primary == null)
                {
                    primary = light;
                    data.SetIntensity(110000f, LightUnit.Lux);
                    data.SetShadowResolution(2048);
                    light.shadows = LightShadows.Soft;
                }
                else
                {
                    light.shadows = LightShadows.None;
                }
            }

            if (primary == null)
            {
                var sun = new GameObject("Orbital sunlight");
                sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
                primary = sun.AddComponent<Light>();
                primary.type = LightType.Directional;
                primary.color = new Color(1f, 0.93f, 0.82f);
                primary.shadows = LightShadows.Soft;
                sun.AddComponent<HDAdditionalLightData>().SetIntensity(110000f, LightUnit.Lux);
            }

            if (GameObject.Find("Improving graphics volume") == null)
            {
                var volume = new GameObject("Improving graphics volume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
                volume.sharedProfile = Resources.Load<VolumeProfile>("Graphics/SpaceVolume");
            }

            ConvertHierarchy(null);
        }

        public static void PrepareHierarchy(GameObject root)
        {
            if (instance != null)
            {
                instance.ConvertHierarchy(root);
            }
        }

        private void ConvertHierarchy(GameObject root)
        {
            var renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : FindObjectsOfType<Renderer>(true);

            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;

                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var original = materials[materialIndex];
                    if (original == null || original.shader == null)
                    {
                        continue;
                    }

                    string shaderName = original.shader.name;
                    if (shaderName != "Standard" && shaderName != "Standard (Specular setup)")
                    {
                        continue;
                    }

                    int materialKey = original.ComputeCRC();
                    if (!converted.TryGetValue(materialKey, out var replacement))
                    {
                        replacement = GraphicsMaterials.ConvertSurface(original);
                        converted.Add(materialKey, replacement);
                    }

                    materials[materialIndex] = replacement;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    if (renderer is MeshRenderer)
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                    }
                }
            }
        }

        private void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (Time.unscaledTime < nextSample)
            {
                return;
            }

            nextSample = Time.unscaledTime + 0.5f;

            // GPU timing avoids interpreting the frame limiter or CPU-bound frames as GPU load.
            if (!HDDynamicResolutionPlatformCapabilities.DLSSDetected && FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                double gpuMs = timings[0].gpuFrameTime;
                if (gpuMs > 8.6)
                {
                    scale = Mathf.Max(67f, scale - 3f);
                }
                else if (gpuMs > 0 && gpuMs < 7.2)
                {
                    scale = Mathf.Min(100f, scale + 1f);
                }
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;

            foreach (var material in converted.Values)
            {
                if (material != null)
                {
                    GraphicsMaterials.ReleaseConvertedSurface(material);
                }
            }

            converted.Clear();
            GraphicsMaterials.ReleaseMaskCache();
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
