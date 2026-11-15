using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Progression
{
    public enum Salvage
    {
        Alloy, Circuits, Energy, Plasma, Shield, Thruster
    }

    public enum Rarity
    {
        Common, Rare, Epic, Legendary
    }

    public sealed class WeaponSpec
    {
        public readonly string Name;
        public readonly Rarity Rarity;
        public readonly float Damage;
        public readonly float Rate;
        public readonly float Speed;
        public readonly int Projectiles;

        public WeaponSpec(string name, Rarity rarity, float damage, float rate, float speed, int count = 1)
        {
            Name = name;
            Rarity = rarity;
            Damage = damage;
            Rate = rate;
            Speed = speed;
            Projectiles = count;
        }
    }

    public static class SalvageCatalog
    {
        public static readonly string[] Materials = { "Alloy scrap", "Circuit boards", "Energy cells", "Plasma cores", "Shield emitters", "Thruster coils" };
        public static readonly string[] Ships = { "Pathfinder", "Vanguard", "Aegis" };
        public static readonly WeaponSpec[] Weapons = {
            new WeaponSpec("Pulse cannon", Rarity.Common, 1, 6, 45),
            new WeaponSpec("Arc repeater", Rarity.Rare, 1.4f, 7, 58),
            new WeaponSpec("Scatter lance", Rarity.Rare, 0.9f, 3, 52, 3),
            new WeaponSpec("Ion rail", Rarity.Epic, 3.8f, 2.4f, 95),
            new WeaponSpec("Nova driver", Rarity.Epic, 1.7f, 3.6f, 62, 3),
            new WeaponSpec("Sovereign's Light", Rarity.Legendary, 2.6f, 4, 85, 3)
        };

        // Fixed recipes: alloy, circuits, energy, plasma, shield, thruster.
        public static readonly int[][] Recipes = {
            new[]{0,0,0,0,0,0}, new[]{12,4,5,0,0,0}, new[]{16,3,3,0,0,2},
            new[]{28,10,8,3,0,2}, new[]{32,12,10,5,3,0}
        };

        public static int[] ShipCost(int ship) => ship == 1 ? new[] { 35, 10, 8, 0, 5, 8 } : new[] { 65, 18, 15, 6, 15, 12 };
    }

    [Serializable]
    public sealed class InventoryState
    {
        public int version = 2;
        public int credits = 100;
        public int equipped;
        public int ship;
        public int upgrade;
        public int[] modules = new int[4];
        public int[] dropPity = new int[6];
        public int sectorSeed;
        public int currentSector = 1;
        public int completedSectorsMask;
        public int[] tradeStock = new int[14];
        public long tradeEpoch = -1;
        public int[] materials = new int[6];
        public List<int> weapons = new List<int> { 0 };
        public List<int> ships = new List<int> { 0 };
        public int completedCampaigns;

        public void Normalize()
        {
            bool isLegacySave = version < 2;
            if (modules == null || modules.Length != 4)
            {
                modules = new int[4];
            }

            // Older saves used one upgrade level; carry it into the hull and drive modules.
            if (isLegacySave)
            {
                modules[0] = Mathf.Clamp(upgrade, 0, 5);
                modules[1] = modules[0];
                completedSectorsMask = (1 << Mathf.Clamp(completedCampaigns, 0, 8)) - 1;
            }

            for (int index = 0; index < modules.Length; index++)
            {
                modules[index] = Mathf.Clamp(modules[index], 0, 5);
            }

            if (dropPity == null || dropPity.Length != 6)
            {
                dropPity = new int[6];
            }

            for (int index = 0; index < 6; index++)
            {
                dropPity[index] = Mathf.Clamp(dropPity[index], 0, 30);
            }

            if (tradeStock == null || tradeStock.Length != 14)
            {
                tradeStock = new int[14];
                tradeEpoch = -1;
            }

            for (int index = 0; index < tradeStock.Length; index++)
            {
                tradeStock[index] = Mathf.Clamp(tradeStock[index], 0, 99);
            }

            currentSector = Mathf.Clamp(currentSector, 1, 8);
            completedSectorsMask &= 255;
            version = 2;
            if (materials == null || materials.Length != 6)
            {
                materials = new int[6];
            }

            for (int index = 0; index < 6; index++)
            {
                materials[index] = Mathf.Clamp(materials[index], 0, 999999);
            }

            credits = Mathf.Clamp(credits, 0, 9999999);
            upgrade = Mathf.Clamp(upgrade, 0, 5);

            // Always retain starter equipment and discard invalid or duplicate owned IDs.
            weapons = Clean(weapons, SalvageCatalog.Weapons.Length);
            ships = Clean(ships, 3);
            if (!weapons.Contains(equipped))
            {
                equipped = 0;
            }

            if (!ships.Contains(ship))
            {
                ship = 0;
            }

            completedCampaigns = Mathf.Clamp(completedCampaigns, 0, 8);
        }

        public int ModuleTier(int moduleId) => moduleId < 0 || moduleId >= 4 ? 0 : Mathf.Clamp(modules[moduleId], 0, 5);

        static List<int> Clean(List<int> ownedItems, int count)
        {
            var validItems = new List<int> { 0 };
            if (ownedItems != null)
            {
                foreach (int itemId in ownedItems)
                {
                    if (itemId > 0 && itemId < count && !validItems.Contains(itemId))
                    {
                        validItems.Add(itemId);
                    }
                }
            }

            return validItems;
        }

        public bool CanPay(int[] cost, int price)
        {
            if (price < 0 || credits < price || cost == null || cost.Length != 6)
            {
                return false;
            }

            for (int index = 0; index < 6; index++)
            {
                if (cost[index] < 0 || materials[index] < cost[index])
                {
                    return false;
                }
            }

            return true;
        }

        public void Grant(int material, int count, int weapon, int money)
        {
            if (material >= 0 && material < 6)
            {
                materials[material] = Mathf.Clamp(materials[material] + Mathf.Clamp(count, 0, 1000), 0, 999999);
            }

            credits = Mathf.Clamp(credits + Mathf.Clamp(money, 0, 10000), 0, 9999999);
            if (weapon > 0 && weapon < SalvageCatalog.Weapons.Length)
            {
                if (!weapons.Contains(weapon))
                {
                    weapons.Add(weapon);
                }
                else
                {
                    credits = Mathf.Min(9999999, credits + 50 * (weapon + 1));
                }
            }
        }
    }
}
