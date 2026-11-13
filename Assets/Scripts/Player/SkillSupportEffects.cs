using System.Collections.Generic;
using Game.Combat;
using UnityEngine;

// Applies repair and missile-defence abilities for both local players and host-controlled remote pilots.
namespace Game.Player
{
    // Shared host-authoritative support effects for local and remote pilots.
    public static class SkillSupportEffects
    {
        // Each skill owns one binary flag. Checking that flag avoids searching a list for every effect.
        public static bool Has(uint unlockedSkills, SkillId skillId) => (int)skillId > 0 && (int)skillId < 31 && (unlockedSkills & (1u << (int)skillId)) != 0;

        public static float Cooldown(uint unlockedSkills, SkillId skillId) => PlayerSkills.CooldownFor(skillId) * (Has(unlockedSkills, SkillId.Damage3) ? 0.85f : 1f);

        public static float SensorRange(uint unlockedSkills) => Has(unlockedSkills, SkillId.AttackCore) ? 240 : 150;

        public static float TractorRange(uint unlockedSkills) => Has(unlockedSkills, SkillId.Speed4) ? 32 : 16;

        public static void Repair(GameObject pilot, Health health, uint unlockedSkills)
        {
            if (health != null)
            {
                health.Heal(Has(unlockedSkills, SkillId.Damage1) ? 2 : 1);
            }

            if (!Has(unlockedSkills, SkillId.Damage2))
            {
                return;
            }

            // The upgraded repair also heals nearby allies, provided scenery does not block the signal.
            var allies = new List<RadarContact>();
            RadarIndex.Query(pilot.transform.position, 40, RadarKind.Friendly, allies);

            foreach (var ally in allies)
            {
                if (ally.Health != null && ally.Health != health && RadarIndex.LineOfSight(pilot.transform.position, ally))
                {
                    ally.Health.Heal(1);
                }
            }
        }

        public static void Intercept(GameObject pilot, uint unlockedSkills, ref float nextPulse, List<RadarContact> buffer)
        {
            if (Game.Net.NetSession.Active && !Game.Net.NetSession.IsHost)
            {
                return;
            }

            if (Time.time < nextPulse || (!Has(unlockedSkills, SkillId.RapidFire) && !Has(unlockedSkills, SkillId.Overclock)))
            {
                return;
            }

            // The jammer expands the search radius and allows three interceptions per pulse.
            bool jammer = Has(unlockedSkills, SkillId.Overclock);
            buffer.Clear();
            RadarIndex.Query(pilot.transform.position, jammer ? 26 : 16, RadarKind.Missile, buffer);
            int remainingInterceptions = jammer ? 3 : 1;

            foreach (var missile in buffer)
            {
                if (missile.Projectile == null || !RadarIndex.LineOfSight(pilot.transform.position, missile))
                {
                    continue;
                }

                // Tell peers which projectile disappeared before returning it to the reusable pool.
                Game.Net.NetRunner.Instance?.NoteInterception(missile.Projectile.NetworkId);
                missile.Projectile.Despawn();
                if (--remainingInterceptions == 0)
                {
                    break;
                }
            }

            nextPulse = Time.time + 2;
        }
    }
}
