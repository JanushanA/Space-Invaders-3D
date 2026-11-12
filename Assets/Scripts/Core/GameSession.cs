using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Core
{
    // Selected sector and saved stars. Shared by the menu and combat.
    // Keeps run progress and saved player data available while scenes change.
    public static class GameSession
    {
        public const int LevelCount = 8;
        public const string MenuScene = "Space-Invaders-3D";
        public const string CombatScene = "Combat_Test";
        public static int SelectedLevel = 1;
        public static bool ShowLevelSelect;
        public static bool ShowMpLobby;
        public static bool EnteredFromMenu;
        public static bool SuppressSave;
        public static int RunScore;
        public static string LastKiller = "UNKNOWN";
        private const string StarsKey = "sector_stars_";
        private const string UnlockedKey = "sector_unlocked_";
        private const string PilotSavedKey = "pilot_saved";
        private const string PilotLevelKey = "pilot_level";
        private const string PilotXpKey = "pilot_xp";
        private const string PilotPointsKey = "pilot_points";
        private const string PilotSkillsKey = "pilot_skills";
        private const string PilotSlotKey = "pilot_slot_";
        private const string PilotBindKey = "pilot_bind_";
        private const string InvertKey = "invert_look";
        private const string BestScoreKey = "pilot_best_score";

        public static bool IsUnlocked(int level)
        {
            // Always permits the first sector, then checks an explicit unlock or stars from the previous sector.
            if (level <= 1)
            {
                return true;
            }

            if (PlayerPrefs.GetInt(UnlockedKey + level, 0) == 1)
            {
                return true;
            }

            return GetStars(level - 1) > 0;
        }

        public static int GetStars(int level)
        {
            // Clamps saved star data so a corrupt preference cannot show an invalid rating in level select.
            return Mathf.Clamp(PlayerPrefs.GetInt(StarsKey + level, 0), 0, 3);
        }

        public static void CompleteLevel(int level, int stars)
        {
            // Saves only when progression is enabled, allowing testing resets to avoid rewriting deleted values.
            if (SuppressSave)
            {
                return;
            }

            int current = GetStars(level);
            if (stars > current)
            {
                // Keeps the best result rather than replacing a three-star completion with a later lower score.
                PlayerPrefs.SetInt(StarsKey + level, Mathf.Clamp(stars, 0, 3));
            }

            int next = level + 1;
            if (next <= LevelCount)
            {
                // Unlocks exactly the next sector after a successful completion.
                PlayerPrefs.SetInt(UnlockedKey + next, 1);
            }

            PlayerPrefs.Save();
        }

        public static int WavesToClear()
        {
            // Adds one required wave per sector so later levels have a simple predictable difficulty increase.
            return 3 + Mathf.Max(0, SelectedLevel - 1);
        }

        public static int PartySize
        {
            get
            {
                if (Game.Net.NetSession.Active && Game.Net.NetSession.PlayerCount > 1)
                {
                    // Uses the multiplayer count only inside an active lobby and clamps it to the allowed capacity.
                    return Mathf.Clamp(Game.Net.NetSession.PlayerCount, 1, Game.Net.NetSession.MaxPlayers);
                }

                return 1;
            }
        }

        public static bool InvertLook
        {
            // Reads the saved integer as a boolean because PlayerPrefs does not store bool values directly.
            get
            {
                return PlayerPrefs.GetInt(InvertKey, 0) == 1;
            }
        }

        public static void SetInvertLook(bool isEnabled)
        {
            // Converts a bool into PlayerPrefs' integer format and writes it immediately for the next session.
            if (SuppressSave)
            {
                return;
            }

            int value = 0;
            if (isEnabled)
            {
                value = 1;
            }

            PlayerPrefs.SetInt(InvertKey, value);
            PlayerPrefs.Save();
        }

        public static bool HasPilotSave()
        {
            // Checks the marker written with player data to distinguish a new pilot from a saved one.
            return PlayerPrefs.GetInt(PilotSavedKey, 0) == 1;
        }

        public static void SavePilotStats(int level, int experiencePoints, int points)
        {
            // Stores progression in PlayerPrefs so it survives closing the game and changing scenes.
            if (SuppressSave)
            {
                return;
            }

            PlayerPrefs.SetInt(PilotSavedKey, 1);
            PlayerPrefs.SetInt(PilotLevelKey, Mathf.Max(1, level));
            PlayerPrefs.SetInt(PilotXpKey, Mathf.Max(0, experiencePoints));
            PlayerPrefs.SetInt(PilotPointsKey, Mathf.Max(0, points));
            PlayerPrefs.Save();
        }

        public static void LoadPilotStats(out int level, out int experiencePoints, out int points)
        {
            // Reads saved values with safe defaults for a new pilot with no existing save.
            level = Mathf.Max(1, PlayerPrefs.GetInt(PilotLevelKey, 1));
            experiencePoints = Mathf.Max(0, PlayerPrefs.GetInt(PilotXpKey, 0));
            points = Mathf.Max(0, PlayerPrefs.GetInt(PilotPointsKey, 0));
        }

        public static void SaveSkillList(string savedSkillIds)
        {
            // Stores unlocked IDs as one compact string because PlayerPrefs has no collection type.
            if (SuppressSave)
            {
                return;
            }

            if (savedSkillIds == null)
            {
                savedSkillIds = "";
            }

            PlayerPrefs.SetInt(PilotSavedKey, 1);
            PlayerPrefs.SetString(PilotSkillsKey, savedSkillIds);
            PlayerPrefs.Save();
        }

        public static string LoadSkillList()
        {
            // Returns an empty skill list for a player who has never saved progression.
            return PlayerPrefs.GetString(PilotSkillsKey, "");
        }

        public static void SaveHotbar(int slotIndex, int skillId, int bindCode)
        {
            // Uses the slot index in each key so all three selected skills and their binds persist independently.
            if (SuppressSave)
            {
                return;
            }

            PlayerPrefs.SetInt(PilotSavedKey, 1);
            PlayerPrefs.SetInt(PilotSlotKey + slotIndex, skillId);
            PlayerPrefs.SetInt(PilotBindKey + slotIndex, bindCode);
            PlayerPrefs.Save();
        }

        public static void LoadHotbarSlot(int slotIndex, out int skillId, out int bindCode, out bool found)
        {
            // Uses the skill key as the presence marker because an unassigned slot has no saved record.
            found = PlayerPrefs.HasKey(PilotSlotKey + slotIndex);
            skillId = PlayerPrefs.GetInt(PilotSlotKey + slotIndex, 0);
            bindCode = PlayerPrefs.GetInt(PilotBindKey + slotIndex, 0);
        }

        public static bool ResetKeyPressed()
        {
            // Checks the new Input System first, with the old Input Manager fallback for existing editor setups.
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                return UnityEngine.InputSystem.Keyboard.current.pKey.wasPressedThisFrame;
            }

            return Input.GetKeyDown(KeyCode.P);
        }

        public static void ResetAllProgress()
        {
            // Clears every progression key together so the testing reset starts a genuinely new run.
            SuppressSave = true;
            SelectedLevel = 1;
            ShowLevelSelect = false;
            ShowMpLobby = false;
            EnteredFromMenu = true;
            PlayerPrefs.DeleteKey(PilotSavedKey);
            PlayerPrefs.DeleteKey(PilotLevelKey);
            PlayerPrefs.DeleteKey(PilotXpKey);
            PlayerPrefs.DeleteKey(PilotPointsKey);
            PlayerPrefs.DeleteKey(PilotSkillsKey);
            PlayerPrefs.DeleteKey(InvertKey);
            PlayerPrefs.DeleteKey(BestScoreKey);

            // Deletes every pilot-wide value before separately clearing per-sector and per-slot key ranges.
            int progressEntryIndex;

            for (progressEntryIndex = 1; progressEntryIndex <= LevelCount; progressEntryIndex++)
            {
                // Clears level completion and unlock state for every sector in the current campaign.
                PlayerPrefs.DeleteKey(StarsKey + progressEntryIndex);
                PlayerPrefs.DeleteKey(UnlockedKey + progressEntryIndex);
            }

            for (progressEntryIndex = 0; progressEntryIndex < 8; progressEntryIndex++)
            {
                // Clears saved hotbar records, including spare slots from older versions of the project.
                PlayerPrefs.DeleteKey(PilotSlotKey + progressEntryIndex);
                PlayerPrefs.DeleteKey(PilotBindKey + progressEntryIndex);
            }

            PlayerPrefs.DeleteAll();
            Game.Progression.PilotInventory.Reload();
            Game.Campaign.CampaignSession.Arcade();

            // Flushes deletion immediately so closing the game during the menu load cannot restore old progress.
            PlayerPrefs.Save();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(MenuScene);

            // Returns to the menu after resetting cursor and time state so a new run starts cleanly.
        }

        public static void BeginCombat()
        {
            // Resets only score/death information; saved pilot progression intentionally remains untouched.
            RunScore = 0;
            LastKiller = "UNKNOWN";
        }

        public static void NoteKill(int score)
        {
            // Rejects negative score values so a caller cannot accidentally lower the current run score.
            if (score < 0)
            {
                score = 0;
            }

            RunScore += score;
        }

        public static void NoteDeath(string killer)
        {
            // Uses a safe display label when the damage source did not provide a killer name.
            if (killer == null || killer.Length == 0)
            {
                killer = "UNKNOWN";
            }

            LastKiller = killer;
            int best = PlayerPrefs.GetInt(BestScoreKey, 0);
            if (!SuppressSave && RunScore > best)
            {
                // Saves a new best score only when this run exceeds the previous stored record.
                PlayerPrefs.SetInt(BestScoreKey, RunScore);
                PlayerPrefs.Save();
            }
        }

        public static int BestScore()
        {
            // Shows the current run immediately when it is already higher than the saved best score.
            int best = PlayerPrefs.GetInt(BestScoreKey, 0);
            if (RunScore > best)
            {
                return RunScore;
            }

            return best;
        }

        public static string SectorLabel()
        {
            // Adds a leading zero for single-digit sectors to match the sci-fi menu presentation.
            int level = Mathf.Clamp(SelectedLevel, 1, LevelCount);
            if (level < 10)
            {
                return "SECTOR 0" + level;
            }

            return "SECTOR " + level;
        }
    }
}
