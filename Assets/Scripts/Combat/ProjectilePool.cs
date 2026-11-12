using System.Collections.Generic;
using UnityEngine;
namespace Game.Combat
{
    // Recycles shots instead of instantiating and destroying them.
    // Reuses projectile objects instead of repeatedly creating and destroying bullets.
    public class ProjectilePool : MonoBehaviour
    {
        private static ProjectilePool instance;
        private static bool applicationIsQuitting;

        // Created on first use, so no setup is needed.
        public static ProjectilePool Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    // Avoids creating Unity objects while the editor or built game is shutting down.
                    return null;
                }

                if (instance == null)
                {
                    // Creates the shared pool only when a weapon first requests a projectile.
                    instance = new GameObject("ProjectilePool (runtime)").AddComponent<ProjectilePool>();
                }

                return instance;
            }
        }
        private readonly Dictionary<Projectile, Stack<Projectile>> availableByPrefab =
            new Dictionary<Projectile, Stack<Projectile>>();
        private readonly Dictionary<Projectile, Projectile> prefabByInstance =
            new Dictionary<Projectile, Projectile>();

        private void Awake()
        {
            // Keeps one global pool because every weapon needs to request the same projectile types.
            if (instance != null && instance != this)
            {
                // Removes accidental duplicate pools so all weapons return bullets to the same owner.
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        public Projectile Get(Projectile prefab)
        {
            // Reuses an inactive projectile when possible to avoid garbage collection during firefights.
            if (prefab == null)
            {
                Debug.LogError("[ProjectilePool] Asked for a shot with no prefab.", this);
                return null;
            }

            Stack<Projectile> available = GetStack(prefab);

            // Keeps a separate stack per prefab so different bullet visuals are never mixed together.
            Projectile projectile = null;

            while (available.Count > 0 && projectile == null)
            {
                // Discards destroyed references that may remain if an object was removed externally.
                // Skips anything destroyed behind the pool's back.
                projectile = available.Pop();
            }

            if (projectile == null)
            {
                // Creates a new projectile only when the matching inactive stack is empty.
                projectile = Instantiate(prefab, transform);
                projectile.name = prefab.name;
                prefabByInstance[projectile] = prefab;
            }

            projectile.gameObject.SetActive(true);

            // Activating after setup lets its normal Unity lifecycle prepare it for a new launch.
            return projectile;
        }

        // Safe to call twice.
        public void Release(Projectile projectile)
        {
            // Deactivates the projectile first, then stores it under its original prefab type.
            if (projectile == null || !projectile.gameObject.activeSelf)
            {
                return;
            }

            projectile.gameObject.SetActive(false);
            if (prefabByInstance.TryGetValue(projectile, out Projectile prefab))
            {
                // Returns the instance to the stack belonging to the prefab it was originally created from.
                GetStack(prefab).Push(projectile);
            }
            else
            {
                Destroy(projectile.gameObject);
            }
        }

        private Stack<Projectile> GetStack(Projectile prefab)
        {
            // Creates the prefab's inactive stack on first use, then reuses that collection afterwards.
            if (!availableByPrefab.TryGetValue(prefab, out Stack<Projectile> stack))
            {
                stack = new Stack<Projectile>();
                availableByPrefab.Add(prefab, stack);
            }

            return stack;
        }
    }
}
