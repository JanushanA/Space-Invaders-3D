using System.Collections.Generic;
using UnityEngine;
namespace Game.Core
{
    // Layer names, and the hit masks built from them.
    // Caches Unity layer masks used to decide which objects can interact in combat.
    public static class GameLayers
    {
        public const string Player = "Player";
        public const string PlayerProjectile = "PlayerProjectile";
        public const string Enemy = "Enemy";
        public const string EnemyProjectile = "EnemyProjectile";
        public const string LevelGeometry = "LevelGeometry";
        public static readonly string[] RequiredLayers =
        {
            Player, PlayerProjectile, Enemy, EnemyProjectile, LevelGeometry
        };
        private static bool cacheBuilt;
        private static LayerMask playerWeaponHitMask;
        private static LayerMask enemyWeaponHitMask;

        public static LayerMask PlayerWeaponHitMask
        {
            get
            {
                // Builds the masks lazily so layer validation happens once, only when combat needs it.
                BuildCache();
                return playerWeaponHitMask;
            }
        }

        public static LayerMask EnemyWeaponHitMask
        {
            get
            {
                BuildCache();
                return enemyWeaponHitMask;
            }
        }

        // Anything not an enemy shot counts as the player's.
        public static LayerMask HitMaskFor(DamageSource source)
        {
            // Gives enemy shots a player-only mask; every other source follows the player attack rule.
            if (source == DamageSource.EnemyProjectile)
            {
                return EnemyWeaponHitMask;
            }

            return PlayerWeaponHitMask;
        }

        private static void BuildCache()
        {
            // Caches masks after the first call so repeated projectile firing does not rebuild layer data.
            if (cacheBuilt)
            {
                return;
            }

            cacheBuilt = true;
            List<string> missing = new List<string>();

            // Collects every missing project layer so one error message explains all setup problems at once.
            for (int layerIndex = 0; layerIndex < RequiredLayers.Length; layerIndex++)
            {
                // Looks up the configured layer name because Unity returns a negative value when it is absent.
                if (LayerMask.NameToLayer(RequiredLayers[layerIndex]) < 0)
                {
                    missing.Add(RequiredLayers[layerIndex]);
                }
            }

            if (missing.Count > 0)
            {
                Debug.LogError("[GameLayers] Missing layers: " + string.Join(", ", missing) +
                               ". Shots will pass through targets.");
            }

            playerWeaponHitMask = LayerMask.GetMask(Enemy, LevelGeometry);
            enemyWeaponHitMask = LayerMask.GetMask(Player, LevelGeometry);

            // Prepares opposite collision masks so player and enemy bullets cannot damage their own side.
        }
    }
}
