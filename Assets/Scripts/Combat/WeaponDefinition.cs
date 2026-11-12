using UnityEngine;
namespace Game.Combat
{
    // Tuning numbers for one weapon.
    [CreateAssetMenu(menuName = "Game/Combat/Weapon Definition", fileName = "NewWeaponDefinition")]

    // Stores reusable weapon values so different ships can share the same weapon setup.
    public class WeaponDefinition : ScriptableObject
    {
        public Projectile projectilePrefab;
        public float projectileSpeed = 40f;
        public float projectileRadius = 0.15f;
        public float projectileLifetime = 4f;
        public float damagePerProjectile = 10f;
        public float shotsPerSecond = 4f;
        public int projectilesPerShot = 1;
        public float spreadAngle;

        public float SecondsBetweenShots
        {
            // Divides one second by the fire rate to turn shots per second into a firing delay.
            get
            {
                return 1f / Mathf.Max(0.05f, shotsPerSecond);
            }
        }

        private void OnValidate()
        {
            // Clamps weapon values in the editor so invalid tuning cannot create broken projectiles at runtime.
            projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
            projectileRadius = Mathf.Max(0.01f, projectileRadius);
            projectileLifetime = Mathf.Max(0.1f, projectileLifetime);
            damagePerProjectile = Mathf.Max(0f, damagePerProjectile);
            shotsPerSecond = Mathf.Max(0.05f, shotsPerSecond);
            projectilesPerShot = Mathf.Max(1, projectilesPerShot);
            spreadAngle = Mathf.Clamp(spreadAngle, 0f, 180f);
        }
    }
}
