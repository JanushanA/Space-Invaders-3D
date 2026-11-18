using System;
using System.Collections.Generic;
using System.Reflection;
using Game.World;
using Game.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Checks rebasing across physics, particles and drawing history while preserving global positions.
public static class FloatingOriginChecks
{
    [MenuItem("Tools/Group Game/Check floating origin preservation")]
    public static void Run()
    {
        var root = new GameObject("OriginCheckRoot");
        var inactive = new GameObject("OriginCheckInactive");
        var frame = new GameObject("OriginCheckCustomFrame");
        var fixedFrame = new GameObject("OriginCheckFixedFrame");
        var overlay = new GameObject("OriginCheckOverlay", typeof(RectTransform), typeof(Canvas));
        var fixtures = new List<Transform> { root.transform, inactive.transform, frame.transform, overlay.transform };
        Vector3 originalOffset = FloatingOrigin.OriginOffset;
        Vector3 totalShift = Vector3.zero;
        int notifications = 0;
        Action<Vector3> listener = delta => notifications++;
        FloatingOrigin.Shifted += listener;
        EngineTrail engine = null;
        try
        {
            root.transform.position = new Vector3(6000f, 100f, -3000f);
            engine = root.AddComponent<EngineTrail>();
            typeof(EngineTrail).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(engine, null);
            var cameraObject = new GameObject("OriginCheckCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = Vector3.back * 10f;
            var camera = cameraObject.AddComponent<Camera>();
            Vector3 projection = camera.WorldToViewportPoint(root.transform.position);
            inactive.transform.position = root.transform.position + Vector3.one;
            inactive.SetActive(false);
            frame.transform.position = root.transform.position + Vector3.right * 100f;
            frame.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            frame.transform.localScale = new Vector3(2f, 1f, 3f);
            fixedFrame.transform.position = root.transform.position;
            overlay.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.transform.position = new Vector3(400f, 300f, 0f);
            Vector3 overlayPosition = overlay.transform.position;
            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(root.transform, false);
            bodyObject.transform.localPosition = new Vector3(3f, 4f, 5f);
            var body = bodyObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = new Vector3(7f, -2f, 9f);
            body.angularVelocity = new Vector3(0.1f, 0.2f, 0.3f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var world = MakeParticles(root.transform, ParticleSystemSimulationSpace.World, null);
            var local = MakeParticles(root.transform, ParticleSystemSimulationSpace.Local, null);
            var custom = MakeParticles(root.transform, ParticleSystemSimulationSpace.Custom, frame.transform);
            var fixedCustom = MakeParticles(root.transform, ParticleSystemSimulationSpace.Custom, fixedFrame.transform);
            Vector3 worldParticle = Position(world);
            Vector3 localParticle = Position(local);
            Vector3 customWorld = frame.transform.TransformPoint(Position(custom));
            Vector3 fixedCustomWorld = fixedFrame.transform.TransformPoint(Position(fixedCustom));
            var trail = bodyObject.AddComponent<TrailRenderer>();
            trail.emitting = false;
            trail.time = 1000f;
            trail.AddPositions(new[] { root.transform.position, root.transform.position + Vector3.forward * 5f });
            var line = bodyObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPositions(new[] { root.transform.position, root.transform.position + Vector3.up * 3f });
            Vector3 rootBefore = root.transform.position;
            Vector3 bodyBefore = body.position;
            Vector3 globalBefore = FloatingOrigin.ToGlobalPosition(rootBefore);
            Vector3 delta = -rootBefore;
            ShiftFixtures(fixtures, delta);
            totalShift += delta;
            Require(root.transform.position.sqrMagnitude < 0.001f && Close(body.position, bodyBefore + delta), "Hierarchy or rigidbody shifted twice");
            Require(Close(camera.WorldToViewportPoint(root.transform.position), projection), "Rebase changed camera-relative composition");
            Require(Close((Vector3)typeof(EngineTrail).GetField("lastPosition", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(engine), root.transform.position),
                "Rebase left a stale engine velocity sample");
            Require(Close(inactive.transform.position, Vector3.one), "Inactive scene root was skipped");
            Require(Close(overlay.transform.position, overlayPosition), "Screen-space overlay was rebased");
            Require(Close(body.velocity, new Vector3(7f, -2f, 9f)) && Close(body.angularVelocity, new Vector3(0.1f, 0.2f, 0.3f)) &&
                body.interpolation == RigidbodyInterpolation.Interpolate, "Rebasing altered rigidbody motion");
            Require(Close(Position(world), worldParticle + delta) && Close(Position(local), localParticle), "World/local particle spaces were confused");
            Require(Close(frame.transform.TransformPoint(Position(custom)), customWorld + delta), "Moved custom particle frame shifted twice");
            Require(Close(fixedFrame.transform.TransformPoint(Position(fixedCustom)), fixedCustomWorld + delta), "Unmoved custom particle frame was not compensated");
            Require(Close(trail.GetPosition(0), rootBefore + delta) && Close(trail.GetPosition(1), rootBefore + Vector3.forward * 5f + delta), "Trail history streaked across the old origin");
            Require(Close(line.GetPosition(0), rootBefore + delta), "World line endpoints were not shifted");
            Require(Close(FloatingOrigin.ToGlobalPosition(root.transform.position), globalBefore) &&
                Close(FloatingOrigin.ToLocalPosition(globalBefore), root.transform.position), "Wire coordinates changed after rebasing");
            Vector3 second = new Vector3(-2100f, 30f, 80f);
            ShiftFixtures(fixtures, second);
            totalShift += second;
            Require(notifications == 2 && Close(FloatingOrigin.ToGlobalPosition(root.transform.position), globalBefore), "Repeated rebases lost offset or notifications");
            Debug.Log("FLOATING ORIGIN CHECKS: PASS hierarchy, inactive/UI roots, physics, particle spaces, trail/line history and repeated global conversion");
        }
        finally
        {
            FloatingOrigin.Shifted -= listener;
            if (totalShift != Vector3.zero)
            {
                ShiftFixtures(fixtures, -totalShift);
            }

            if (engine != null)
            {
                typeof(EngineTrail).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(engine, null);
            }

            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(inactive);
            UnityEngine.Object.DestroyImmediate(frame);
            UnityEngine.Object.DestroyImmediate(fixedFrame);
            UnityEngine.Object.DestroyImmediate(overlay);
            Require(Close(FloatingOrigin.OriginOffset, originalOffset), "Check leaked origin offset");
        }

        CheckSceneReset();
    }

    private static void CheckSceneReset()
    {
        var property = typeof(FloatingOrigin).GetProperty("OriginOffset");
        var setter = property.GetSetMethod(true);
        Vector3 original = FloatingOrigin.OriginOffset;
        try
        {
            setter.Invoke(null, new object[] { new Vector3(6000f, 20f, -3000f) });
            var callback = typeof(FloatingOrigin).GetMethod("HandleSceneLoaded", BindingFlags.Static | BindingFlags.NonPublic);
            callback.Invoke(null, new object[] { default(Scene), LoadSceneMode.Additive });
            Require(Close(FloatingOrigin.OriginOffset, new Vector3(6000f, 20f, -3000f)), "Additive scene discarded the current world origin");
            callback.Invoke(null, new object[] { default(Scene), LoadSceneMode.Single });
            Require(FloatingOrigin.OriginOffset == Vector3.zero, "Scene retry retained the previous round's origin");
        }
        finally
        {
            setter.Invoke(null, new object[] { original });
        }
    }

    private static ParticleSystem MakeParticles(Transform parent, ParticleSystemSimulationSpace space, Transform custom)
    {
        var host = new GameObject("Particles_" + space);
        host.transform.SetParent(parent, false);
        var system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.playOnAwake = false;
        main.simulationSpace = space;
        if (custom != null)
        {
            main.customSimulationSpace = custom;
        }

        var emission = system.emission;
        emission.enabled = false;

        // Edit-mode stopped systems have not allocated their native simulation buffer.
        // Initialize it, then pause so the fixture cannot age between assertions.
        system.Play(false);
        system.Pause(false);
        var particles = new[] { new ParticleSystem.Particle { position = new Vector3(4f, 5f, 6f), velocity = Vector3.forward,
            startLifetime = 60f, remainingLifetime = 50f, startSize = 1f } };
        system.SetParticles(particles, 1);
        Require(system.particleCount == 1, "Particle fixture failed to initialize " + space + " simulation");
        return system;
    }

    private static Vector3 Position(ParticleSystem system)
    {
        var particles = new ParticleSystem.Particle[1];
        Require(system.GetParticles(particles) == 1, "Particle count changed in " + system.name);
        Require(Close(particles[0].velocity, Vector3.forward), "Rebasing changed particle velocity");
        return particles[0].position;
    }

    private static void ShiftFixtures(List<Transform> roots, Vector3 offset)
    {
        typeof(FloatingOrigin).GetMethod("ShiftRoots", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { roots, offset });
    }

    private static bool Close(Vector3 actualPosition, Vector3 expectedPosition) => Vector3.Distance(actualPosition, expectedPosition) < 0.01f;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
