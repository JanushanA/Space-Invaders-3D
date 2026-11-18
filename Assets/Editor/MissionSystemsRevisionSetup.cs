using UnityEditor;
using UnityEngine;
namespace Game.EditorTools
{
    // Runs the combined mission revision checks and delegates building to the campaign setup.
    public static class MissionSystemsRevisionSetup
    {
        public static void Prepare()
        {
            AssetDatabase.ImportAsset("Assets/Resources/UI/FlightCrosshair.png", ImportAssetOptions.ForceUpdate);
            Checks();
        }

        public static void Checks()
        {
            SystemsEconomyChecks.Run();
            MissionSystemsChecks.Run();
            MissionPresentationChecks.Run();
            CampaignExpansionSetup.AllChecks();
            Debug.Log("MISSION_REVISION_ALL_CHECKS_PASS");
        }

        public static void Build()
        {
            CampaignExpansionSetup.Build();
        }
    }
}
