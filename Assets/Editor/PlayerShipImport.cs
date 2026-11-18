using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
namespace Game.EditorTools
{
    // Converts source ship textures to HDRP materials and builds the runtime player hull prefabs.
    public static class PlayerShipImport
    {
        [MenuItem("Tools/Campaign/Configure player ships")]
        public static void Configure()
        {
            string shipAssetDirectory = "Assets/Resources/Ships/Proteron/";
            string[] names = { "hippocrates", "pythagoras", "watson" };

            for (int hullIndex = 0; hullIndex < 3; hullIndex++)
            {
                string textureName = names[hullIndex];
                var importer = AssetImporter.GetAtPath(shipAssetDirectory + textureName + ".obj") as ModelImporter;
                importer.isReadable = true;
                importer.SaveAndReimport();
                Texture2D Load(string suffix, bool normal = false)
                {
                    string path = shipAssetDirectory + textureName + "_" + suffix + ".png";
                    var textureImporter = (TextureImporter)AssetImporter.GetAtPath(path);
                    textureImporter.isReadable = true;
                    textureImporter.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                    textureImporter.sRGBTexture = suffix == "diffuse" || suffix == "emission";
                    textureImporter.maxTextureSize = 2048;
                    textureImporter.SaveAndReimport();
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                var diffuse = Load("diffuse");
                var normal = Load("normal", true);
                var emission = Load("emission");
                var metallic = Load("metallic");
                var rough = Load("roughness");

                // Pack HDRP metallic, occlusion, detail and smoothness channels; smoothness is inverse roughness.
                var pixels = metallic.GetPixels();
                var roughPixels = rough.GetPixels();

                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    pixels[pixelIndex] = new Color(pixels[pixelIndex].r, 1, 1, 1 - roughPixels[pixelIndex].r);
                }

                var materialMaskTexture = new Texture2D(metallic.width, metallic.height, TextureFormat.RGBA32, true, true);
                materialMaskTexture.SetPixels(pixels);
                materialMaskTexture.Apply();
                string maskPath = shipAssetDirectory + textureName + "_mask.png";
                System.IO.File.WriteAllBytes(maskPath, materialMaskTexture.EncodeToPNG());
                Object.DestroyImmediate(materialMaskTexture);
                AssetDatabase.ImportAsset(maskPath);
                var maskImporter = (TextureImporter)AssetImporter.GetAtPath(maskPath);
                maskImporter.sRGBTexture = false;
                maskImporter.maxTextureSize = 2048;
                maskImporter.SaveAndReimport();
                materialMaskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(shipAssetDirectory + textureName + "_mask.asset") != null)
                {
                    AssetDatabase.DeleteAsset(shipAssetDirectory + textureName + "_mask.asset");
                }

                // Reuse the material asset so existing references survive a repeated import.
                string matPath = shipAssetDirectory + textureName + "_HDRP.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material == null)
                {
                    material = Game.Graphics.GraphicsMaterials.Lit(textureName, Color.white, 0.75f, 0.45f);
                    AssetDatabase.CreateAsset(material, matPath);
                }

                material.SetTexture("_BaseColorMap", diffuse);
                material.SetTexture("_NormalMap", normal);
                material.SetFloat("_NormalScale", 0.7f);
                material.SetTexture("_MaskMap", materialMaskTexture);
                material.SetTexture("_EmissiveColorMap", emission);
                material.SetColor("_EmissiveColor", Color.white * 2);
                HDMaterial.ValidateMaterial(material);
                EditorUtility.SetDirty(material);
                var root = new GameObject("Player" + hullIndex);
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(shipAssetDirectory + textureName + ".obj"), root.transform);
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;

                foreach (var renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    renderer.sharedMaterial = material;
                }

                float scale = 4.5f / Mathf.Max(bounds.size.x, bounds.size.z);
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = -bounds.center * scale;

                // Source OBJ noses point toward negative Z; Unity flight looks along positive Z.
                var mount = new GameObject("Forward mount");
                mount.transform.SetParent(root.transform, false);
                model.transform.SetParent(mount.transform, false);
                mount.transform.localRotation = Quaternion.Euler(0, 180, 0);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Ships/Player" + hullIndex + ".prefab");
                Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerShipImport] Three licensed HDRP player hulls ready");
        }
    }
}
