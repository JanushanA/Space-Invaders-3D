using UnityEditor;
using UnityEngine;
namespace Game.EditorTools
{
    // Imports campaign art, runs its validation suite and builds the player with asset license notices.
    public static class CampaignExpansionSetup
    {
        public static void Prepare()
        {
            ReferenceUiAssetImporter.ConfigureAll();
            PlayerShipImport.Configure();

            // Prepare salvage art for the inventory UI before running the campaign checks.
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Salvage" }))
            {
                var textureImporter = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as TextureImporter;
                textureImporter.alphaIsTransparency = true;
                textureImporter.mipmapEnabled = false;
                textureImporter.maxTextureSize = 512;
                textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                textureImporter.SaveAndReimport();
            }

            Checks();
            AssetDatabase.SaveAssets();
        }

        public static void Checks()
        {
            ExpansionChecks.Run();
            AiChecks.Run();
            FractureChecks.Run();
            EquipmentChecks.Run();
            ReferenceUiChecks.Run();
            Debug.Log("[CampaignExpansion] Core checks PASS");
        }

        public static void Build()
        {
            GroupIntegrationSetup.BuildWindows();

            // Ship the source-art license notices alongside the finished player.
            System.IO.File.WriteAllText("Builds/Windows/ThirdPartyNotices.txt", System.IO.File.ReadAllText("Assets/Resources/Ships/Proteron/LICENSE.txt") + "\n\n" + System.IO.File.ReadAllText("Assets/Resources/UI/Fonts/OFL.txt"));
        }

        public static void AllChecks()
        {
            Checks();
            ImprovingGraphicsChecks.Run();
        }
    }
}
