using System;
using System.Reflection;
using Game.Combat;
using Game.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Checks that live flight instruments and cockpit geometry remain readable without intercepting input.
public static class CockpitHudChecks
{
    [MenuItem("Tools/Group Game/Check cockpit and flight instruments")]
    public static void Run()
    {
        var pilot = new GameObject("Instrument check pilot");
        Canvas canvas = null;
        try
        {
            Health health = pilot.AddComponent<Health>();
            health.maxHealth = 200f;
            health.ResetHealth();
            var gameHud = pilot.AddComponent<GameHud>();
            if (Get(gameHud, "canvas") == null)
            {
                Call(gameHud, "Awake");
            }

            Call(gameHud, "OnDisable");
            Call(gameHud, "OnEnable");
            canvas = (Canvas)Get(gameHud, "canvas");
            health.SetNetworkHealth(75f);
            Require(Mathf.Approximately(((Image)Get(gameHud, "hullFill")).fillAmount, 0.375f), "Hull instrument did not follow HealthChanged");
            Require(((Text)Get(gameHud, "hullText")).text.Contains("038%"), "Hull percentage is not current health / max health");
            Require(canvas.GetComponent<GraphicRaycaster>() == null, "Flight instrument canvas must not intercept pointer input");
            Call(gameHud, "OnDisable");
            Require(!canvas.gameObject.activeSelf, "Disabled player left flight UI visible");
            Call(gameHud, "OnEnable");
            Require(canvas.gameObject.activeSelf, "Re-enabled player did not restore flight UI");
            CockpitRig cockpitRig = pilot.GetComponent<CockpitRig>();
            Call(cockpitRig, "Build");
            GameObject cabin = (GameObject)Get(cockpitRig, "cabin");
            MeshRenderer[] renderers = cabin.GetComponentsInChildren<MeshRenderer>();
            Require(renderers.Length == 4, "Cockpit must remain four shared-material mesh batches");
            Require(cabin.GetComponentsInChildren<Collider>().Length == 0, "Camera cabin must never affect combat collision");

            foreach (MeshFilter filter in cabin.GetComponentsInChildren<MeshFilter>())
            {
                Require(filter.sharedMesh.vertexCount > 0, "Cockpit mesh batch is empty");

                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Require(vertex.z > 0.05f, "Cabin crosses the first-person near plane");

                    // The central aiming corridor must remain completely unobstructed.
                    Require(Mathf.Abs(vertex.x / vertex.z) > 0.25f || Mathf.Abs(vertex.y / vertex.z) > 0.23f,
                        "Cockpit metal intrudes into the forward aiming corridor");
                }
            }

            Call(cockpitRig, "LateUpdate");
            Require(!cabin.activeSelf, "Cockpit must be hidden without an active first-person FlightView");

            // This EditMode check invokes Awake manually; Unity never starts this component's
            // play-mode lifecycle, so invoke the corresponding teardown callbacks explicitly too.
            Call(gameHud, "OnDisable");
            Call(gameHud, "OnDestroy");
            Call(cockpitRig, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(pilot);
            Require(canvas == null, "HUD teardown did not release its detached canvas");
            Debug.Log("COCKPIT HUD CHECKS: PASS live health, pointer isolation, enable/disable cleanup, four mesh batches, clear aiming corridor and first-person-only cabin");
        }
        finally
        {
            if (pilot != null)
            {
                var cockpitRig2 = pilot.GetComponent<CockpitRig>();
                if (cockpitRig2 != null)
                {
                    Call(cockpitRig2, "OnDestroy");
                }

                UnityEngine.Object.DestroyImmediate(pilot);
            }

            if (canvas != null)
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
        }
    }

    private static object Get(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
    }

    private static void Call(object value, string name)
    {
        value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, null);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
