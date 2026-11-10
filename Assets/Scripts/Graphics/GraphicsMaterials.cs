using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Converts source material textures into the channels expected by the high-definition renderer.
namespace Game.Graphics
{
    /// <summary>Creates owned materials. Callers share and dispose them; source art is never mutated.</summary>

    public static class GraphicsMaterials
    {
        private static readonly Dictionary<(Texture, Texture, Texture, Texture), RenderTexture> masks =
            new Dictionary<(Texture, Texture, Texture, Texture), RenderTexture>();

        public static Material Lit(string name, Color color, float metallic = 0f, float smoothness = 0.5f)
        {
            var template = Resources.Load<Material>("Graphics/LitTemplate");
            var material = template != null ? new Material(template) : new Material(Shader.Find("HDRP/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            return material;
        }

        public static Material Unlit(string name, Color color, bool additive = false)
        {
            var shader = Resources.Load<Shader>("Shaders/SpaceEffect");
            var material = new Material(shader) { name = name, enableInstancing = true };

            // HDR intensity belongs in RGB; alpha above one can subtract the destination during blending.
            color.a = Mathf.Clamp01(color.a);
            material.SetColor("_Color", color);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            return material;
        }

        public static Material ConvertSurface(Material source)
        {
            bool specular = source.shader.name == "Standard (Specular setup)";
            Color color = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            var target = Lit(source.name + " (runtime HDRP)", color,
                source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0.15f,
                source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.4f);
            CopyTexture(source, "_MainTex", target, "_BaseColorMap");
            target.SetFloat("_MaterialID", specular ? 4f : 1f);
            if (specular)
            {
                CopyTexture(source, "_SpecGlossMap", target, "_SpecularColorMap");
                target.SetColor("_SpecularColor", source.GetTexture("_SpecGlossMap") != null ? Color.white : source.GetColor("_SpecColor"));
            }

            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
            {
                target.SetTexture("_NormalMap", source.GetTexture("_BumpMap"));
                target.SetFloat("_NormalScale", source.GetFloat("_BumpScale"));
                target.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
                target.EnableKeyword("_NORMALMAP");
            }

            CopyTexture(source, "_EmissionMap", target, "_EmissiveColorMap");
            target.SetColor("_EmissiveColor", source.IsKeywordEnabled("_EMISSION") ? source.GetColor("_EmissionColor") : Color.black);
            target.SetFloat("_UseEmissiveIntensity", 0f);
            if (source.HasProperty("_MainTex"))
            {
                // Built-in Standard samples emission using its main UV transform.
                target.SetTextureScale("_EmissiveColorMap", source.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_EmissiveColorMap", source.GetTextureOffset("_MainTex"));
            }

            string smoothnessProperty = source.GetFloat("_SmoothnessTextureChannel") > 0.5f ? "_MainTex" : specular ? "_SpecGlossMap" : "_MetallicGlossMap";
            Texture smoothness = source.GetTexture(smoothnessProperty);
            Texture metallic = specular ? null : source.GetTexture("_MetallicGlossMap");
            Texture occlusion = source.GetTexture("_OcclusionMap");
            Texture detailMask = source.GetTexture("_DetailMask");
            if (smoothness != null || metallic != null || occlusion != null || detailMask != null)
            {
                target.SetTexture("_MaskMap", PackMask(source.name, metallic, occlusion, detailMask, smoothness));
                target.SetFloat("_MetallicRemapMax", specular ? 0f : metallic != null ? 1f : source.GetFloat("_Metallic"));
                target.SetFloat("_AORemapMin", 1f - source.GetFloat("_OcclusionStrength"));
                target.SetFloat("_SmoothnessRemapMax", source.GetFloat(smoothness != null ? "_GlossMapScale" : "_Glossiness"));
            }

            int mode = source.GetInt("_Mode");
            target.SetFloat("_SurfaceType", mode >= 2 ? 1f : 0f);
            target.SetFloat("_BlendMode", 0f);
            target.SetFloat("_AlphaCutoffEnable", mode == 1 ? 1f : 0f);
            target.SetFloat("_AlphaCutoff", source.GetFloat("_Cutoff"));
            target.SetFloat("_EnableBlendModePreserveSpecularLighting", mode == 2 ? 0f : 1f);
            if (mode >= 2)
            {
                target.SetFloat("_TransparentZWrite", 0f);
            }

            HDMaterial.ValidateMaterial(target);
            return target;
        }

        private static void CopyTexture(Material source, string from, Material destination, string destination2)
        {
            if (!source.HasProperty(from))
            {
                return;
            }

            destination.SetTexture(destination2, source.GetTexture(from));
            destination.SetTextureScale(destination2, source.GetTextureScale(from));
            destination.SetTextureOffset(destination2, source.GetTextureOffset(from));
        }

        private static RenderTexture PackMask(string name, Texture metallic, Texture occlusion, Texture detail, Texture smoothness)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return null;
            }

            var key = (metallic, occlusion, detail, smoothness);
            if (masks.TryGetValue(key, out RenderTexture cached) && cached != null)
            {
                return cached;
            }

            int width = 4;
            int height = 4;

            foreach (Texture texture in new[] { metallic, occlusion, detail, smoothness })
            {
                if (texture != null)
                {
                    width = Mathf.Max(width, texture.width);
                    height = Mathf.Max(height, texture.height);
                }
            }

            var output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = name + " (runtime HDRP mask)",
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };
            output.Create();
            var pack = new Material(Resources.Load<Shader>("Shaders/SurfaceMask"));
            pack.SetTexture("_MetallicMap", metallic != null ? metallic : Texture2D.whiteTexture);
            pack.SetTexture("_OcclusionMap", occlusion != null ? occlusion : Texture2D.whiteTexture);
            pack.SetTexture("_DetailMap", detail != null ? detail : Texture2D.whiteTexture);
            pack.SetTexture("_SmoothnessMap", smoothness != null ? smoothness : Texture2D.whiteTexture);
            UnityEngine.Graphics.Blit(Texture2D.whiteTexture, output, pack);
            if (Application.isPlaying)
            {
                Object.Destroy(pack);
            }
            else
            {
                Object.DestroyImmediate(pack);
            }

            masks[key] = output;
            return output;
        }

        public static void ReleaseConvertedSurface(Material material)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(material);
            }
            else
            {
                Object.DestroyImmediate(material);
            }
        }

        public static void ReleaseMaskCache()
        {
            // One packed map per source texture tuple, shared across all cloned fighter materials.
            foreach (RenderTexture packedMaterialTexture in masks.Values)
            {
                if (packedMaterialTexture == null)
                {
                    continue;
                }

                packedMaterialTexture.Release();
                if (Application.isPlaying)
                {
                    Object.Destroy(packedMaterialTexture);
                }
                else
                {
                    Object.DestroyImmediate(packedMaterialTexture);
                }
            }

            masks.Clear();
        }
    }
}
