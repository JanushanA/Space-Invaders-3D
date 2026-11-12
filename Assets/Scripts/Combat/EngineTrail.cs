using Game.Core;
using UnityEngine;
namespace Game.Combat
{
    // Glow and sparks out of the ship's tail. Off while parked.
    // Creates the engine beam and particles that show whether a ship is moving.
    public class EngineTrail : MonoBehaviour
    {
        public Color colour = new Color(0.35f, 0.85f, 1f, 1f);
        public float beamLength = 0.55f;
        private Transform beamRoot;
        private LineRenderer beam;
        private ParticleSystem sparks;
        private Vector3 lastPosition;
        private bool thrusting = true;

        private void OnEnable()
        {
            lastPosition = transform.position;
            Game.World.FloatingOrigin.Shifted += ShiftOrigin;
        }

        private void OnDisable()
        {
            Game.World.FloatingOrigin.Shifted -= ShiftOrigin;
        }

        private void ShiftOrigin(Vector3 offset)
        {
            lastPosition += offset;
        }

        // Finds the ship movement source before deciding when the engine effect should be visible.
        private void Start()
        {
            // Gives non-player ships an orange exhaust so friendly and enemy engines are easier to tell apart.
            if (GetComponent<Player.PlayerController>() == null)
            {
                colour = new Color(1f, 0.45f, 0.15f, 1f);
            }

            // Builds the effect once, hides shadows, and starts with no thrust until the ship moves.
            lastPosition = transform.position;
            PlaceAtTail();
            BuildBeam();
            BuildSparks();
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            {
                VisualUtil.HideShadows(gameObject);
            }

            SetThrust(false);
        }

        // Updates the beam after movement so its position follows the final ship transform for this frame.
        private void LateUpdate()
        {
            // Skips extremely small frame times to avoid dividing movement by almost zero.
            float deltaTime2 = Time.deltaTime;
            if (deltaTime2 < 0.0001f)
            {
                return;
            }

            // Divides distance travelled by frame time to convert this frame's movement into speed.
            float speed = (transform.position - lastPosition).magnitude / deltaTime2;
            lastPosition = transform.position;
            SetThrust(speed > 0.35f);
        }

        // Enables or disables both beam and sparks together so idle ships do not look like they are thrusting.
        private void SetThrust(bool isEnabled)
        {
            // Avoids replaying particle state changes when the requested thrust state is already active.
            if (isEnabled == thrusting)
            {
                return;
            }

            thrusting = isEnabled;

            // Keeps the visible line beam in the same on/off state as the particle exhaust.
            if (beam != null)
            {
                beam.enabled = isEnabled;
            }

            // Stops here when this ship has no sparks component to control.
            if (sparks == null)
            {
                return;
            }

            // Starts emitting while moving and stops new particles when the ship comes to rest.
            if (isEnabled)
            {
                sparks.Play(true);
            }
            else
            {
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // Positions the effect behind the ship in local space so it rotates correctly with the hull.
        private void PlaceAtTail()
        {
            // Starts with a safe default tail position for ships that have no readable mesh bounds.
            BoxCollider hullCollider = GetComponent<BoxCollider>();
            Vector3 localPos = new Vector3(0f, 0f, -1f);
            float length = beamLength;

            // Reads the mesh's local bounds so the exhaust sits just behind differently sized ship models.
            if (hullCollider != null)
            {
                Bounds bounds = new Bounds(hullCollider.center, hullCollider.size);
                float extra = 0.04f / Mathf.Max(0.001f, Mathf.Abs(transform.lossyScale.z));
                localPos = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - extra);

                // Scales the mesh tail length into a clamped beam length so tiny and huge models stay readable.
                float tailSpan = Mathf.Abs(bounds.min.z) * Mathf.Abs(transform.lossyScale.z);
                length = Mathf.Clamp(tailSpan * 0.28f, 0.2f, 0.55f);
                beamLength = length;
            }

            // Parents the beam to the ship so its local tail position follows all hull rotations.
            beamRoot = new GameObject("EngineBeam").transform;
            beamRoot.SetParent(transform, false);
            beamRoot.localPosition = localPos;
            beamRoot.localRotation = Quaternion.identity;
        }

        // Creates the narrow engine beam once and reuses it while the ship moves.
        private void BuildBeam()
        {
            // Draws a two-point line from the engine nozzle backwards along the ship's local Z axis.
            beam = beamRoot.gameObject.AddComponent<LineRenderer>();
            beam.useWorldSpace = false;
            beam.positionCount = 2;
            beam.SetPosition(0, Vector3.zero);
            beam.SetPosition(1, Vector3.back * beamLength);

            // Narrows the beam toward the end and keeps its width reasonable across model scales.
            float width = Mathf.Clamp(0.08f * Mathf.Abs(transform.lossyScale.x) * 4f, 0.03f, 0.1f);
            beam.startWidth = width;
            beam.endWidth = 0.008f;

            // Prefers an additive material for a light-like glow, with an unlit fallback for compatibility.
            beam.material = VisualUtil.MakeAdditive(colour);
            if (beam.material == null)
            {
                beam.material = VisualUtil.MakeUnlit(colour);
            }

            // Fades the far endpoint transparent and disables costly shadow rendering for this visual effect.
            beam.startColor = colour;
            beam.endColor = new Color(colour.r, colour.g, colour.b, 0f);
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.numCapVertices = 2;
        }

        // Creates small particle sparks that make the engine thrust feel less static.
        private void BuildSparks()
        {
            // Creates a particle object beneath the beam so sparks always start at the engine nozzle.
            GameObject sparkObject = new GameObject("EngineSparks");
            sparkObject.transform.SetParent(beamRoot, false);
            sparkObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sparks = sparkObject.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Configures short-lived world-space particles so existing sparks continue behind a moving ship.
            ParticleSystem.MainModule main = sparks.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 28;
            main.gravityModifier = 0f;

            // Emits a steady stream while thrust is enabled.
            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.rateOverTime = 18f;

            // Spreads sparks through a narrow cone pointing out of the ship's tail.
            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.02f;

            // Blends from the engine colour to white while fading alpha to make particles disappear smoothly.
            ParticleSystem.ColorOverLifetimeModule tint = sparks.colorOverLifetime;
            tint.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(colour, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            tint.color = gradient;

            // Uses an additive billboard material and disables shadows because sparks are light effects.
            ParticleSystemRenderer renderer = sparkObject.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(colour);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            VisualUtil.HideShadows(renderer);
        }
    }
}
