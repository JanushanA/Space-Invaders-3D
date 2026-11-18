using System;
using UnityEngine;
using Game.Progression;
namespace Game.EditorTools
{
    // Checks crafting and save migration alongside deterministic, reachable campaign sectors.
    public static class ExpansionChecks
    {
        public static void Run()
        {
            Type inventory = typeof(Game.Core.GameSession).Assembly.GetType("Game.Progression.InventoryState");
            Require(inventory != null, "Persistent salvage inventory must exist");
            var fresh = new Game.Progression.InventoryState();
            string before = JsonUtility.ToJson(fresh);
            var bench = new CraftingBench();
            var rail = AssemblyRecipes.Find("weapon-3");
            Require(!bench.Craft(fresh, rail, out _) && JsonUtility.ToJson(fresh) == before, "Insufficient resources must not mutate inventory");

            // Fund the same fixture, then verify successful crafting and rejection of duplicate spending.
            for (int materialIndex = 0; materialIndex < 6; materialIndex++)
            {
                fresh.materials[materialIndex] = 100;
            }

            fresh.credits = 3000;
            Fill(bench, rail, fresh);
            Require(bench.Craft(fresh, rail, out _) && fresh.weapons.Contains(3), "Craft paid weapon");
            before = JsonUtility.ToJson(fresh);
            Require(!bench.Craft(fresh, rail, out _) && JsonUtility.ToJson(fresh) == before, "Duplicate fabrication cannot charge twice");
            var ship = AssemblyRecipes.Find("ship-1");
            Fill(bench, ship, fresh);
            Require(bench.Craft(fresh, ship, out _), "Ordered ship assembly");
            var hull = AssemblyRecipes.Find("module-0");
            Fill(bench, hull, fresh);
            Require(bench.Craft(fresh, hull, out _), "Ordered module upgrade");

            // Round-trip equipped rewards through JSON before checking normalization of malformed values.
            fresh.Grant(3, 2, 5, 250);
            fresh.equipped = 5;
            var loaded = JsonUtility.FromJson<Game.Progression.InventoryState>(JsonUtility.ToJson(fresh));
            loaded.Normalize();
            Require(loaded.weapons.Contains(5) && loaded.equipped == 5 && loaded.ship == 1 && loaded.upgrade == 1, "Round-trip equipment/legendary persistence");
            loaded.materials[0] = -50;
            loaded.equipped = 999;
            loaded.Normalize();
            Require(loaded.materials[0] == 0 && loaded.equipped == 0, "Malformed values normalized");

            // Exercise the generator's seed validation after the inventory checks.
            Type missions = typeof(Game.Core.GameSession).Assembly.GetType("Game.Campaign.SectorGenerator");
            Require(missions != null, "Validated procedural sectors must exist");
            missions.GetMethod("VerifySeeds").Invoke(null, null);
            Debug.Log("[ExpansionChecks] PASS economy transactions and procedural reachability");
        }

        static void Fill(CraftingBench bench, AssemblyRecipe recipe, InventoryState state)
        {
            bench.Clear();

            foreach (var part in recipe.Pattern(state))
            {
                if (part.Count > 0)
                {
                    Require(bench.Add(part.Position, part.Material, part.Count, state), "Recipe reservation");
                }
            }
        }

        public static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("[ExpansionChecks] " + message);
            }
        }
    }
}
