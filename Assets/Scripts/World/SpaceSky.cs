using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;
namespace Game.World
{
    // Distant sky objects: galaxies, giant stars, pulsars, quasars, black holes.
    // Creates distant galaxies and celestial bodies used to fill the space background.
    public static class SpaceSky
    {
        public static void Build(Transform root)
        {
            // Builds each celestial category separately so the background has variety without gameplay colliders.
            BuildGalaxies(root, 3);
            BuildGiantStars(root, 4);
            BuildPulsars(root, 2);
            BuildQuasars(root, 2);
            BuildBlackHoles(root, 2);
        }

        private static void BuildGalaxies(Transform root, int count)
        {
            // Places large flat particle discs far away so galaxies feel enormous without expensive meshes.
            for (int galaxyIndex = 0; galaxyIndex < count; galaxyIndex++)
            {
                Vector3 position2 = Random.onUnitSphere * Random.Range(520f, 760f);
                Color armColor = BrightColour();
                Color core = Color.Lerp(Color.white, new Color(1f, 0.9f, 0.55f), 0.6f);
                GameObject galaxy = new GameObject("Galaxy_" + (galaxyIndex + 1));
                galaxy.transform.SetParent(root, false);
                galaxy.transform.position = position2;
                galaxy.transform.rotation = Random.rotation;
                float span = Random.Range(70f, 120f);
                galaxy.transform.localScale = new Vector3(span, span * 0.12f, span);
                AddDiscParticles(galaxy.transform, "Arms", armColor, 280, 1.1f, 0.9f, 1.6f);
                AddSphere(galaxy.transform, "Core", core, 0.12f);
                ParticleSystem glow = AddBurstParticles(galaxy.transform, "CoreGlow", core, 40, 0.4f, 0.8f);
                ParticleSystem.ShapeModule glowShape = glow.shape;
                glowShape.shapeType = ParticleSystemShapeType.Sphere;
                glowShape.radius = 0.08f;
                SpinMotion spin = galaxy.AddComponent<SpinMotion>();
                spin.axis = Vector3.up;
                spin.degreesPerSecond = Random.Range(1.5f, 4f);
            }
        }

        private static void BuildGiantStars(Transform root, int count)
        {
            // Combines a solid core, corona particles and stretched flares to create a bright star silhouette.
            for (int starIndex = 0; starIndex < count; starIndex++)
            {
                Vector3 position2 = Random.onUnitSphere * Random.Range(210f, 340f);
                Color star = Color.Lerp(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.55f, 0.15f), Random.value);
                float size = Random.Range(18f, 36f);
                GameObject body = AddSphere(root, "GiantStar_" + (starIndex + 1), star, size);
                body.transform.position = position2;
                ParticleSystem corona = AddConeParticles(body.transform, "Corona", star, 35, 0.8f, 6f, 14f, 55f);
                ParticleSystem.ShapeModule coronaShape = corona.shape;
                coronaShape.shapeType = ParticleSystemShapeType.Sphere;
                coronaShape.radius = 0.55f;
                ParticleSystem flare = AddConeParticles(body.transform, "Flares", Color.Lerp(star, Color.white, 0.4f), 18, 0.35f, 10f, 22f, 18f);
                ParticleSystem.ShapeModule flareShape = flare.shape;
                flareShape.shapeType = ParticleSystemShapeType.Hemisphere;
                flareShape.radius = 0.5f;
                SpinMotion spin = body.AddComponent<SpinMotion>();
                spin.axis = Random.onUnitSphere;
                spin.degreesPerSecond = Random.Range(8f, 18f);
            }
        }

        private static void BuildPulsars(Transform root, int count)
        {
            // Creates a bright rotating core with two opposing beams to represent a pulsar.
            for (int pulsarIndex = 0; pulsarIndex < count; pulsarIndex++)
            {
                Vector3 position2 = Random.onUnitSphere * Random.Range(170f, 280f);
                GameObject pulsar = AddSphere(root, "Pulsar_" + (pulsarIndex + 1), Color.white, Random.Range(4f, 7f));
                pulsar.transform.position = position2;
                pulsar.transform.rotation = Random.rotation;
                Color beamColour = new Color(0.7f, 0.85f, 1f);
                AddJet(pulsar.transform, "BeamA", beamColour, 40f, Vector3.up);
                AddJet(pulsar.transform, "BeamB", beamColour, 40f, Vector3.down);
                ParticleSystem sparks = AddBurstParticles(pulsar.transform, "Pulse", Color.white, 24, 0.2f, 2.5f);
                ParticleSystem.EmissionModule emission = sparks.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new ParticleSystem.Burst[]
                {
                    new ParticleSystem.Burst(0f, (short)16, (short)16, 0, 0.35f)
                });
                SpinMotion spin = pulsar.AddComponent<SpinMotion>();
                spin.axis = Vector3.right;
                spin.degreesPerSecond = Random.Range(70f, 140f);
            }
        }

        private static void BuildQuasars(Transform root, int count)
        {
            // Uses longer opposing jets and a halo to visually distinguish quasars from pulsars.
            for (int quasarIndex = 0; quasarIndex < count; quasarIndex++)
            {
                Vector3 position2 = Random.onUnitSphere * Random.Range(380f, 620f);
                Color core = new Color(1f, 0.85f, 1f);
                Color jetColor = new Color(0.55f, 0.75f, 1f);
                float size = Random.Range(10f, 16f);
                GameObject quasar = AddSphere(root, "Quasar_" + (quasarIndex + 1), core, size);
                quasar.transform.position = position2;
                quasar.transform.rotation = Random.rotation;
                AddJet(quasar.transform, "JetA", jetColor, 90f, Vector3.up);
                AddJet(quasar.transform, "JetB", jetColor, 90f, Vector3.down);
                AddBurstParticles(quasar.transform, "Halo", core, 30, 0.5f, 1.2f);
            }
        }

        private static void BuildBlackHoles(Transform root, int count)
        {
            // Builds a dark core, spinning accretion disc and inward-moving particles for black holes.
            for (int blackHoleIndex = 0; blackHoleIndex < count; blackHoleIndex++)
            {
                Vector3 position2 = Random.onUnitSphere * Random.Range(200f, 380f);
                float size = Random.Range(12f, 22f);
                GameObject hole = AddSphere(root, "BlackHole_" + (blackHoleIndex + 1), new Color(0.04f, 0.04f, 0.05f), size);
                hole.transform.position = position2;
                hole.transform.rotation = Random.rotation;
                GameObject disc = new GameObject("Accretion");
                disc.transform.SetParent(hole.transform, false);
                disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ParticleSystem ring = AddDiscParticles(disc.transform, "Disc", new Color(1f, 0.55f, 0.15f), 160, 1.8f, 0.15f, 0.45f);
                ParticleSystem.MainModule ringMain = ring.main;
                ringMain.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 0.35f, 0.05f),
                    new Color(1f, 0.9f, 0.4f));
                ParticleSystem infall = AddConeParticles(hole.transform, "Infall", new Color(1f, 0.4f, 0.1f), 22, 0.2f, 2f, 6f, 25f);
                ParticleSystem.ShapeModule infallShape = infall.shape;
                infallShape.shapeType = ParticleSystemShapeType.Sphere;
                infallShape.radius = 2.2f;
                ParticleSystem.VelocityOverLifetimeModule pull = infall.velocityOverLifetime;
                pull.enabled = true;
                pull.radial = new ParticleSystem.MinMaxCurve(-6f);

                // A negative radial velocity pulls particles inward, making the material appear to fall into the hole.
                SpinMotion spin = hole.AddComponent<SpinMotion>();
                spin.axis = Vector3.up;
                spin.degreesPerSecond = Random.Range(12f, 28f);
            }
        }

        public static GameObject AddSphere(Transform parent, string name, Color colour, float size)
        {
            // Creates simple unlit decorative geometry and removes its collider because space scenery is non-interactive.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = name;
            body.transform.SetParent(parent, false);
            body.transform.localScale = new Vector3(size, size, size);
            Collider collider = body.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            MeshRenderer renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MakeUnlit(colour);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return body;
        }

        public static Color BrightColour()
        {
            // Selects full-wheel hues while keeping saturation and value high enough to show against black space.
            float hueFraction = Random.value;
            return Color.HSVToRGB(hueFraction, Random.Range(0.5f, 1f), Random.Range(0.7f, 1f));
        }

        public static ParticleSystem AddDiscParticles(Transform parent, string name, Color colour, int burst, float radius, float minSize, float maxSize)
        {
            // Configures a local circular particle field used for galaxy arms and accretion discs.
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            ParticleSystem particleSystem = host.AddComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 8f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = burst;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = burst / 8f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)burst) });

            // Emits the entire disc immediately, then uses normal emission to replace particles as they expire.
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.65f;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(colour);
            VisualUtil.HideShadows(renderer);
            particleSystem.Play(true);
            return particleSystem;
        }

        public static ParticleSystem AddConeParticles(
            Transform parent,
            string name,
            Color colour,
            float rate,
            float size,
            float minSpeed,
            float maxSpeed,
            float angle)
        {
            // Configures a cone emitter for stars, flares and jets with a controlled speed range and fade.
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            ParticleSystem particleSystem = host.AddComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = size;
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 80;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = rate;
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.15f;
            ParticleSystem.ColorOverLifetimeModule tint = particleSystem.colorOverLifetime;
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

            // Fades particle alpha to zero over life so bright effects dissolve instead of stopping abruptly.
            tint.color = gradient;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(colour);
            VisualUtil.HideShadows(renderer);
            particleSystem.Play(true);
            return particleSystem;
        }

        public static ParticleSystem AddBurstParticles(Transform parent, string name, Color colour, int count, float size, float speed)
        {
            // Creates a compact spherical particle effect used as a continuous glow or repeating pulse.
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            ParticleSystem particleSystem = host.AddComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 0.8f;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = count;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = count * 0.4f;
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(colour);
            VisualUtil.HideShadows(renderer);
            particleSystem.Play(true);
            return particleSystem;
        }

        private static void AddJet(Transform parent, string name, Color colour, float length, Vector3 localDir)
        {
            // Rotates a cone emitter toward the requested local direction to form one side of a two-way jet.
            GameObject jetObject = new GameObject(name);
            jetObject.transform.SetParent(parent, false);
            jetObject.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, localDir);
            ParticleSystem particleSystem = AddConeParticles(jetObject.transform, "Stream", colour, 28, 0.25f, length * 0.4f, length, 6f);
            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;

            // Stretches fast jet particles so the beam looks continuous at a distance.
            renderer.lengthScale = 4f;
            renderer.velocityScale = 0.15f;
        }

        private static Material MakeUnlit(Color colour)
        {
            return VisualUtil.MakeUnlit(colour);
        }
    }

    // Rotates a decorative space object at a fixed rate.
    public class SpinMotion : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 20f;

        private void Update()
        {
            // Rotates decorative objects by degrees per second, scaled by delta time for frame-rate independence.
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }

    // Moves a moon around a planet using a circular sine and cosine path.
    public class OrbitMotion : MonoBehaviour
    {
        public Transform pivot;
        public Vector3 axis = Vector3.up;
        public float radius = 8f;
        public float degreesPerSecond = 18f;
        public float angle;

        private void Update()
        {
            // Advances an orbit angle and rotates a radius vector around the pivot to produce circular moon movement.
            if (pivot == null)
            {
                return;
            }

            angle += degreesPerSecond * Time.deltaTime;
            Quaternion rotation = Quaternion.AngleAxis(angle, axis);
            transform.position = pivot.position + rotation * (Vector3.right * radius);

            // Rotating the radius vector creates a position on the orbit circle around the planet pivot.
        }
    }

    // Sends occasional asteroids past the player to add depth without overcrowding space.
    public class NearAsteroidField : MonoBehaviour
    {
        public int count = 6;
        public float range = 38f;
        private Transform player;
        private Transform[] rocks;
        private Vector3[] velocities;

        private void Start()
        {
            // Creates a small number of nearby asteroids and records one velocity for each reusable transform.
            GameObject found = GameObject.FindWithTag("Player");
            if (found != null)
            {
                player = found.transform;
            }

            rocks = new Transform[count];
            velocities = new Vector3[count];
            Vector3 origin = Vector3.zero;
            if (player != null)
            {
                origin = player.position;
            }

            for (int asteroidIndex = 0; asteroidIndex < count; asteroidIndex++)
            {
                // Randomises rock size, starting location, spin and velocity to avoid a repeated particle-field look.
                Color colour = Color.Lerp(new Color(0.42f, 0.42f, 0.45f), new Color(0.58f, 0.57f, 0.55f), Random.value);
                float size = Random.Range(0.25f, 0.9f);
                GameObject rock = SpaceSky.AddSphere(transform, "Asteroid_" + (asteroidIndex + 1), colour, size);
                rock.transform.position = origin + Random.insideUnitSphere * range;
                rock.transform.rotation = Random.rotation;
                AddAsteroidTrail(rock.transform);
                SpinMotion spin = rock.AddComponent<SpinMotion>();
                spin.axis = Random.onUnitSphere;
                spin.degreesPerSecond = Random.Range(20f, 70f);
                rocks[asteroidIndex] = rock.transform;
                velocities[asteroidIndex] = Random.onUnitSphere * Random.Range(3.5f, 11f);
            }
        }

        private void Update()
        {
            // Moves rocks in world space, then wraps any that leave the nearby cube back to the opposite side.
            if (player == null)
            {
                GameObject found = GameObject.FindWithTag("Player");
                if (found != null)
                {
                    player = found.transform;
                }
            }

            if (player == null || rocks == null)
            {
                return;
            }

            Vector3 origin = player.position;
            float limit = range * 1.15f;

            for (int asteroidIndex = 0; asteroidIndex < rocks.Length; asteroidIndex++)
            {
                if (rocks[asteroidIndex] == null)
                {
                    continue;
                }

                rocks[asteroidIndex].position += velocities[asteroidIndex] * Time.deltaTime;

                // Multiplies velocity by delta time so asteroid movement remains stable across frame rates.
                Vector3 offset = rocks[asteroidIndex].position - origin;
                if (offset.x > limit)
                {
                    // Wraps each axis separately to recycle asteroids without spawning or destroying new objects.
                    offset.x -= limit * 2f;
                }
                else if (offset.x < -limit)
                {
                    offset.x += limit * 2f;
                }

                if (offset.y > limit)
                {
                    offset.y -= limit * 2f;
                }
                else if (offset.y < -limit)
                {
                    offset.y += limit * 2f;
                }

                if (offset.z > limit)
                {
                    offset.z -= limit * 2f;
                }
                else if (offset.z < -limit)
                {
                    offset.z += limit * 2f;
                }

                rocks[asteroidIndex].position = origin + offset;
            }
        }

        private static void AddAsteroidTrail(Transform rock)
        {
            // Adds a red-orange inherited-velocity trail so grey asteroids read as fast-moving objects.
            GameObject host = new GameObject("Trail");
            host.transform.SetParent(rock, false);
            ParticleSystem particleSystem = host.AddComponent<ParticleSystem>();
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.25f, 0.05f),
                new Color(1f, 0.65f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.gravityModifier = 0f;
            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = 22f;
            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;
            ParticleSystem.ColorOverLifetimeModule tint = particleSystem.colorOverLifetime;
            tint.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0f),
                    new GradientColorKey(new Color(0.9f, 0.15f, 0.05f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            tint.color = gradient;
            ParticleSystem.InheritVelocityModule inherit = particleSystem.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Current;
            inherit.curve = new ParticleSystem.MinMaxCurve(0.35f);

            // Copies part of the asteroid velocity into particles so the trail naturally follows its direction.
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.material = VisualUtil.MakeAdditive(new Color(1f, 0.4f, 0.08f));
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4.5f;
            renderer.velocityScale = 0.12f;
            VisualUtil.HideShadows(renderer);
            particleSystem.Play(true);
        }
    }
}
