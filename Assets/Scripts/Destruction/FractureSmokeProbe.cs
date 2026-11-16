using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Progression;
using UnityEngine;
namespace Game.Destruction
{
    // Invoked explicitly by standalone integration smoke tests; never starts itself in player builds.
    public static class FractureSmokeProbe
    {
        public static void Run()
        {
            if (Game.Net.NetSession.Active)
            {
                throw new InvalidOperationException("Run fracture smoke in a solo test scene");
            }

            // Record existing objects so cleanup removes only what this probe creates.
            var previous = new HashSet<int>();

            foreach (var existingObject in UnityEngine.Object.FindObjectsOfType<SalvagePickup>())
            {
                previous.Add(existingObject.GetInstanceID());
            }

            foreach (var existingObject in UnityEngine.Object.FindObjectsOfType<FractureDebris>())
            {
                previous.Add(existingObject.GetInstanceID());
            }

            var ship = new GameObject("Fracture smoke fixture");
            ship.transform.position = new Vector3(500, 500, 500);
            try
            {
                var health = ship.AddComponent<Health>();
                health.maxHealth = 100;
                health.deactivateOnDeath = false;
                health.ResetHealth();
                var enemy = ship.AddComponent<EnemyController>();
                enemy.enabled = false;
                enemy.advanceSpeed = 10;
                var weapon = ship.AddComponent<Weapon>();
                weapon.fireRateMul = 2;
                var assembly = DestructibleAssembly.Attach(ship);
                Require(assembly != null, "attach returns assembly");
                Transform engine = null;

                foreach (Transform child in ship.transform)
                {
                    if (child.name == "Detachable engine")
                    {
                        engine = child;
                        break;
                    }
                }

                Require(engine != null, "visible engine module exists");
                Physics.SyncTransforms();

                // A localized engine hit must affect both the physical fragments and ship systems.
                int previousDebrisCount = FractureDebris.ActiveCount;
                health.ApplyDamage(new DamageInfo(20, DamageSource.Script, null, engine.position, Vector3.right));
                Require(assembly.DetachedCount == 1 && assembly.LiveEngineCount == 1, "localized engine detaches alone");
                Require(Mathf.Abs(enemy.advanceSpeed - 7) < 0.001f, "engine loss reduces movement");
                Require(Mathf.Abs(weapon.fireRateMul - 1.65f) < 0.001f, "engine loss reduces weapon rate");
                Require(FractureDebris.ActiveCount >= previousDebrisCount + 2, "runtime plane cut creates physical pieces");

                foreach (var fragment in UnityEngine.Object.FindObjectsOfType<FractureDebris>())
                {
                    if (!previous.Contains(fragment.GetInstanceID()))
                    {
                        Require(fragment.GetComponent<Rigidbody>() != null && fragment.GetComponent<MeshCollider>().convex, "physical convex fragment");
                        Require(ConvexMeshCutter.IsClosed(fragment.GetComponent<MeshFilter>().sharedMesh), "runtime cut cap closes mesh");
                    }
                }

                Debug.Log("FRACTURE_RUNTIME_SMOKE_OK: localized hit, capped meshes, Rigidbody debris, engine mobility and weapon effects");
            }
            finally
            {
                UnityEngine.Object.Destroy(ship);

                foreach (var existingObject in UnityEngine.Object.FindObjectsOfType<SalvagePickup>())
                {
                    if (!previous.Contains(existingObject.GetInstanceID()))
                    {
                        UnityEngine.Object.Destroy(existingObject.gameObject);
                    }
                }

                foreach (var existingObject in UnityEngine.Object.FindObjectsOfType<FractureDebris>())
                {
                    if (!previous.Contains(existingObject.GetInstanceID()))
                    {
                        UnityEngine.Object.Destroy(existingObject.gameObject);
                    }
                }
            }
        }

        static void Require(bool value, string message)
        {
            if (!value)
            {
                throw new Exception("Fracture runtime: " + message);
            }
        }
    }
}
