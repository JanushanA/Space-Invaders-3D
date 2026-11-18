using System;
using Game.Progression;
using UnityEngine;

// Checks ordered crafting, stock transactions, save migration and weighted salvage rewards.
public static class SystemsEconomyChecks
{
    public static void Run()
    {
        var state = new InventoryState();

        for (int materialIndex = 0; materialIndex < 6; materialIndex++)
        {
            state.materials[materialIndex] = 1000;
        }

        state.credits = 10000;
        var bench = new CraftingBench();
        var recipe = AssemblyRecipes.Find("weapon-3");
        Need(recipe != null, "Ion rail recipe exists");

        // Compare complete inventory snapshots so failed crafting cannot silently spend any resource.
        string before = JsonUtility.ToJson(state);
        Need(!bench.Craft(state, recipe, out _) && JsonUtility.ToJson(state) == before, "empty grid cannot fabricate or charge");
        Fill(bench, recipe, state);
        var first = bench.Cells[0];
        var other = bench.Cells[1];
        bench.Cells[0] = other;
        bench.Cells[1] = first;
        Need(!bench.Craft(state, recipe, out _) && JsonUtility.ToJson(state) == before, "right totals in wrong order must fail atomically");
        Fill(bench, recipe, state);
        Need(bench.Craft(state, recipe, out _) && state.weapons.Contains(3), "exact ordered weapon grid produces weapon");
        string after = JsonUtility.ToJson(state);
        Need(!bench.Craft(state, recipe, out _) && JsonUtility.ToJson(state) == after, "double click cannot spend twice");

        // Exercise each remaining output type after the ordered weapon recipe succeeds.
        foreach (var recipe2 in AssemblyRecipes.All)
        {
            if (recipe2.Id == "weapon-3")
            {
                continue;
            }

            Fill(bench, recipe2, state);
            Need(bench.Craft(state, recipe2, out _), "craft " + recipe2.Id);
        }

        Need(state.ships.Contains(1) && state.ships.Contains(2), "ship grid outputs");

        for (int moduleIndex = 0; moduleIndex < 4; moduleIndex++)
        {
            Need(state.ModuleTier(moduleIndex) == 1, "module installed " + moduleIndex);
        }

        // Clearing a bench only clears reservations; it must never create or refund inventory.
        var reservation = new CraftingBench();
        Need(reservation.Add(0, 0, 5, state), "place material stack");
        int alloy = state.materials[0];
        reservation.Clear();
        Need(state.materials[0] == alloy, "clearing reservations cannot duplicate/refund materials");
        var poor = new InventoryState();
        Need(!reservation.Add(0, 0, 1, poor), "cannot place unowned material");

        // Legacy upgrades must map into the new module tiers and completed-sector flags.
        var legacy = new InventoryState { version = 1, upgrade = 3, completedCampaigns = 3 };
        legacy.Normalize();
        Need(legacy.version == 2 && legacy.ModuleTier(0) == 3 && legacy.ModuleTier(1) == 3 && legacy.completedSectorsMask == 7, "v1 upgrade and route migration");

        // Check progression-based drop weights and the reset of the awarded material's counter.
        var loot = new DynamicLoot(1);
        int[] pity = new int[6];
        int[] baseline = loot.Weights(1, 0, pity);
        pity[3] = 15;
        int[] boosted = loot.Weights(8, 5, pity);
        Need(boosted[3] > baseline[3], "sector, salvage module and pity raise rare material weight");
        int selected = loot.RollMaterial(8, 5, pity, 0);
        Need(pity[selected] == 0, "drop resets selected pity");

        // Stock changes only on successful purchases; rejected purchases must leave the full save unchanged.
        var shop = new TradingStock(state);
        shop.Refresh(100);
        var offers = shop.Query(0, 2000);
        Need(offers.Count >= 10, "AVL exposes stock catalogue");
        int identifier = offers[0].Id;
        int previous = state.tradeStock[identifier];
        Need(shop.Buy(identifier, out _), "affordable trade");
        Need(state.tradeStock[identifier] == previous - 1, "stock decremented once");
        state.credits = 0;
        before = JsonUtility.ToJson(state);
        Need(!shop.Buy(identifier, out _) && JsonUtility.ToJson(state) == before, "failed trade atomic");
        shop.Refresh(101);
        Need(state.tradeEpoch == 101, "changing stock refresh persisted");
        Debug.Log("SYSTEMS_ECONOMY_PASS: wrong-order/exact/duplicate crafting, ships/modules, reservations, migration, weighted pity and AVL stock transactions");
    }

    static void Fill(CraftingBench bench, AssemblyRecipe recipe, InventoryState state)
    {
        bench.Clear();

        foreach (var slot in recipe.Pattern(state))
        {
            Need(slot.Count == 0 || bench.Add(slot.Position, slot.Material, slot.Count, state), "fill recipe slot");
        }
    }

    static void Need(bool isValid, string message)
    {
        if (!isValid)
        {
            throw new Exception("Economy systems: " + message);
        }
    }
}
