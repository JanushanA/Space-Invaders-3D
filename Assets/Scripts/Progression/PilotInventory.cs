using System;
using Game.Core;
using UnityEngine;
namespace Game.Progression
{
    public static class PilotInventory
    {
        const string Key = "SpaceInvaders.Salvage.v1";
        static InventoryState state;
        static bool suppressed;
        public static event Action Changed;

        public static string LastPickup { get; private set; } = "";

        public static float PickupUntil
        {
            get; private set;
        }

        public static InventoryState State
        {
            get
            {
                if (state == null || suppressed != GameSession.SuppressSave)
                {
                    suppressed = GameSession.SuppressSave;
                    state = suppressed ? new InventoryState() : Read();
                }

                return state;
            }
        }

        public static string EquippedWeaponName => SalvageCatalog.Weapons[State.equipped].Name;

        static InventoryState Read()
        {
            foreach (string key in new[] { Key, Key + ".backup" })
            {
                if (!PlayerPrefs.HasKey(key))
                {
                    continue;
                }

                try
                {
                    var result = JsonUtility.FromJson<InventoryState>(PlayerPrefs.GetString(key));
                    if (result != null && (result.version == 1 || result.version == 2))
                    {
                        result.Normalize();
                        return result;
                    }
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning("Salvage save could not be read; trying backup.");
                }
            }

            return new InventoryState();
        }

        public static void Save()
        {
            if (!GameSession.SuppressSave)
            {
                string json = JsonUtility.ToJson(State);

                // Keep the previous save available in case the new value cannot be read later.
                if (PlayerPrefs.HasKey(Key))
                {
                    PlayerPrefs.SetString(Key + ".backup", PlayerPrefs.GetString(Key));
                }

                PlayerPrefs.SetString(Key, json);
                PlayerPrefs.Save();
            }

            Changed?.Invoke();
        }

        public static bool Equip(int weaponId)
        {
            if (!State.weapons.Contains(weaponId))
            {
                return false;
            }

            State.equipped = weaponId;
            Save();
            return true;
        }

        public static bool SelectShip(int shipId)
        {
            if (!State.ships.Contains(shipId))
            {
                return false;
            }

            State.ship = shipId;
            Save();
            return true;
        }

        public static void Grant(int material, int count, int weapon, int money)
        {
            State.Grant(material, count, weapon, money);
            LastPickup = weapon > 0 ? SalvageCatalog.Weapons[weapon].Rarity + " / " + SalvageCatalog.Weapons[weapon].Name : (material >= 0 ? "+" + count + " " + SalvageCatalog.Materials[material] : "Salvage secured");
            if (money > 0)
            {
                LastPickup += "  +" + money + " credits";
            }

            PickupUntil = Time.unscaledTime + 5;
            Save();
        }

        public static void Reload()
        {
            state = null;
        }
    }
}
