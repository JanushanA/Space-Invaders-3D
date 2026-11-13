using System;
using Game.Core;
using UnityEngine;
namespace Game.Player
{
    // XP and skill points. Skill tree comes later.
    // Converts enemy kills into experience levels and skill points, then saves the result.
    public class PlayerProgression : MonoBehaviour
    {
        public int xpPerEnemy = 20;
        public int xpForFirstLevel = 40;
        public int extraXpPerLevel = 20;
        private int level = 1;
        private int skillPoints;
        private int xp;
        private int xpToNext;
        public event Action Changed;

        public int Level
        {
            get
            {
                return level;
            }
        }

        public int SkillPoints
        {
            get
            {
                return skillPoints;
            }
        }

        public int Xp
        {
            get
            {
                return xp;
            }
        }

        public int XpToNext
        {
            get
            {
                return xpToNext;
            }
        }

        private void Awake()
        {
            // Creates a safe first level requirement before optionally replacing it with saved progress.
            xpToNext = Mathf.Max(1, xpForFirstLevel);
            LoadSaved();
        }

        private void OnDestroy()
        {
            SaveNow();
        }

        private void LoadSaved()
        {
            // Leaves default values in place for a new player with no saved pilot progression.
            if (!GameSession.HasPilotSave())
            {
                return;
            }

            GameSession.LoadPilotStats(out level, out xp, out skillPoints);

            // Recalculates the next threshold because it is derived from the loaded level, not stored separately.
            xpToNext = XpNeededFor(level);
        }

        private int XpNeededFor(int atLevel)
        {
            // Increases the requirement by a fixed amount for each completed level.
            // Mathf.Max protects against accidental inspector values that would make a zero-XP level.
            return Mathf.Max(1, xpForFirstLevel + (atLevel - 1) * extraXpPerLevel);
        }

        private void SaveNow()
        {
            // Stores all three progression values together so level, XP and points stay in sync.
            GameSession.SavePilotStats(level, xp, skillPoints);
        }

        public void AddKillXp()
        {
            // Adds the configured enemy reward to both run score tracking and permanent pilot XP.
            GameSession.NoteKill(xpPerEnemy);
            AddXp(xpPerEnemy);
        }

        public void AddSkillPoints(int amount)
        {
            // Refuses non-positive rewards so external callers cannot remove points through this helper.
            if (amount <= 0)
            {
                return;
            }

            skillPoints += amount;
            if (Changed != null)
            {
                // Tells the HUD and skill tree to refresh immediately after the point total changes.
                Changed();
            }

            SaveNow();
        }

        public bool SpendPoints(int amount)
        {
            // Checks affordability before subtracting, allowing the skill tree to reject invalid purchases safely.
            if (amount <= 0)
            {
                return true;
            }

            if (skillPoints < amount)
            {
                return false;
            }

            skillPoints -= amount;
            if (Changed != null)
            {
                Changed();
            }

            SaveNow();
            return true;
        }

        public void AddXp(int amount)
        {
            // Adds the reward first, then carries excess XP through one or more level thresholds.
            if (amount <= 0)
            {
                return;
            }

            xp += amount;

            while (xp >= xpToNext)
            {
                // Keeps leftover XP after levelling so a large reward can level the player more than once.
                xp -= xpToNext;
                level += 1;
                skillPoints += 1;
                xpToNext = XpNeededFor(level);

                // Uses the new level to calculate the harder requirement for the following level.
            }

            if (Changed != null)
            {
                Changed();
            }

            SaveNow();
        }
    }
}
