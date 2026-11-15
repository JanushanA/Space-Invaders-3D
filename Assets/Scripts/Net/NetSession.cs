using UnityEngine;
namespace Game.Net
{
    // Shared lobby and match state survives scene changes for either LAN or Relay connections.
    public static class NetSession
    {
        public const int MinPlayers = 1;
        public const int MaxPlayers = 5;
        public const int GamePort = 7777;
        public const int BeaconPort = 7788;
        public static bool Active;
        public static bool IsHost;
        public static bool InMatch;
        public static bool HoldLobby;
        public static bool ViaRelay;
        public static string Code = "";
        public static int LocalSlot;
        public static int PlayerCount = 1;
        public static int SelectedLevel = 1;
        public static string Status = "";
        public static bool Occupied0;
        public static bool Occupied1;
        public static bool Occupied2;
        public static bool Occupied3;
        public static bool Occupied4;

        public static bool SlotTaken(int slot)
        {
            // Maps a numeric lobby slot to its stored occupied flag.
            if (slot == 0)
            {
                return Occupied0;
            }

            if (slot == 1)
            {
                return Occupied1;
            }

            if (slot == 2)
            {
                return Occupied2;
            }

            if (slot == 3)
            {
                return Occupied3;
            }

            if (slot == 4)
            {
                return Occupied4;
            }

            return false;
        }

        public static void SetSlot(int slot, bool taken)
        {
            // Updates only the requested slot, allowing host packets to add or remove one pilot at a time.
            if (slot == 0)
            {
                Occupied0 = taken;
            }
            else if (slot == 1)
            {
                Occupied1 = taken;
            }
            else if (slot == 2)
            {
                Occupied2 = taken;
            }
            else if (slot == 3)
            {
                Occupied3 = taken;
            }
            else if (slot == 4)
            {
                Occupied4 = taken;
            }
        }

        public static void ClearSlots()
        {
            // Clears every occupied flag when leaving a lobby so the next session starts empty.
            Occupied0 = false;
            Occupied1 = false;
            Occupied2 = false;
            Occupied3 = false;
            Occupied4 = false;
        }

        public static void Reset()
        {
            // Restores every multiplayer setting to safe single-player defaults between sessions.
            Active = false;
            IsHost = false;
            InMatch = false;
            HoldLobby = false;
            ViaRelay = false;
            Code = "";
            LocalSlot = 0;
            PlayerCount = 1;
            SelectedLevel = 1;
            Status = "";
            ClearSlots();
        }

        public static string PilotLabel(int slot)
        {
            // Converts the zero-based internal slot into the one-based label players see in the lobby UI.
            return "PILOT " + (slot + 1);
        }
    }
}
