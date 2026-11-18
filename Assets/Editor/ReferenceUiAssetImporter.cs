using UnityEditor;
using UnityEngine;

// Import individual icons and the supplied flight crosshair as UI sprites.
public sealed class ReferenceUiAssetImporter : AssetPostprocessor
{
    [MenuItem("Tools/Group Game/Configure reference UI sprites")]
    public static void ConfigureAll()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UI/Icons" }))
        {
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }

        AssetDatabase.ImportAsset("Assets/Resources/UI/FlightCrosshair.png", ImportAssetOptions.ForceUpdate);
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/UI/Icons/") && assetPath != "Assets/Resources/UI/FlightCrosshair.png")
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = assetPath.EndsWith("FlightCrosshair.png") ? 2048 : 256;
        importer.isReadable = false;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
    }
}
