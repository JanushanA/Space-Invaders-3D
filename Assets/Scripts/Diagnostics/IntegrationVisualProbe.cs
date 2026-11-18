#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
namespace Game.Diagnostics
{
    // Explicit development-build diagnostic. Ordinary launches create no probe.
    public sealed class IntegrationVisualProbe : MonoBehaviour
    {
        private string directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-integrationVisualProbe") < 0)
            {
                return;
            }

            var sceneObject = new GameObject("IntegrationVisualProbe");
            DontDestroyOnLoad(sceneObject);
            var probe = sceneObject.AddComponent<IntegrationVisualProbe>();
            int index = Array.IndexOf(args, "-integrationVisualDir");
            probe.directory = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) :
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "VisualProbe"));
            GameSession.SuppressSave = true;
            Application.runInBackground = true;
            probe.StartCoroutine(probe.Run());
        }

        private void Update()
        {
            GameSession.SuppressSave = true;
        }

        private IEnumerator Run()
        {
            yield return null;
            if (SceneManager.GetActiveScene().name != GameSession.MenuScene)
            {
                yield return SceneManager.LoadSceneAsync(GameSession.MenuScene);
            }

            yield return new WaitForSecondsRealtime(1f);
            GameSession.SuppressSave = true;
            try
            {
                Directory.CreateDirectory(directory);
                Capture();
                Debug.Log("INTEGRATION VISUAL PROBE PASS " + directory);
                Application.Quit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Debug.LogError("INTEGRATION VISUAL PROBE FAIL");
                Application.Quit(1);
            }
        }

        private void Capture()
        {
            var display = GameObject.Find("GroupShipDisplay");
            if (display == null)
            {
                throw new InvalidOperationException("Group menu display missing");
            }

            var renderer = display.GetComponentInChildren<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var original = renderer.sharedMaterial;
            var camera = Camera.main;
            Debug.Log("RUNTIME VISUAL batch=" + Application.isBatchMode + " graphics=" + SystemInfo.graphicsDeviceType +
                " colorSpace=" + QualitySettings.activeColorSpace + " quality=" + QualitySettings.GetQualityLevel() + " masterTextureLimit=" + QualitySettings.masterTextureLimit);
            Debug.Log("RUNTIME VISUAL material=" + original.name + " shader=" + original.shader.name + " supported=" + original.shader.isSupported +
                " queue=" + original.renderQueue + " tint=" + original.color + " scale=" + original.mainTextureScale + " offset=" + original.mainTextureOffset + " keywords=" + string.Join(",", original.shaderKeywords));
            Debug.Log("RUNTIME VISUAL ambientMode=" + RenderSettings.ambientMode + " ambient=" + RenderSettings.ambientLight +
                " sky=" + RenderSettings.ambientSkyColor + " equator=" + RenderSettings.ambientEquatorColor + " ground=" + RenderSettings.ambientGroundColor +
                " intensity=" + RenderSettings.ambientIntensity + " reflection=" + RenderSettings.reflectionIntensity);
            var ambientProbe = RenderSettings.ambientProbe;

            for (int channel = 0; channel < 3; channel++)
            {
                var coefficients = new string[9];

                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    coefficients[coefficient] = ambientProbe[channel, coefficient].ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
                }

                Debug.Log("RUNTIME VISUAL ambientProbe[" + channel + "]=" + string.Join(",", coefficients));
            }

            var lights = FindObjectsOfType<Light>();
            var lightEnabled = new bool[lights.Length];

            for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                lightEnabled[lightIndex] = lights[lightIndex].enabled;
                Debug.Log("RUNTIME VISUAL light=" + lights[lightIndex].name + " enabled=" + lights[lightIndex].enabled + " type=" + lights[lightIndex].type + " color=" + lights[lightIndex].color + " intensity=" + lights[lightIndex].intensity);
            }

            Debug.Log("RUNTIME VISUAL mesh=" + mesh.name + " vertices=" + mesh.vertexCount + " readable=" + mesh.isReadable +
                " uv0=" + mesh.HasVertexAttribute(VertexAttribute.TexCoord0) + " normals=" + mesh.HasVertexAttribute(VertexAttribute.Normal));
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
            {
                Debug.Log("RUNTIME VISUAL uv0Format=" + mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0) + " dimension=" + mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0));
            }

            if (mesh.isReadable)
            {
                var textureCoordinates = new List<Vector2>();
                mesh.GetUVs(0, textureCoordinates);
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

                foreach (var point in textureCoordinates)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                Debug.Log("RUNTIME VISUAL uv0Count=" + textureCoordinates.Count + " min=" + min.ToString("F5") + " max=" + max.ToString("F5"));
            }

            foreach (string property in new[] { "_MainTex", "_EmissionMap", "_BumpMap", "_SpecGlossMap" })
            {
                if (!original.HasProperty(property))
                {
                    continue;
                }

                var texture = original.GetTexture(property);
                Debug.Log("RUNTIME VISUAL " + property + "=" + (texture == null ? "NULL" : texture.name + " " + texture.width + "x" + texture.height + " format=" + texture.graphicsFormat));
            }

            if (original.mainTexture == null)
            {
                throw new InvalidOperationException("Runtime albedo missing");
            }

            // Save the scene state before rendering controlled material and lighting comparisons.
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fieldOfView2 = camera.fieldOfView;
            var target = new RenderTexture(1280, 900, 24, RenderTextureFormat.ARGB32);
            var variant = new Material(original);
            var withoutEmission = new Material(original);
            var unlit = VisualUtil.MakeUnlitTexture(original.mainTexture);
            var ambientMode = RenderSettings.ambientMode;
            var ambientColor = RenderSettings.ambientLight;
            float ambientIntensity = RenderSettings.ambientIntensity;
            float reflectionIntensity = RenderSettings.reflectionIntensity;
            try
            {
                camera.transform.position = renderer.bounds.center + new Vector3(0f, 2f, -6f);
                camera.transform.LookAt(renderer.bounds.center);
                camera.fieldOfView = 45f;
                camera.targetTexture = target;
                variant.DisableKeyword("_DETAIL_MULX2");
                variant.DisableKeyword("_METALLICGLOSSMAP");
                withoutEmission.DisableKeyword("_EMISSION");
                withoutEmission.SetColor("_EmissionColor", Color.black);
                string[] names = { "original", "without-detail", "unlit", "without-emission" };
                Material[] materials = { original, variant, unlit, withoutEmission };

                for (int captureIndex = 0; captureIndex < names.Length; captureIndex++)
                {
                    renderer.sharedMaterial = materials[captureIndex];
                    camera.Render();
                    WriteImage(target, names[captureIndex]);
                }

                // Capture the texture by itself to distinguish an asset problem from a lighting problem.
                UnityEngine.Graphics.Blit(original.mainTexture, target);
                WriteImage(target, "albedo-blit");
                var finiteProbe = new SphericalHarmonicsL2();
                finiteProbe.AddAmbientLight(ambientColor);
                RenderSettings.ambientProbe = finiteProbe;
                renderer.sharedMaterial = original;
                camera.Render();
                WriteImage(target, "finite-probe-only");
                RenderSettings.ambientProbe = finiteProbe;
                RenderSettings.reflectionIntensity = 0f;
                camera.Render();
                WriteImage(target, "finite-probe-no-reflections");
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.ambientProbe = finiteProbe;

                foreach (var light in lights)
                {
                    light.enabled = false;
                }

                camera.Render();
                WriteImage(target, "finite-probe-no-lights");

                for (int restoredLightIndex = 0; restoredLightIndex < lights.Length; restoredLightIndex++)
                {
                    lights[restoredLightIndex].enabled = lightEnabled[restoredLightIndex];
                }

                foreach (var light in lights)
                {
                    light.enabled = false;
                }

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f);
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.reflectionIntensity = 0f;

                // Finish with known flat lighting so the comparison does not depend on the scene's ambient probe.
                var controlledProbe = new SphericalHarmonicsL2();
                controlledProbe.AddAmbientLight(new Color(0.4f, 0.4f, 0.4f));
                RenderSettings.ambientProbe = controlledProbe;
                renderer.sharedMaterial = original;
                camera.Render();
                WriteImage(target, "controlled-lighting");
                renderer.sharedMaterial = withoutEmission;
                camera.Render();
                WriteImage(target, "controlled-without-emission");
            }
            finally
            {
                renderer.sharedMaterial = original;

                for (int cleanupLightIndex = 0; cleanupLightIndex < lights.Length; cleanupLightIndex++)
                {
                    lights[cleanupLightIndex].enabled = lightEnabled[cleanupLightIndex];
                }

                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientColor;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.ambientProbe = ambientProbe;
                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView2;
                RenderTexture.active = previousActive;
                Destroy(variant);
                Destroy(withoutEmission);
                Destroy(unlit);
                target.Release();
                Destroy(target);
            }
        }

        private void WriteImage(RenderTexture target, string name)
        {
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                string path = Path.Combine(directory, "VisualProbe-" + name + ".png");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                Debug.Log("RUNTIME VISUAL CAPTURE " + path);
            }
            finally
            {
                Destroy(pixels);
            }
        }
    }
}
#endif
