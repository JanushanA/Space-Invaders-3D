using System.Collections.Generic;
using Game.Combat;
using UnityEngine;
namespace Game.Net
{
    // Tracks player ship references for enemy targeting and party-wipe checks in solo or multiplayer.
    // Queries inspect health so a defeated ship can remain registered without being chosen as a target.
    public static class PlayerRoster
    {
        private static readonly List<Transform> ships = new List<Transform>();

        public static void Add(Transform ship)
        {
            // Adds each active player ship once so enemies can target any living pilot.
            if (ship == null)
            {
                return;
            }

            if (ships.Contains(ship))
            {
                // Avoids duplicate references when a ship is enabled more than once.
                return;
            }

            ships.Add(ship);
        }

        public static void Remove(Transform ship)
        {
            // Removes a disabled or destroyed player so enemies cannot keep choosing an invalid target.
            ships.Remove(ship);
        }

        public static void Clear()
        {
            // Clears old scene references before a fresh multiplayer match creates new player ships.
            ships.Clear();
        }

        public static int Count
        {
            // Exposes the tracked ship count for systems that need party size information.
            get
            {
                return ships.Count;
            }
        }

        public static Transform Nearest(Vector3 from)
        {
            // Finds the closest living ship using squared distance to avoid unnecessary square roots.
            Transform best = null;
            float bestSqr = float.MaxValue;
            int shipIndex;

            for (shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                // Skips missing transforms and defeated ships so AI targets a valid active pilot.
                Transform ship = ships[shipIndex];
                if (ship == null)
                {
                    continue;
                }

                Health health = ship.GetComponent<Health>();
                if (health != null && !health.IsAlive)
                {
                    continue;
                }

                float squaredDistance = (ship.position - from).sqrMagnitude;
                if (squaredDistance < bestSqr)
                {
                    bestSqr = squaredDistance;
                    best = ship;
                }
            }

            return best;
        }

        public static int AliveCount()
        {
            // Counts playable ships for party-wipe checks without treating a destroyed player as alive.
            int alivePilotCount = 0;
            int shipIndex;

            for (shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                Transform ship = ships[shipIndex];
                if (ship == null)
                {
                    continue;
                }

                Health health = ship.GetComponent<Health>();
                if (health == null || health.IsAlive)
                {
                    alivePilotCount++;
                }
            }

            return alivePilotCount;
        }

        public static Transform At(int index)
        {
            // This index is the roster insertion order, not necessarily the pilot's lobby slot.
            if (index < 0 || index >= ships.Count)
            {
                return null;
            }

            return ships[index];
        }
    }
}
