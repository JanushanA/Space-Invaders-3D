using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Net;
using Game.Player;
using UnityEditor;
using UnityEngine;

// Checks boss clearance and camera ownership with both first-person and third-person flight views.
public static class FlightCombatChecks
{
    [MenuItem("Tools/Group Game/Check flight views and escort clearance")]
    public static void Run()
    {
        CheckEscorts();
        CheckViews();
        FlightBindingChecks.Run();
        Debug.Log("FLIGHT COMBAT CHECKS: PASS escort foreground, smooth retreat, party clearance, ordinary phases, local visibility and camera convergence");
    }

    private static void CheckEscorts()
    {
        var pilot = new GameObject("CheckPilot");
        var wingman = new GameObject("CheckWingman");
        var host = new GameObject("CheckMothership");
        var prefab = new GameObject("CheckEscortPrefab");
        var spawned = new List<GameObject>();
        PlayerRoster.Add(pilot.transform);
        PlayerRoster.Add(wingman.transform);
        try
        {
            wingman.transform.position = new Vector3(20f, 0f, 8f);
            host.AddComponent<Health>();
            var boss = host.AddComponent<MotherShipBoss>();
            host.transform.position = new Vector3(0f, 0f, 26f);
            host.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Set(boss, "player", pilot.transform);
            boss.Bind(prefab, null);
            SetMove(boss, "Minions");
            Call(boss, "SpawnMinions");
            spawned.AddRange((List<GameObject>)Get(boss, "escorts"));
            Require(spawned.Count >= 3, "Escort phase spawned no targets");

            foreach (var escort in spawned)
            {
                Require(escort.transform.position.z > 10f && escort.transform.position.z < 19f,
                    "Escort spawned behind or inside the mothership's foreground hull");
            }

            Vector3 before = host.transform.position;
            Call(boss, "ChasePlayer", 0.1f);
            Require(host.transform.position.z > before.z && Vector3.Distance(before, host.transform.position) <= 4.01f,
                "Escort retreat must move away continuously, without teleporting");

            for (int simulationStep = 0; simulationStep < 300; simulationStep++)
            {
                Call(boss, "ChasePlayer", 0.1f);
            }

            Require(Vector3.Distance(host.transform.position, pilot.transform.position) >= 52f &&
                    Vector3.Distance(host.transform.position, wingman.transform.position) >= 52f,
                "Escort phase did not hold clear of the whole party");
            Require(Mathf.Approximately(boss.PlanarRange, ShipClearance.BossRange),
                "Retreat must not teleport the player through enlarged collision clearance");
            SetMove(boss, "AimedLaserWarn");
            host.transform.position = new Vector3(0f, 0f, 64f);
            Call(boss, "ChasePlayer", 0.1f);
            Require(host.transform.position.z < 64f, "Normal attacks no longer approach the player");
            SetMove(boss, "Charge");
            before = host.transform.position;
            Call(boss, "ChasePlayer", 0.1f);
            Require(host.transform.position == before && Mathf.Approximately(boss.PlanarRange, ShipClearance.BossRamRange),
                "Retreat changed charge movement or clearance");
            Require(boss.CapturePresentation().Phase == 9, "Existing network phase identifiers changed");
        }
        finally
        {
            PlayerRoster.Remove(pilot.transform);
            PlayerRoster.Remove(wingman.transform);

            foreach (var escort in spawned)
            {
                UnityEngine.Object.DestroyImmediate(escort);
            }

            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(pilot);
            UnityEngine.Object.DestroyImmediate(wingman);
        }
    }

    private static void CheckViews()
    {
        var cameraHost = new GameObject("CheckCamera");
        var playerHost = new GameObject("CheckLocalShip");
        playerHost.SetActive(false);
        var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var remote = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var sceneObject = new GameObject("Aim");
        try
        {
            var camera = cameraHost.AddComponent<Camera>();
            var follower = cameraHost.AddComponent<MoveCamera>();
            var player = playerHost.AddComponent<PlayerController>();
            var health = playerHost.AddComponent<Health>();
            health.ResetHealth();
            player.health = health;
            sceneObject.transform.SetParent(playerHost.transform, false);
            player.aimPivot = sceneObject.transform;
            hull.name = "Hull";
            hull.transform.SetParent(playerHost.transform, false);
            hull.transform.localScale = new Vector3(4f, 1f, 4f);
            var weapon = sceneObject.AddComponent<Weapon>();
            weapon.damageSource = DamageSource.PlayerProjectile;
            player.weapon = weapon;
            var view = playerHost.AddComponent<FlightView>();
            Call(view, "OnEnable");
            Call(view, "AcquireCamera", camera);
            var renderer = hull.GetComponent<Renderer>();
            view.SetFirstPerson(true);
            Require(view.IsFirstPerson && renderer.forceRenderingOff && !remote.GetComponent<Renderer>().forceRenderingOff,
                "First-person must hide only the local Hull");
            Require(!follower.enabled && view.ViewCamera == camera && camera.nearClipPlane <= 0.08f,
                "Flight view did not take ownership of the existing camera");
            view.SetFirstPerson(false);
            Require(!renderer.forceRenderingOff, "Third-person did not restore the local hull");
            playerHost.SetActive(false);

            // Pose calculations also run with edit-mode fixtures, without advancing a frame.
            foreach (bool firstPerson in new[] { false, true })
            {
                foreach (float pitch in new[] { -40f, 0f, 55f })
                {
                    view.SetFirstPerson(firstPerson);
                    sceneObject.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
                    Vector3 position;
                    Vector3 direction;
                    view.GetShotPose(weapon, out position, out direction);
                    Call(view, "ApplyCameraPose");
                    Vector3 launchInView = camera.WorldToViewportPoint(position);
                    Require(launchInView.z > 0f && launchInView.x > 0f && launchInView.x < 1f && launchInView.y > 0f && launchInView.y < 1f,
                        "Player shot begins outside the view");
                    var hullBounds = renderer.bounds;
                    float entry;
                    var sight = new Ray(camera.transform.position, (position - camera.transform.position).normalized);
                    Require(firstPerson || !hullBounds.IntersectRay(sight, out entry) || entry > Vector3.Distance(camera.transform.position, position) - 0.05f,
                        "Player hull blocks the muzzle from the third-person camera");
                    var target = camera.transform.position + camera.transform.forward * 120f;
                    Require(Vector3.Cross((target - position).normalized, direction).magnitude < 0.001f,
                        "Shot does not converge with the center reticle");
                }
            }

            view.SetFirstPerson(false);
            PlayerSkills.MenuOpen = true;
            view.SetFirstPerson(true);
            Require(!view.IsFirstPerson, "Skill menu allowed view switching");
            PlayerSkills.MenuOpen = false;
            Set(health, "isDead", true);
            view.SetFirstPerson(true);
            Require(!view.IsFirstPerson, "Defeated ship allowed view switching");
            Set(health, "isDead", false);
            Call(view, "OnDisable");
            Require(follower.enabled && !renderer.forceRenderingOff, "Camera/hull ownership was not restored");
        }
        finally
        {
            PlayerSkills.MenuOpen = false;
            UnityEngine.Object.DestroyImmediate(playerHost);
            UnityEngine.Object.DestroyImmediate(remote);
            UnityEngine.Object.DestroyImmediate(cameraHost);
        }
    }

    private static void SetMove(MotherShipBoss boss, string name)
    {
        var field = typeof(MotherShipBoss).GetField("move", BindingFlags.Instance | BindingFlags.NonPublic);
        field.SetValue(boss, Enum.Parse(field.FieldType, name));
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
