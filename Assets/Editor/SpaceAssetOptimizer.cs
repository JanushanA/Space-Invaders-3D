using System;
using UnityEditor;
using UnityEngine;
namespace Game.EditorTools
{
    /// <summary>Batch optimizer restricted to newly generated graphics assets.</summary>

    public static class SpaceAssetOptimizer
    {
        private static readonly string[] Roots = { "Assets/Resources/Graphics", "Assets/ImprovingGraphics" };

        [MenuItem("Tools/Group Game/Optimize generated space assets")]
        public static void Optimize()
        {
            int materials = 0;
            int prefabs = 0;

            foreach (string root in Roots)
            {
                if (!AssetDatabase.IsValidFolder(root))
                {
                    continue;
                }

                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { root }))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    materials++;
                }

                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var instance = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        OptimizeRoot(instance);
                        PrefabUtility.SaveAsPrefabAsset(instance, path);
                        prefabs++;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(instance);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[SpaceAssetOptimizer] Optimized {materials} generated materials and {prefabs} generated prefabs; source art untouched.");
        }

        public static void OptimizeRoot(GameObject root)
        {
            foreach (var detailLevels in root.GetComponentsInChildren<LODGroup>(true))
            {
                var levels = detailLevels.GetLODs();

                for (int detailLevelIndex = 0; detailLevelIndex < levels.Length; detailLevelIndex++)
                {
                    levels[detailLevelIndex].screenRelativeTransitionHeight = Mathf.Lerp(0.12f, 0.012f, levels.Length == 1 ? 1f : (float)detailLevelIndex / (levels.Length - 1));
                }

                detailLevels.SetLODs(levels);
                detailLevels.fadeMode = LODFadeMode.None;
                detailLevels.RecalculateBounds();
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.IndexOf("station", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                foreach (var renderer in child.GetComponentsInChildren<MeshRenderer>(true))
                {
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.BatchingStatic);
                }
            }
        }
    }
}
