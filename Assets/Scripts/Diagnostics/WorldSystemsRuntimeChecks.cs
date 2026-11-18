using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.AI;
using Game.Enemies;
using Game.Campaign;
using Game.Combat;
using Game.Core;
using Game.Player;
using Game.World;
using UnityEngine;
namespace Game.Diagnostics
{
    // Invoked by the standalone campaign smoke harness after a mission has started.
    public static class WorldSystemsRuntimeChecks
    {
        static void Require(bool value, string message)
        {
            if (!value)
            {
                throw new Exception("World runtime: " + message);
            }
        }

        public static IEnumerator Run(CampaignMission mission, PlayerController pilot)
        {
            Require(mission != null && mission.Plan != null && pilot != null, "live mission and pilot");
            Require(mission.NavigationCore.GetComponentsInChildren<MeshFilter>().Length >= 15, "detailed reusable core assembly");
            Require(mission.ExtractionBeacon.GetComponentsInChildren<MeshFilter>().Length >= 18, "detailed reusable jump beacon");

            foreach (var detailLevels in UnityEngine.Object.FindObjectsOfType<LODGroup>())
            {
                if (detailLevels.name.StartsWith("Drifting rock"))
                {
                    Require(detailLevels.GetComponent<Collider>() != null, "decorative rock physically blocks");
                }
            }

            for (int firstRelay = 0; firstRelay < mission.Relays.Count; firstRelay++)
            {
                for (int secondRelay = firstRelay + 1; secondRelay < mission.Relays.Count; secondRelay++)
                {
                    Require(Vector3.Distance(mission.Relays[firstRelay].position, mission.Relays[secondRelay].position) >= 110, "live relay spacing");
                }
            }

            // Move collision probes away from mission traffic and save every player value the probes modify.
            var fixture = new GameObject("Runtime world fixture");
            fixture.transform.position = mission.transform.position + new Vector3(700, 400, 700);
            Vector3 saved = pilot.transform.position;
            bool enabled = pilot.enabled;
            Vector3 savedVelocity = Read<Vector3>(pilot, "currentVelocity");
            float savedAcceleration = pilot.acceleration;
            float savedBank = Read<float>(pilot, "bankRoll");
            bool savedInput = Read<bool>(pilot, "inputEnabled");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(fixture.transform, false);
            wall.layer = LayerMask.NameToLayer(GameLayers.LevelGeometry);
            try
            {
                pilot.enabled = false;
                pilot.acceleration = 0;
                Write(pilot, "inputEnabled", false);
                VerifyPlayerMovement(pilot, fixture, wall.GetComponent<Collider>());
                pilot.transform.position = saved;
                pilot.acceleration = savedAcceleration;
                Write(pilot, "currentVelocity", savedVelocity);
                Write(pilot, "inputEnabled", savedInput);
                Write(pilot, "bankRoll", savedBank);
                Call(pilot, "ApplyBodyRotation");
                pilot.enabled = enabled;
                VerifyHostileMovement(fixture, wall.GetComponent<Collider>());
                wall.SetActive(false);
                UnityEngine.Object.Destroy(wall);
                wall = null;

                // Use real elapsed frames to check event cancellation, rescheduling and owner cleanup.
                var clock = BattleTimeline.For(fixture);
                int fired = 0;
                int cancelled = clock.After(0.04f, () => fired += 100);
                clock.Cancel(cancelled);
                int moved = clock.After(5, () => fired++);
                clock.Reschedule(moved, 0.06f);
                yield return new WaitForSeconds(0.12f);
                Require(fired == 1, "live indexed scheduler cancellation and rescheduling");
                clock.After(0.04f, () => fired += 10);
                clock.enabled = false;
                yield return new WaitForSeconds(0.08f);
                Require(fired == 1 && clock.PendingCount == 0, "disabled owner leaves no delayed events");

                // An armed reactor must add route danger, damage a nearby hull and then remove its danger cost.
                var victim = new GameObject("Delayed blast victim", typeof(SphereCollider));
                victim.transform.SetParent(fixture.transform, false);
                var health = victim.AddComponent<Health>();
                health.maxHealth = 20;
                health.ResetHealth();
                mission.ArmReactor(victim.transform.position, 0.08f);
                Require(mission.HazardCost(victim.transform.position) > 0, "armed explosion affects live navigation hazard costs");
                yield return new WaitForSeconds(0.15f);
                Require(health.CurrentHealth < 20, "timeline actually detonates and damages nearby hull");
                Require(mission.HazardCost(victim.transform.position) == 0, "exploded hazard removed from navigation costs");
                Debug.Log("WORLD_RUNTIME_CHECKS_OK: live objectives/assets/rocks; real player Move+Dash, ordinary Advance, AcePilot.Tick and SquadronMember.Tick control-vs-rock sweeps before/after coordinate shift; scheduled cancellation/rescheduling/cleanup; damaging delayed explosion");
            }
            finally
            {
                if (pilot != null)
                {
                    pilot.transform.position = saved;
                    pilot.acceleration = savedAcceleration;
                    Write(pilot, "currentVelocity", savedVelocity);
                    Write(pilot, "inputEnabled", savedInput);
                    Write(pilot, "bankRoll", savedBank);
                    Call(pilot, "ApplyBodyRotation");
                    pilot.enabled = enabled;
                }

                UnityEngine.Object.Destroy(fixture);
            }
        }
        static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, PrivateInstance).GetValue(owner);

        static void Write(object owner, string field, object value) => owner.GetType().GetField(field, PrivateInstance).SetValue(owner, value);

        static void Call(object owner, string method) => owner.GetType().GetMethod(method, PrivateInstance).Invoke(owner, null);

        static void AssertSweep(string label, Transform actor, Collider wall, Vector3 start, Vector3 forward, Action prepare, Action tick)
        {
            // The unobstructed control rejects a false positive from disabled AI, zero dt,
            // failed target acquisition or avoidance choosing to stop before the movement hook.
            wall.enabled = false;
            actor.position = start;
            prepare();
            Physics.SyncTransforms();
            tick();
            Require(Vector3.Dot(actor.position - start, forward) > 100, label + " unobstructed control actually moves through wall plane");
            wall.enabled = true;
            actor.position = start;
            prepare();
            Physics.SyncTransforms();
            tick();
            float travelled = Vector3.Dot(actor.position - start, forward);
            Require(travelled > 10 && travelled < 37.1f, label + " stops on near side of rock after long displacement; distance=" + travelled);
            Require(Vector3.Distance(wall.ClosestPoint(actor.position), actor.position) >= 2.39f, label + " retains hull clearance");
        }

        static void VerifyPlayerMovement(PlayerController pilot, GameObject fixture, Collider wall)
        {
            Require(Time.deltaTime > 0.000001f, "normal flight test has a real frame delta");
            pilot.GetAimPose(out float yawDegrees, out float pitch, out _);
            Vector3 direction = Quaternion.Euler(0, yawDegrees, 0) * Quaternion.Euler(pitch, 0, 0) * Vector3.forward;

            for (int pass = 0; pass < 2; pass++)
            {
                if (pass > 0)
                {
                    fixture.transform.position += new Vector3(-120, -60, -120);
                }

                wall.transform.position = fixture.transform.position + direction * 40;
                wall.transform.rotation = Quaternion.LookRotation(direction);
                wall.transform.localScale = new Vector3(40, 40, 2);
                string suffix = pass == 0 ? "" : " after coordinate shift";
                AssertSweep("PlayerController.Move" + suffix, pilot.transform, wall, fixture.transform.position, direction,
                    () => Write(pilot, "currentVelocity", direction * (1000 / Time.deltaTime)), () => Call(pilot, "Move"));
                AssertSweep("PlayerController.DashAlongAim" + suffix, pilot.transform, wall, fixture.transform.position, direction,
                    () => Write(pilot, "currentVelocity", Vector3.zero), () => pilot.DashAlongAim(1000));
            }
        }

        static void VerifyHostileMovement(GameObject fixture, Collider wall)
        {
            var actor = new GameObject("Hostile movement probe");
            actor.transform.SetParent(fixture.transform, false);
            actor.AddComponent<Health>();
            var ordinary = actor.AddComponent<EnemyController>();
            ordinary.enabled = false;
            ordinary.strafeDistance = 0;
            ordinary.stoppingDistance = 0;
            ordinary.advanceSpeed = 1000 / Time.deltaTime;
            var wing = new GameObject("Squad probe wingman");
            wing.transform.SetParent(fixture.transform, false);
            wing.AddComponent<Health>();
            var target = new GameObject("Hostile movement destination");
            target.transform.SetParent(fixture.transform, false);
            var acePilot = AcePilot.Attach(actor);
            Require(acePilot != null && acePilot.Ready, "actual trained ace movement available");
            var squad = SquadronCoordinator.Create(new List<GameObject> { actor, wing });
            Require(squad != null, "actual squad movement available");
            squad.enabled = false;
            squad.transform.SetParent(fixture.transform, false);
            var member = actor.GetComponent<SquadronMember>();
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass > 0)
                    {
                        fixture.transform.position += new Vector3(-120, -60, -120);
                    }

                    Vector3 start = fixture.transform.position;
                    target.transform.position = start + Vector3.forward * 1200;
                    wing.transform.position = start + Vector3.right * 100;
                    wall.transform.position = start + Vector3.forward * 40;
                    wall.transform.rotation = Quaternion.identity;
                    wall.transform.localScale = new Vector3(40, 40, 2);
                    string suffix = pass == 0 ? "" : " after coordinate shift";
                    AssertSweep("EnemyController.Advance" + suffix, actor.transform, wall, start, Vector3.forward,
                        () =>
                        {
                            Write(ordinary, "target", target.transform);
                            Write(ordinary, "lastForward", Vector3.forward);
                        }, () => Call(ordinary, "Advance"));
                    AssertSweep("AcePilot.Tick" + suffix, actor.transform, wall, start, Vector3.forward,
                        () =>
                        {
                            Write(acePilot, "velocity", Vector3.zero);
                            Write(acePilot, "nextDecision", float.PositiveInfinity);
                            Write(acePilot, "action", 4);
                        },
                        () => Require(acePilot.Tick(target.transform, 250, null, out _), "ace executes movement branch"));
                    AssertSweep("SquadronMember.Tick" + suffix, actor.transform, wall, start, Vector3.forward,
                        () =>
                        {
                            Write(member, "<Velocity>k__BackingField", Vector3.zero);
                            Write(squad, "slots", new[] { target.transform.position, wing.transform.position });
                            Write(squad, "assignment", new[] { 0, 1 });
                        },
                        () => Require(member.Tick(target.transform, 1000, out _), "squad executes movement branch"));
                }
            }
            finally
            {
                // Disable before deferred destruction so no fixture can tick during later waits.
                actor.SetActive(false);
                wing.SetActive(false);
                target.SetActive(false);
                squad.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(actor);
                UnityEngine.Object.Destroy(wing);
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(squad.gameObject);
            }
        }
    }
}
