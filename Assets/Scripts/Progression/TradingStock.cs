using System;
using System.Collections.Generic;
namespace Game.Progression
{
    public sealed class TradingStock
    {
        readonly InventoryState state;
        readonly PriceCatalogue catalogue = new PriceCatalogue();
        public static readonly int[] Prices = { 25, 40, 30, 100, 75, 65, 300, 600, 900, 1200, 400, 425, 500, 450 };

        public TradingStock(InventoryState inventory)
        {
            state = inventory;

            for (int stockIndex = 0; stockIndex < Prices.Length; stockIndex++)
            {
                catalogue.Insert(new PriceEntry(stockIndex, Prices[stockIndex]));
            }
        }

        public static long CurrentEpoch => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300;

        public bool Refresh(long epoch)
        {
            if (epoch <= state.tradeEpoch)
            {
                return false;
            }

            // Sector and five-minute epoch determine stock, keeping refreshes repeatable.
            var random = new Random(unchecked((int)(epoch ^ (epoch >> 32)) + state.currentSector * 7919));

            for (int stockIndex = 0; stockIndex < Prices.Length; stockIndex++)
            {
                state.tradeStock[stockIndex] = stockIndex < 6 ? random.Next(3, 9) : random.Next(1, 4);
            }

            state.tradeEpoch = epoch;
            return true;
        }

        public List<PriceEntry> Query(int minimum, int maximum) => catalogue.Range(minimum, maximum);

        public static string Name(int itemId) => itemId < 6 ? SalvageCatalog.Materials[itemId] + " ×5" : itemId < 10 ? SalvageCatalog.Weapons[itemId - 5].Name : AssemblyRecipes.ModuleNames[itemId - 10];

        public bool Buy(int itemId, out string message)
        {
            message = "Unavailable stock or insufficient credits.";
            if (itemId < 0 || itemId >= Prices.Length || state.tradeStock[itemId] < 1 || state.credits < Prices[itemId])
            {
                return false;
            }

            if (itemId >= 6 && itemId < 10 && state.weapons.Contains(itemId - 5))
            {
                message = "Weapon already owned.";
                return false;
            }

            if (itemId >= 10 && state.ModuleTier(itemId - 10) >= 5)
            {
                message = "Module is already at maximum tier.";
                return false;
            }

            if (itemId < 6 && state.materials[itemId] > 999994)
            {
                message = "Material storage full.";
                return false;
            }

            // Spend credits only after every ownership and storage check has passed.
            state.credits -= Prices[itemId];
            state.tradeStock[itemId]--;
            if (itemId < 6)
            {
                state.materials[itemId] += 5;
            }
            else if (itemId < 10)
            {
                state.weapons.Add(itemId - 5);
            }
            else
            {
                AssemblyRecipes.Find("module-" + (itemId - 10)).Receive(state);
            }

            message = "Purchased " + Name(itemId);
            return true;
        }

        public bool Sell(int itemId, out string message)
        {
            message = "No eligible item to sell.";
            if (itemId < 0 || itemId >= 10 || state.tradeStock[itemId] >= 99)
            {
                return false;
            }

            if (state.credits > 9999999 - Prices[itemId] / 2)
            {
                message = "Credit storage full.";
                return false;
            }

            if (itemId < 6)
            {
                if (state.materials[itemId] < 5)
                {
                    return false;
                }

                state.materials[itemId] -= 5;
            }
            else
            {
                int weapon = itemId - 5;
                if (!state.weapons.Remove(weapon))
                {
                    return false;
                }

                if (state.equipped == weapon)
                {
                    state.equipped = 0;
                }
            }

            state.credits = Math.Min(9999999, state.credits + Prices[itemId] / 2);
            state.tradeStock[itemId]++;
            message = "Sold " + Name(itemId);
            return true;
        }
    }

    public sealed class DynamicLoot
    {
        readonly FenwickWeights distribution = new FenwickWeights(6);
        readonly Random random;

        public DynamicLoot(int seed)
        {
            random = new Random(seed);
        }

        public int[] Weights(int sector, int salvageTier, int[] pity)
        {
            int[] weights = { 36, 24, 28, 6, 12, 16 };
            sector = Math.Max(1, Math.Min(8, sector));
            salvageTier = Math.Max(0, Math.Min(5, salvageTier));

            for (int materialIndex = 0; materialIndex < 6; materialIndex++)
            {
                int missedDrops = pity != null && materialIndex < pity.Length ? Math.Max(0, Math.Min(30, pity[materialIndex])) : 0;

                // Materials not recently awarded gradually become more likely to drop.
                weights[materialIndex] += missedDrops * 3;
                if (materialIndex >= 3)
                {
                    weights[materialIndex] += (sector - 1) * 2 + salvageTier * 4;
                }

                if (materialIndex == (sector - 1) % 6)
                {
                    weights[materialIndex] += 12;
                }
            }

            return weights;
        }

        // Explicit ticket supports deterministic boundary tests; normal play draws uniformly.
        public int RollMaterial(int sector, int salvageTier, int[] pity, int ticket = -1)
        {
            if (pity == null || pity.Length != 6)
            {
                throw new ArgumentException("Six material pity counters required");
            }

            var weights = Weights(sector, salvageTier, pity);

            for (int materialIndex = 0; materialIndex < 6; materialIndex++)
            {
                distribution.Set(materialIndex, weights[materialIndex]);
            }

            int selectedMaterial = distribution.Sample(ticket < 0 ? random.Next(distribution.Total) : ticket);

            // Only the selected material resets its drought counter.
            for (int materialIndex = 0; materialIndex < 6; materialIndex++)
            {
                pity[materialIndex] = materialIndex == selectedMaterial ? 0 : Math.Min(30, pity[materialIndex] + 1);
            }

            return selectedMaterial;
        }
    }
}
