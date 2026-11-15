using Game.Core;
using Game.Progression;
using UnityEngine.SceneManagement;
namespace Game.Campaign
{
    public static class CampaignSession
    {
        public static bool StoryMode;
        public static bool InHideout;
        public static int Seed;
        static SectorNetwork network;

        public static SectorNetwork Network
        {
            get
            {
                var state = PilotInventory.State;
                if (state.sectorSeed == 0)
                {
                    state.sectorSeed = System.Environment.TickCount & int.MaxValue;
                    if (state.sectorSeed == 0)
                    {
                        state.sectorSeed = 81511;
                    }

                    PilotInventory.Save();
                }

                if (network == null || network.Seed != state.sectorSeed)
                {
                    network = new SectorNetwork(state.sectorSeed);
                }

                return network;
            }
        }

        public static int CurrentSector => UnityEngine.Mathf.Clamp(PilotInventory.State.currentSector, 1, 8);

        public static System.Collections.Generic.IReadOnlyList<int> JumpDestinations
        {
            get
            {
                var routes = new System.Collections.Generic.List<int>();

                for (int index = 1; index <= 8; index++)
                {
                    if (CanLaunch(index))
                    {
                        routes.Add(index);
                    }
                }

                return routes;
            }
        }

        public static bool CanLaunch(int level)
        {
            if (level < 1 || level > 8)
            {
                return false;
            }

            // The first sector opens the network; later travel requires clearing the departure sector.
            if ((PilotInventory.State.completedSectorsMask & 1) == 0)
            {
                return level == 1;
            }

            if (level == CurrentSector)
            {
                return true;
            }

            bool currentCleared = (PilotInventory.State.completedSectorsMask & (1 << (CurrentSector - 1))) != 0;
            return currentCleared && Network.Adjacent(CurrentSector, level);
        }

        public static void BeginRun(int seed)
        {
            var state = PilotInventory.State;
            state.sectorSeed = seed == 0 ? 81511 : seed;
            state.currentSector = 1;
            state.completedSectorsMask = 0;
            Seed = state.sectorSeed;
            network = new SectorNetwork(Seed);
            PilotInventory.Save();
        }

        public static void NewRun() => BeginRun(System.Environment.TickCount & int.MaxValue);
        public static readonly string[] Locations = { "ASTER / HOME ORBIT", "VESPER / RED FRONTIER", "NIVALIS / ICE SHELF", "PYRE / VOLCANIC RIFT", "ZEPHYR / GAS GIANT", "AUREOLE / RING CROSSING", "UMBRA / ECLIPSE", "PELAGIC / LAST LIGHT" };
        public static readonly string[] Titles = { "The evacuation", "Break the blockade", "Cold signal", "Ash and alloy", "Storm runners", "The broken ring", "A quiet rebellion", "The way home" };
        public static readonly string[] Briefings = {
            "Commander Vale: Aster's civilian convoy is trapped. Recover its navigation core, escort the transport through the debris and secure its jump beacon. Bring our people home.",
            "Commander Vale: The mothership is holding Vesper's fuel lanes. Recover a cipher cell, align the relay sequence and bring down its commander. Intelligence confirms a legendary weapon aboard.",
            "Dr. Imani: A distress signal is repeating beneath Nivalis' ice storms. Recover the recorder and protect the research tender. The signal contains a map of the invasion fleet.",
            "Chief Rook: Pyre's refineries can forge the armour we need. Recover the cooling manifold, synchronise three relays and defend the transfer. Leave nothing useful to the invaders.",
            "Commander Vale: Zephyr's clouds hide a refugee carrier. Recover its launch codes and escort it out while the fleet searches. Stay close; its shields are failing.",
            "Chief Rook: The ring station has been cut apart. Find the power core, restore the relays in the transmitted order, then secure the cargo. Shoot damaged modules for emergency salvage.",
            "Dr. Imani: Umbra's eclipse gives us one quiet approach. Retrieve the resistance archive and protect their courier. An ace pilot is patrolling this sector; keep moving.",
            "Commander Vale: Pelagic is the final jump. Recover the beacon key, realign the ancient relay and hold the extraction point. Every survivor you saved is counting on this route."
        };

        public static void OpenHideout()
        {
            Game.Net.NetRunner.Instance?.Leave();
            StoryMode = true;
            InHideout = true;
            GameSession.ShowLevelSelect = false;
            SceneManager.LoadScene(GameSession.CombatScene);
        }

        public static bool Launch(int level)
        {
            if (!CanLaunch(level))
            {
                return false;
            }

            StoryMode = true;
            InHideout = false;
            GameSession.SelectedLevel = level;
            GameSession.EnteredFromMenu = true;
            Seed = unchecked(Network.Seed + level * 104729);
            PilotInventory.State.currentSector = level;
            PilotInventory.Save();
            SceneManager.LoadScene(GameSession.CombatScene);
            return true;
        }

        public static void Complete()
        {
            PilotInventory.State.completedCampaigns = System.Math.Max(PilotInventory.State.completedCampaigns, GameSession.SelectedLevel);
            PilotInventory.State.completedSectorsMask |= 1 << (GameSession.SelectedLevel - 1);
            PilotInventory.State.currentSector = GameSession.SelectedLevel;
            PilotInventory.Grant(0, 8, -1, 150);
            PilotInventory.Save();
            InHideout = true;
        }

        public static void Arcade()
        {
            StoryMode = false;
            InHideout = false;
            Seed = 0;
        }
    }
}
