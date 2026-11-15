using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEngine;
namespace Game.Net
{
    // Host-side rules for an admitted guest: loadout effects, skill cooldowns, and shot allowance.
    // The guest avatar has no local input, HUD, or save components.
    public sealed class RemotePilotAuthority : MonoBehaviour
    {
        public int Slot
        {
            get; set;
        }

        public bool HasLoadout
        {
            get; private set;
        }

        public float DamageMultiplier { get; private set; } = 1f;

        public float SpeedMultiplier { get; private set; } = 1f;

        // One binary flag per unlocked SkillId; the host checks these before applying abilities.
        private uint unlockedSkills;
        private readonly float[] ready = new float[31];
        private float shieldUntil;
        private float criticalUntil;
        private float seekerUntil;
        private float fireRate = 1f;
        private float tokens;
        private float tokenTime;
        private int extraProjectiles;
        private int seekerShots;
        private Health health;
        private float nextIntercept;
        private readonly System.Collections.Generic.List<RadarContact> supportContacts = new System.Collections.Generic.List<RadarContact>();

        private void Awake()
        {
            health = GetComponent<Health>();
        }

        private void Update()
        {
            if (health != null && health.IsAlive)
            {
                SkillSupportEffects.Intercept(gameObject, unlockedSkills, ref nextIntercept, supportContacts);
            }

            if (shieldUntil > 0f && Time.time >= shieldUntil)
            {
                if (health != null)
                {
                    health.invulnerable = false;
                }

                shieldUntil = 0f;
            }
        }

        public void ApplyLoadout(uint declared)
        {
            if (health == null)
            {
                health = GetComponent<Health>();
            }

            // Unlocks only grow during a match; late older declarations cannot roll back upgrades.
            unlockedSkills = (HasLoadout ? unlockedSkills | declared : declared) & 0x7fffffffu;
            float maximum = 3f + Ranks(SkillId.Health1);
            if (health != null)
            {
                float gain = Mathf.Max(0f, maximum - health.maxHealth);
                health.maxHealth = maximum;
                health.regenPerSecond = Unlocked(SkillId.Health4) ? 0.12f : 0f;
                if (!HasLoadout)
                {
                    health.SetNetworkHealth(maximum);
                }
                else if (gain > 0f && health.IsAlive)
                {
                    health.Heal(gain);
                }
            }

            DamageMultiplier = 1f + 0.25f * Ranks(SkillId.Damage1);
            SpeedMultiplier = 1f;
            fireRate = 1f;
            extraProjectiles = 0;
            if (Unlocked(SkillId.TwinShot))
            {
                extraProjectiles++;
            }

            if (Unlocked(SkillId.QuadShot))
            {
                extraProjectiles += 2;
            }

            if (Unlocked(SkillId.SpreadShot))
            {
                extraProjectiles += 2;
            }

            if (Unlocked(SkillId.RapidFire))
            {
                fireRate *= 1.8f;
            }

            if (Unlocked(SkillId.Overclock))
            {
                fireRate *= 1.6f;
            }

            if (Unlocked(SkillId.HeavyShot))
            {
                DamageMultiplier *= 2f;
                fireRate *= 0.5f;
                SpeedMultiplier *= 0.85f;
            }

            if (Unlocked(SkillId.Cannon))
            {
                DamageMultiplier *= 2f;
                fireRate *= 0.55f;
                SpeedMultiplier *= 0.75f;
            }

            if (!HasLoadout)
            {
                tokens = 2f * (1 + extraProjectiles);
                tokenTime = Time.time;
            }

            HasLoadout = true;
        }

        public bool TryConsumeShot(WeaponDefinition definition)
        {
            if (!HasLoadout || health == null || !health.IsAlive || definition == null)
            {
                return false;
            }

            // Homing Burst has its own short-lived shot allowance, separate from the normal weapon.
            if (seekerShots > 0 && Time.time < seekerUntil)
            {
                seekerShots--;
                return true;
            }

            // Refill a bounded shot allowance over time, allowing normal multi-projectile bursts
            // without accepting an unlimited stream of fire packets from a guest.
            int shots = Mathf.Max(1, definition.projectilesPerShot + extraProjectiles);
            float rate = shots * fireRate / Mathf.Max(0.02f, definition.SecondsBetweenShots);
            if (Time.time < criticalUntil)
            {
                rate += 60f;
            }

            float capacity = Mathf.Max(shots * 2f, Time.time < criticalUntil ? 12f : 0f);
            tokens = Mathf.Min(capacity, tokens + Mathf.Max(0f, Time.time - tokenTime) * rate);
            tokenTime = Time.time;
            if (tokens < 1f)
            {
                return false;
            }

            tokens -= 1f;
            return true;
        }

        public bool TryUse(SkillId skill)
        {
            int index = (int)skill;
            if (!HasLoadout || !IsActiveSkill(skill) || !Unlocked(skill) || health == null || !health.IsAlive || Time.time < ready[index])
            {
                return false;
            }

            // Start the host cooldown before applying the effect, including duplicate client requests.
            ready[index] = Time.time + SkillSupportEffects.Cooldown(unlockedSkills, skill);
            if (skill == SkillId.Repair)
            {
                SkillSupportEffects.Repair(gameObject, health, unlockedSkills);
            }
            else if (skill == SkillId.Shield || skill == SkillId.Barrier)
            {
                shieldUntil = Time.time + (skill == SkillId.Barrier ? 4f : 2.2f);
                health.invulnerable = true;
            }
            else if (skill == SkillId.Bomb || skill == SkillId.PlasmaBomb)
            {
                PlayerSkills.DetonateAt(transform.position, skill == SkillId.PlasmaBomb ? 34f : 22f, gameObject);
            }
            else if (skill == SkillId.Critical)
            {
                criticalUntil = Time.time + 1.2f;
                tokens = Mathf.Max(tokens, 3f);
            }
            else if (skill == SkillId.HomingBurst)
            {
                seekerShots = 6;
                seekerUntil = Time.time + 1f;
            }

            return true;
        }

        public static bool IsActiveSkill(SkillId skill)
        {
            return skill >= SkillId.Critical && skill <= SkillId.HomingBurst;
        }

        private bool Unlocked(SkillId skill)
        {
            return (unlockedSkills & (1u << (int)skill)) != 0;
        }

        private int Ranks(SkillId first)
        {
            int result = 0;

            for (int rankIndex = 0; rankIndex < 4; rankIndex++)
            {
                if (Unlocked((SkillId)((int)first + rankIndex)))
                {
                    result++;
                }
            }

            return result;
        }
    }
}
