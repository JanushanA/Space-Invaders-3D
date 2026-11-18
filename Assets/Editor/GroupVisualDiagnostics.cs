using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Captures material comparisons and reports mesh, texture and lighting data for the imported group ship.
public static class GroupVisualDiagnostics
{
    public static void Render()
    {
        EditorSceneManager.OpenScene("Assets/Space-Invaders-3D.unity");
        var renderer = GameObject.Find("GroupShipDisplay").GetComponentInChildren<MeshRenderer>();
        var original = renderer.sharedMaterial;
        var camera = Camera.main;
        camera.transform.position = renderer.bounds.center + new Vector3(0f, 2f, -6f);
        camera.transform.LookAt(renderer.bounds.center);
        camera.fieldOfView = 45f;
        var probe = new RenderTexture(1280, 900, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1280, 900, TextureFormat.RGB24, false);
        var variant = new Material(original);
        var unlit = Game.Core.VisualUtil.MakeUnlitTexture(original.mainTexture);
        var previous = RenderTexture.active;
        try
        {
            variant.DisableKeyword("_DETAIL_MULX2");
            variant.DisableKeyword("_METALLICGLOSSMAP");
            camera.targetTexture = probe;
            string[] names = { "original", "without-detail", "unlit" };
            Material[] materials = { original, variant, unlit };
            System.IO.Directory.CreateDirectory("Logs");

            for (int viewIndex = 0; viewIndex < names.Length; viewIndex++)
            {
                renderer.sharedMaterial = materials[viewIndex];
                camera.Render();
                RenderTexture.active = probe;
                image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0);
                image.Apply();
                System.IO.File.WriteAllBytes("Logs/VisualProbe-" + names[viewIndex] + ".png", image.EncodeToPNG());
                Debug.Log("GROUP VISUAL RENDER " + names[viewIndex] + " shader=" + materials[viewIndex].shader.name);
            }
        }
        finally
        {
            renderer.sharedMaterial = original;
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(variant);
            UnityEngine.Object.DestroyImmediate(unlit);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(probe);
        }
    }

    [MenuItem("Tools/Group Game/Inspect group visual assets")]
    public static void Run()
    {
        Debug.Log("GROUP VISUAL graphics=" + SystemInfo.graphicsDeviceType);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ArvisMag_Assets/BlockySpaceShips/Fihter/BlockyShip_Fihter_.prefab");
        InspectHull(prefab.transform);

        foreach (string scene in new[] { "Assets/Space-Invaders-3D.unity", "Assets/Scenes/Combat_Test.unity" })
        {
            EditorSceneManager.OpenScene(scene);
            Debug.Log("GROUP VISUAL scene=" + scene + " ambient=" + RenderSettings.ambientMode + "/" + RenderSettings.ambientIntensity);

            foreach (var filter in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
            {
                if (filter.sharedMesh != null && AssetDatabase.GetAssetPath(filter.sharedMesh).Contains("ArvisMag_Assets"))
                {
                    InspectHull(filter.transform);
                }
            }
        }
    }

    public static void InspectHull(Transform hull)
    {
        var filter = hull.GetComponent<MeshFilter>();
        var renderer = hull.GetComponent<MeshRenderer>();
        if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null)
        {
            throw new Exception("Incomplete group hull visual: " + hull.name);
        }

        var mesh = filter.sharedMesh;
        var material = renderer.sharedMaterial;
        var textureCoordinates = new List<Vector2>();
        mesh.GetUVs(0, textureCoordinates);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        foreach (var point in textureCoordinates)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        Debug.Log("GROUP VISUAL hull=" + hull.name + " mesh=" + mesh.name + " vertices=" + mesh.vertexCount + " uv0=" + textureCoordinates.Count + " min=" + min.ToString("F5") + " max=" + max.ToString("F5") + " bounds=" + mesh.bounds + " normals=" + mesh.normals.Length);
        Debug.Log("GROUP VISUAL material=" + material.name + " shader=" + material.shader.name + " supported=" + material.shader.isSupported + " color=" + material.color + " textureScale=" + material.mainTextureScale + " textureOffset=" + material.mainTextureOffset + " keywords=" + string.Join(",", material.shaderKeywords));

        foreach (string property in new[] { "_MainTex", "_EmissionMap", "_BumpMap", "_SpecGlossMap" })
        {
            if (!material.HasProperty(property))
            {
                continue;
            }

            var texture = material.GetTexture(property);
            Debug.Log("GROUP VISUAL " + property + "=" + (texture == null ? "NULL" : texture.name + " " + texture.width + "x" + texture.height + " " + AssetDatabase.GetAssetPath(texture)));
        }

        if (material.HasProperty("_EmissionColor"))
        {
            Debug.Log("GROUP VISUAL emission=" + material.GetColor("_EmissionColor"));
        }

        if (textureCoordinates.Count != mesh.vertexCount || max.x - min.x < 0.1f || max.y - min.y < 0.1f)
        {
            throw new Exception("Group hull UV0 missing or collapsed");
        }

        if (material.mainTexture == null || material.mainTexture.width <= 1 || material.mainTextureScale.sqrMagnitude < 0.01f)
        {
            throw new Exception("Group hull albedo missing or collapsed");
        }
    }
}
