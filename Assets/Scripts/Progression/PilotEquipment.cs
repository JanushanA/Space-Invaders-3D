using Game.Combat;
using Game.Player;
using UnityEngine;
namespace Game.Progression
{
    public sealed class PilotEquipment : MonoBehaviour
    {
        Weapon weapon;
        WeaponDefinition original;
        WeaponDefinition instance;
        Health health;
        PlayerController pilot;
        float baseHull;
        float baseSpeed;
        float baseVertical;
        float previousBonus;

        public static float CruiseMultiplier => Game.Net.NetSession.Active ? 1 : 1 + 0.10f * PilotInventory.State.ship + 0.035f * PilotInventory.State.ModuleTier(1);

        public static float VerticalMultiplier => Game.Net.NetSession.Active ? 1 : 1 + 0.08f * PilotInventory.State.ship;

        void Start()
        {
            pilot = GetComponent<PlayerController>();
            health = GetComponent<Health>();
            weapon = GetComponentInChildren<Weapon>();
            if (pilot == null || health == null || weapon == null || weapon.definition == null)
            {
                enabled = false;
                return;
            }

            baseHull = health.maxHealth;
            baseSpeed = pilot.moveSpeed;
            baseVertical = pilot.verticalSpeed;
            original = weapon.definition;
            instance = Instantiate(original);
            weapon.definition = instance;
            PilotInventory.Changed += Apply;
            Apply();
        }

        void Apply()
        {
            // Multiplayer retains the existing server-validated weapon/flight rules until the next solo sortie.
            if (Game.Net.NetSession.Active)
            {
                return;
            }

            var inventory = PilotInventory.State;
            var weaponSpec = SalvageCatalog.Weapons[inventory.equipped];
            instance.damagePerProjectile = weaponSpec.Damage;
            instance.shotsPerSecond = weaponSpec.Rate;
            instance.projectileSpeed = weaponSpec.Speed;
            instance.projectilesPerShot = weaponSpec.Projectiles;
            instance.spreadAngle = weaponSpec.Projectiles > 1 ? 7 : 0;
            float bonus = inventory.ship * 0.75f + inventory.ModuleTier(0) * 0.3f;
            health.incomingDamageMul = 1 - 0.04f * inventory.ModuleTier(2);
            float hullChange = bonus - previousBonus;

            // Equipment changes preserve the current hull percentage instead of healing the ship.
            float hullRatio = health.maxHealth > 0 ? Mathf.Clamp01(health.CurrentHealth / health.maxHealth) : 0;
            health.maxHealth += hullChange;
            previousBonus = bonus;
            if (Mathf.Abs(hullChange) > 0.0001f)
            {
                health.SetNetworkHealth(health.maxHealth * hullRatio);
            }

            var skills = GetComponent<PlayerSkills>();
            if (skills != null)
            {
                skills.RefreshEquipmentBonuses();
            }
            else
            {
                pilot.moveSpeed = baseSpeed * CruiseMultiplier;
                pilot.verticalSpeed = baseVertical * VerticalMultiplier;
            }
        }

        void OnDestroy()
        {
            PilotInventory.Changed -= Apply;
            if (weapon != null && weapon.definition == instance)
            {
                weapon.definition = original;
            }

            if (instance != null)
            {
                Destroy(instance);
            }
        }
    }
}
