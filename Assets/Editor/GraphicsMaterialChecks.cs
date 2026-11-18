using System;
using Game.Graphics;
using UnityEditor;
using UnityEngine;

// Checks runtime HDRP conversion against the authored textures, colors and UV transforms.
public static class GraphicsMaterialChecks
{
    [MenuItem("Tools/Group Game/Check runtime surface conversion")]
    public static void Run()
    {
        var authored = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArvisMag_Assets/BlockySpaceShips/Fihter/FbxModel/Materials/BlockyShip_Fiter_mat.mat");
        Require(authored != null, "Authored fighter material missing");
        string before = EditorJsonUtility.ToJson(authored);
        var source = new Material(authored);
        Material converted = null;
        try
        {
            source.SetTextureScale("_MainTex", new Vector2(2, 3));
            source.SetTextureOffset("_MainTex", new Vector2(0.1f, 0.2f));
            converted = GraphicsMaterials.ConvertSurface(source);
            Require(converted.shader.name == "HDRP/Lit" && converted.GetFloat("_MaterialID") == 4, "Specular Standard was not converted to the HDRP specular workflow");
            Require(converted.GetTexture("_BaseColorMap") == source.GetTexture("_MainTex"), "Authored albedo texture replaced");
            Require(converted.GetTexture("_SpecularColorMap") == source.GetTexture("_SpecGlossMap"), "Authored specular texture lost");
            Require(converted.GetTexture("_EmissiveColorMap") == source.GetTexture("_EmissionMap"), "Authored emission texture lost");
            Require(converted.GetColor("_EmissiveColor") == source.GetColor("_EmissionColor"), "Enabled source emission color lost");
            Require(converted.GetTextureScale("_BaseColorMap") == source.GetTextureScale("_MainTex") &&
                converted.GetTextureOffset("_BaseColorMap") == source.GetTextureOffset("_MainTex"), "Albedo UV transform lost");
            Require(converted.GetTextureScale("_EmissiveColorMap") == source.GetTextureScale("_MainTex"), "Standard emission must follow the source base UV transform");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Require(converted.GetTexture("_MaskMap") is RenderTexture, "Specular-map smoothness alpha was not packed for HDRP");
            }

            Require(Mathf.Approximately(converted.GetFloat("_SmoothnessRemapMax"), source.GetFloat("_GlossMapScale")), "Smoothness scale changed");
            Require(EditorJsonUtility.ToJson(authored) == before, "Runtime conversion mutated authored material data");
            Debug.Log("GRAPHICS MATERIAL CHECKS: PASS specular workflow, source preservation, albedo/specular/emission textures and UV transforms; mask GPU check " +
                (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ? "SKIPPED (Null graphics device)" : "PASS"));
        }
        finally
        {
            if (converted != null)
            {
                GraphicsMaterials.ReleaseConvertedSurface(converted);
            }

            GraphicsMaterials.ReleaseMaskCache();
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
