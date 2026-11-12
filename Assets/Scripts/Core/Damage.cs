using UnityEngine;
namespace Game.Core
{
    // What caused a hit.
    // Identifies the system that caused damage for scoring and combat rules.
    public enum DamageSource
    {
        Unknown = 0,
        PlayerProjectile = 1,
        EnemyProjectile = 2,
        Script = 3
    }

    // One hit.
    // Carries damage details between a projectile or skill and a Health component.
    public struct DamageInfo
    {
        public float Amount;
        public DamageSource Source;
        public GameObject Instigator;
        public Vector3 HitPoint;
        public Vector3 HitNormal;

        public DamageInfo(
            float amount,
            DamageSource source,
            GameObject instigator,
            Vector3 hitPoint,
            Vector3 hitNormal)
        {
            Amount = Mathf.Max(0f, amount);

            // Removes negative damage at creation time so a malformed hit cannot accidentally heal a target.
            Source = source;
            Instigator = instigator;
            HitPoint = hitPoint;
            HitNormal = hitNormal;

            // Keeps impact position and surface direction for effects that need to face the collision correctly.
        }
    }

    // Anything a shot can damage.
    // Lets any object receive damage without the attacker needing to know its exact type.
    public interface IDamageable
    {
        bool IsAlive
        {
            get;
        }

        void ApplyDamage(DamageInfo info);
    }
}
