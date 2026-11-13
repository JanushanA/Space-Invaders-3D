using System.Collections.Generic;
using Game.Core;
using UnityEngine;
namespace Game.Player
{
    // Owns the skill tree rules, including prerequisites, path buying and saved unlocks.
    public class SkillTree : MonoBehaviour
    {
        private readonly Dictionary<SkillId, SkillNode> nodes = new Dictionary<SkillId, SkillNode>();
        private readonly HashSet<SkillId> unlocked = new HashSet<SkillId>();
        private PlayerProgression progression;
        public event System.Action Changed;

        public IEnumerable<SkillNode> AllNodes
        {
            get
            {
                return nodes.Values;
            }
        }

        private void Awake()
        {
            // Builds static node definitions before loading saved IDs so every saved unlock can be validated.
            // Root and Critical are always added because they are the player's starting skills.
            progression = GetComponent<PlayerProgression>();
            BuildNodes();
            unlocked.Add(SkillId.Root);
            unlocked.Add(SkillId.Critical);
            LoadSaved();
        }

        private void OnDestroy()
        {
            SaveNow();
        }

        public bool IsUnlocked(SkillId skillId)
        {
            // Uses a HashSet because unlock checks happen often and should be fast direct lookups.
            return unlocked.Contains(skillId);
        }

        public bool CanUnlock(SkillId skillId)
        {
            // Checks the node exists, its parent is unlocked and the player can afford its cost.
            if (skillId == SkillId.None || unlocked.Contains(skillId))
            {
                return false;
            }

            if (!nodes.ContainsKey(skillId))
            {
                return false;
            }

            SkillNode node = nodes[skillId];

            // Reads the node once so its parent and cost checks use the same definition.
            if (node.parent != SkillId.None && !unlocked.Contains(node.parent))
            {
                // Blocks a node until its direct prerequisite creates a valid tree path to it.
                return false;
            }

            if (progression == null)
            {
                return false;
            }

            return progression.SkillPoints >= node.cost;
        }

        public bool TryUnlock(SkillId skillId)
        {
            // Makes a normal one-node purchase and notifies the HUD/menu after the saved state changes.
            if (!CanUnlock(skillId))
            {
                return false;
            }

            SkillNode node = nodes[skillId];
            if (!progression.SpendPoints(node.cost))
            {
                return false;
            }

            unlocked.Add(skillId);
            NotifyChanged();
            SaveNow();
            return true;
        }

        public int CostToUnlockPath(SkillId skillId)
        {
            // Walks back through parents and totals only the missing nodes needed to reach this one.
            int cost = 0;
            SkillId walk = skillId;
            int guard = 0;

            while (walk != SkillId.None && guard < 40)
            {
                // Follows parent links toward the root; the guard prevents a bad data loop freezing the game.
                guard += 1;
                if (!nodes.ContainsKey(walk))
                {
                    break;
                }

                if (!unlocked.Contains(walk))
                {
                    // Counts only missing nodes because already owned prerequisites cost nothing again.
                    cost += nodes[walk].cost;
                }

                walk = nodes[walk].parent;
            }

            return cost;
        }

        public bool TryUnlockPath(SkillId skillId)
        {
            // Builds the route root-to-target, then buys in that order so prerequisites are always valid.
            if (skillId == SkillId.None || !nodes.ContainsKey(skillId))
            {
                return false;
            }

            List<SkillId> order = new List<SkillId>();
            SkillId walk = skillId;
            int guard = 0;

            while (walk != SkillId.None && guard < 40)
            {
                guard += 1;
                if (!nodes.ContainsKey(walk))
                {
                    break;
                }

                order.Insert(0, walk);

                // Inserts at index zero so the final list runs from the root outward to the clicked node.
                walk = nodes[walk].parent;
            }

            int cost = 0;

            // Checks the full price first, avoiding a partial path unlock when points are insufficient.
            for (int orderedItemIndex = 0; orderedItemIndex < order.Count; orderedItemIndex++)
            {
                if (!unlocked.Contains(order[orderedItemIndex]))
                {
                    cost += nodes[order[orderedItemIndex]].cost;
                }
            }

            if (progression == null || progression.SkillPoints < cost)
            {
                return false;
            }

            bool unlockedAny = false;

            for (int orderedItemIndex2 = 0; orderedItemIndex2 < order.Count; orderedItemIndex2++)
            {
                // Purchases in root-to-target order, making every parent valid before its child is bought.
                if (unlocked.Contains(order[orderedItemIndex2]))
                {
                    continue;
                }

                if (!progression.SpendPoints(nodes[order[orderedItemIndex2]].cost))
                {
                    break;
                }

                unlocked.Add(order[orderedItemIndex2]);
                unlockedAny = true;
            }

            if (unlockedAny)
            {
                NotifyChanged();
                SaveNow();
            }

            return unlockedAny;
        }

        public SkillNode GetNode(SkillId skillId)
        {
            if (nodes.ContainsKey(skillId))
            {
                return nodes[skillId];
            }

            SkillNode empty = new SkillNode();

            // Returns a harmless empty node instead of throwing an error when UI asks for an unknown ID.
            empty.id = SkillId.None;
            empty.title = "";
            empty.detail = "";
            empty.colour = Color.gray;
            empty.mark = '-';
            return empty;
        }

        public bool IsActiveSkill(SkillId skillId)
        {
            if (!nodes.ContainsKey(skillId))
            {
                return false;
            }

            return nodes[skillId].kind == SkillKind.Active;
        }

        private void NotifyChanged()
        {
            if (Changed != null)
            {
                Changed();
            }
        }

        private void LoadSaved()
        {
            // Uses the stored comma-separated IDs only after confirming a pilot save exists.
            if (!GameSession.HasPilotSave())
            {
                return;
            }

            string savedSkillIds = GameSession.LoadSkillList();
            if (savedSkillIds == null || savedSkillIds.Length == 0)
            {
                return;
            }

            string[] parts = savedSkillIds.Split(',');

            // Parses each saved number separately so one corrupt entry does not discard the rest of the skill tree.
            for (int savedSkillIndex = 0; savedSkillIndex < parts.Length; savedSkillIndex++)
            {
                int parsed;
                if (!int.TryParse(parts[savedSkillIndex], out parsed))
                {
                    continue;
                }

                SkillId skillId = (SkillId)parsed;
                if (skillId > SkillId.None && skillId <= SkillId.HomingBurst)
                {
                    // Preserve removed passive unlocks and their existing stat effects.
                    unlocked.Add(skillId);
                }
            }
        }

        private void SaveNow()
        {
            // Converts the HashSet into compact comma-separated IDs because PlayerPrefs stores strings simply.
            string savedSkillIds = "";

            foreach (SkillId skillId in unlocked)
            {
                if (savedSkillIds.Length > 0)
                {
                    savedSkillIds += ",";
                }

                savedSkillIds += ((int)skillId).ToString();
            }

            GameSession.SaveSkillList(savedSkillIds);
        }

        // Stable IDs preserve save/network meanings, including removed legacy passive bonuses.
        public uint NetworkMask
        {
            get
            {
                uint unlockedSkills = 0;

                foreach (SkillId skillId in unlocked)
                {
                    if ((int)skillId > 0 && (int)skillId < 31)
                    {
                        unlockedSkills |= 1u << (int)skillId;
                    }
                }

                return unlockedSkills;
            }
        }

        public static int Branch(SkillId skillId)
        {
            switch (skillId)
            {
                case SkillId.Critical:
                case SkillId.TwinShot:
                case SkillId.QuadShot:
                case SkillId.Bomb:
                case SkillId.PlasmaBomb:
                    return 0;
                case SkillId.Health1:
                case SkillId.Health2:
                case SkillId.Health3:
                case SkillId.Shield:
                case SkillId.Barrier:
                    return 1;
                case SkillId.Speed1:
                case SkillId.Speed2:
                case SkillId.Speed3:
                case SkillId.Dash:
                case SkillId.Afterburner:
                    return 2;
                case SkillId.Repair:
                case SkillId.Health4:
                case SkillId.Damage1:
                case SkillId.Damage2:
                case SkillId.Damage3:
                    return 3;
                case SkillId.AttackCore:
                case SkillId.Speed4:
                case SkillId.HomingBurst:
                case SkillId.RapidFire:
                case SkillId.Overclock:
                    return 4;
                default:
                    return 4;
            }
        }

        private void BuildNodes()
        {
            nodes.Clear();
            Add(SkillId.Root, SkillId.None, "Pilot", "Starting node", 0, SkillKind.Stat, UiChrome.Utility, 'P', 0, 0);
            BranchNodes(new[] { SkillId.Critical, SkillId.TwinShot, SkillId.QuadShot, SkillId.Bomb, SkillId.PlasmaBomb },
                new[] { "Critical", "Twin Shot", "Quad Shot", "Bomb", "Plasma Bomb" },
                new[] { "Spray burst. Starts unlocked.", "One extra projectile; 6 degrees spread.", "Two extra projectiles; 8 degrees spread.", "3 damage to visible hostiles within 22 m.", "3 damage to visible hostiles within 34 m." }, UiChrome.Gold);
            BranchNodes(new[] { SkillId.Health1, SkillId.Health2, SkillId.Health3, SkillId.Shield, SkillId.Barrier },
                new[] { "Hull I", "Hull II", "Hull III", "Shield", "Barrier" },
                new[] { "+1 maximum hull.", "+1 maximum hull.", "+1 maximum hull.", "2.2 seconds of invulnerability.", "4 seconds of invulnerability; longer cooldown." }, UiChrome.NeonBlue);
            BranchNodes(new[] { SkillId.Speed1, SkillId.Speed2, SkillId.Speed3, SkillId.Dash, SkillId.Afterburner },
                new[] { "Thruster I", "Thruster II", "Thruster III", "Dash", "Afterburner" },
                new[] { "+2 cruise; +1.2 vertical speed.", "+2 cruise; +1.2 vertical speed.", "+2 cruise; +1.2 vertical speed.", "Collision-safe forward dash.", "70% speed boost for 3 seconds." }, UiChrome.Mobility);
            BranchNodes(new[] { SkillId.Repair, SkillId.Health4, SkillId.Damage1, SkillId.Damage2, SkillId.Damage3 },
                new[] { "Repair", "Field Repair", "Repair Kit", "Wing Repair", "Recovery" },
                new[] { "Restore 1 hull (2 with Repair Kit).", "Regenerate 0.12 hull/sec after 3 seconds without damage; +1 max hull.", "Repair restores +1 hull; +25% shot damage.", "Repair restores 1 allied hull within 40 m, including escort; +25% damage.", "Active skills recover 15% faster; +25% shot damage." }, UiChrome.Support);
            BranchNodes(new[] { SkillId.AttackCore, SkillId.Speed4, SkillId.HomingBurst, SkillId.RapidFire, SkillId.Overclock },
                new[] { "Sensors", "Tractor", "Seekers", "Point Defence", "Jammer" },
                new[] { "Radar range 150 to 240 m; targeting range 100 to 150 m.", "Draw salvage from 32 m; +2 cruise and +1.2 vertical speed.", "Target up to 6 visible hostiles within 70 m.", "Intercept one hostile missile within 16 m every 2 seconds; +80% fire rate.", "Intercept up to 3 missiles within 26 m every 2 seconds; +60% fire rate." }, UiChrome.Utility);
        }

        void BranchNodes(SkillId[] branchSkillIds, string[] titles, string[] descriptions, Color color)
        {
            for (int nodeIndex = 0; nodeIndex < 5; nodeIndex++)
            {
                SkillId skillId = branchSkillIds[nodeIndex];
                SkillId parent = nodeIndex == 0 ? SkillId.Root : branchSkillIds[nodeIndex >= 3 ? 2 : nodeIndex - 1];
                SkillKind kind = skillId >= SkillId.Critical && skillId <= SkillId.HomingBurst ? SkillKind.Active : SkillKind.Stat;
                Add(skillId, parent, titles[nodeIndex], descriptions[nodeIndex], skillId == SkillId.Critical ? 0 : nodeIndex + 1, kind, color, '+', 0, nodeIndex);
            }
        }

        private void Add(
            SkillId skillId,
            SkillId parent,
            string title,
            string detail,
            int cost,
            SkillKind kind,
            Color colour,
            char mark,
            int hexColumn,
            int hexRow)
        {
            // Uses the short overload for normal nodes that have only one required parent link.
            Add(skillId, parent, title, detail, cost, kind, colour, mark, hexColumn, hexRow, SkillId.None);
        }

        private void Add(
            SkillId skillId2,
            SkillId parent,
            string title,
            string detail,
            int cost,
            SkillKind kind,
            Color colour,
            char mark,
            int hexColumn2,
            int hexRow2,
            SkillId extraLink)
        {
            Vector2 position = HexToPixel(hexColumn2, hexRow2);

            // Converts the logical hex-grid coordinate into the menu position used by the UI.
            SkillNode node = new SkillNode();
            node.id = skillId2;
            node.parent = parent;
            node.extraLink = extraLink;
            node.title = title;
            node.detail = detail;
            node.cost = cost;
            node.kind = kind;
            node.colour = colour;
            node.mark = mark;
            node.x = position.x;
            node.y = position.y;
            nodes[skillId2] = node;

            // Stores each definition by ID so gameplay and UI can retrieve it without scanning a list.
        }

        private static Vector2 HexToPixel(int hexColumn, int hexRow)
        {
            // Converts axial hex coordinates using each hexagon's width and vertical row spacing.
            float size = 50f;
            float pixelX = size * 1.7320508f * (hexColumn + hexRow * 0.5f);
            float pixelY = size * 1.5f * hexRow - 240f;

            // Uses pointy-hex width and row spacing to place neighbouring nodes without overlap.
            return new Vector2(pixelX, pixelY);
        }

        private static Color Bar(float redChannel, float green, float blue)
        {
            return new Color(redChannel, green, blue, 1f);
        }
    }
}
