using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps the player near the coordinate origin to avoid visible jitter far from the starting point.
namespace Game.World
{
    // Runs after ship movement and before FlightView places the camera.
    [DefaultExecutionOrder(50)]
    public sealed class FloatingOrigin : MonoBehaviour
    {
        public static Vector3 OriginOffset
        {
            get; private set;
        }
        public static event Action<Vector3> Shifted;
        private const float Threshold = 2000f;

        public static Vector3 ToGlobalPosition(Vector3 local) => local + OriginOffset;

        public static Vector3 ToLocalPosition(Vector3 global) => global - OriginOffset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OriginOffset = Vector3.zero;
            Shifted = null;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneReset()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A retry is a new world. Additive scenery still belongs to the current one.
            if (mode == LoadSceneMode.Single)
            {
                OriginOffset = Vector3.zero;
            }
        }

        private void LateUpdate()
        {
            if (transform.position.sqrMagnitude > Threshold * Threshold)
            {
                Shift(-transform.position);
            }
        }

        public static void Shift(Vector3 offset)
        {
            // Resources also finds inactive roots and the loaded DontDestroyOnLoad scene.
            // Prefab assets have no loaded scene and are deliberately excluded.
            var roots = new List<Transform>();

            foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate.parent == null && candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded)
                {
                    roots.Add(candidate);
                }
            }

            ShiftRoots(roots, offset);
        }

        private static void ShiftRoots(List<Transform> candidates, Vector3 offset)
        {
            if (offset == Vector3.zero || !Finite(offset.x) || !Finite(offset.y) || !Finite(offset.z))
            {
                return;
            }

            var roots = new HashSet<Transform>();

            foreach (Transform root in candidates)
            {
                if (root == null || root.parent != null)
                {
                    continue;
                }

                Canvas canvas = root.GetComponent<Canvas>();
                if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
                {
                    continue;
                }

                roots.Add(root);
            }

            var bodies = new List<BodyState>();
            var particles = new List<ParticleState>();
            var trails = new List<TrailState>();
            var lines = new List<LineState>();

            foreach (Transform root in roots)
            {
                foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    bodies.Add(new BodyState(body));
                }

                foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main;
                    var trailModule = system.trails;
                    if (main.simulationSpace != ParticleSystemSimulationSpace.Local || (trailModule.enabled && trailModule.worldSpace))
                    {
                        particles.Add(new ParticleState(system));
                    }
                }

                foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                {
                    trails.Add(new TrailState(trail));
                }

                foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>(true))
                {
                    if (line.useWorldSpace)
                    {
                        lines.Add(new LineState(line));
                    }
                }
            }

            foreach (Transform root in roots)
            {
                root.position += offset;
            }

            foreach (BodyState body in bodies)
            {
                body.Restore(offset);
            }

            foreach (ParticleState particle in particles)
            {
                particle.Restore(offset);
            }

            foreach (TrailState trail in trails)
            {
                trail.Restore(offset);
            }

            foreach (LineState line in lines)
            {
                line.Restore(offset);
            }

            Physics.SyncTransforms();

            foreach (BodyState body in bodies)
            {
                body.RestoreInterpolation();
            }

            OriginOffset -= offset;
            Game.Combat.RadarIndex.OriginShifted(offset);
            Shifted?.Invoke(offset);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class BodyState
        {
            private readonly Rigidbody body;
            private readonly Vector3 position;
            private readonly Vector3 velocity;
            private readonly Vector3 angularVelocity;
            private readonly Quaternion rotation;
            private readonly RigidbodyInterpolation interpolation;
            private readonly bool sleeping;

            public BodyState(Rigidbody value)
            {
                body = value;
                position = body.position;
                rotation = body.rotation;
                velocity = body.velocity;
                angularVelocity = body.angularVelocity;
                interpolation = body.interpolation;
                sleeping = body.IsSleeping();

                // Clear interpolation's old-origin pose before relocating the physics actor.
                body.interpolation = RigidbodyInterpolation.None;
            }

            public void Restore(Vector3 offset)
            {
                body.position = position + offset;
                body.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = velocity;
                    body.angularVelocity = angularVelocity;
                }
            }

            public void RestoreInterpolation()
            {
                body.interpolation = interpolation;
                if (sleeping)
                {
                    body.Sleep();
                }
            }
        }

        private sealed class ParticleState
        {
            private readonly ParticleSystem system;
            private readonly ParticleSystem.Particle[] particles;
            private readonly int count;
            private readonly ParticleSystemSimulationSpace space;
            private readonly Transform frame;
            private readonly Matrix4x4 oldFrame;
            private readonly bool resetWorldTrails;

            public ParticleState(ParticleSystem value)
            {
                system = value;
                var main = system.main;
                space = main.simulationSpace;
                frame = main.customSimulationSpace != null ? main.customSimulationSpace : system.transform;
                oldFrame = frame.localToWorldMatrix;
                particles = new ParticleSystem.Particle[system.particleCount];
                count = system.GetParticles(particles);
                var trails = system.trails;
                resetWorldTrails = trails.enabled && (trails.worldSpace || space == ParticleSystemSimulationSpace.World);
            }

            public void Restore(Vector3 offset)
            {
                bool customFrameMoved = (frame.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero) - oldFrame.MultiplyPoint3x4(Vector3.zero) - offset).sqrMagnitude < 0.001f;

                for (int particleIndex = 0; particleIndex < count; particleIndex++)
                {
                    if (space == ParticleSystemSimulationSpace.World)
                    {
                        particles[particleIndex].position += offset;
                    }
                    else if (space == ParticleSystemSimulationSpace.Custom)
                    {
                        particles[particleIndex].position = frame.InverseTransformPoint(oldFrame.MultiplyPoint3x4(particles[particleIndex].position) + offset);
                    }
                }

                // Unity 2022 exposes ParticleSystem.Trails only as opaque playback data:
                // its position lists are internal. Clear that unsupported world history
                // while retaining live particles, rather than draw a 2 km trail segment.
                if (resetWorldTrails || (system.trails.enabled && space == ParticleSystemSimulationSpace.Custom && !customFrameMoved))
                {
                    system.Clear(false);
                }

                system.SetParticles(particles, count);
            }
        }

        private sealed class TrailState
        {
            private readonly TrailRenderer trail;
            private readonly Vector3[] positions;

            public TrailState(TrailRenderer value)
            {
                trail = value;
                positions = new Vector3[trail.positionCount];
                trail.GetPositions(positions);
            }

            public void Restore(Vector3 offset)
            {
                for (int trailPointIndex = 0; trailPointIndex < positions.Length; trailPointIndex++)
                {
                    positions[trailPointIndex] += offset;
                }

                trail.SetPositions(positions);
            }
        }

        private sealed class LineState
        {
            private readonly LineRenderer line;
            private readonly Vector3[] positions;

            public LineState(LineRenderer value)
            {
                line = value;
                positions = new Vector3[line.positionCount];
                line.GetPositions(positions);
            }

            public void Restore(Vector3 offset)
            {
                for (int linePointIndex = 0; linePointIndex < positions.Length; linePointIndex++)
                {
                    positions[linePointIndex] += offset;
                }

                line.SetPositions(positions);
            }
        }
    }
}
