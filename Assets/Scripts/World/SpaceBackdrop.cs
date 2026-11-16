using Game.Core;
using UnityEngine;
namespace Game.World
{
    // Skybox and distant planets.
    // Builds the distant skybox, planets and moons that provide movement reference in space.
    public class SpaceBackdrop : MonoBehaviour
    {
        public Material skyboxMaterial;
        public int planetCount = 9;
        public float planetDistance = 360f;
        public float smallestPlanet = 22f;
        public float largestPlanet = 70f;

        // Fixed, so the sky looks the same every run.
        public int randomSeed = 20260818;
        private static readonly string[] PlanetModels =
        {
            "GG-0001-N",
            "GG-0002-N",
            "NL-0001-N",
            "RP-0002-N",
            "SE-0002-N",
            "SE-0003-N",
            "SE-0005-N",
            "SE-0006-N",
            "SE-0009-N"
        };

        private void Start()
        {
            // HDRP's global volume owns the sky. Keep authored skybox references intact for the menu.
            SpaceLighting.Install();
            if (GetComponent<OrbitalEnvironment>() == null)
            {
                gameObject.AddComponent<OrbitalEnvironment>();
            }
        }

        private void ApplySkybox()
        {
            // Prefers the requested high-resolution skybox, with a lower-resolution fallback for missing assets.
            Material sky = skyboxMaterial;
            if (sky == null)
            {
                sky = RenderSettings.skybox;
            }

            if (sky == null)
            {
                return;
            }

            RenderSettings.skybox = sky;
            SpaceLighting.Install();

            // Assigns the global skybox so every camera and reflective material sees the same space background.
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.clearFlags = CameraClearFlags.Skybox;
            }
        }

        private void BuildPlanets()
        {
            // Uses different models, sizes and positions to keep the background from looking repeated.
            int[] order = ShuffledModelOrder();

            for (int orderedItemIndex = 0; orderedItemIndex < planetCount; orderedItemIndex++)
            {
                // Chooses a random direction and size so planets surround the player without forming a grid.
                Vector3 direction = Random.onUnitSphere;
                float size = Random.Range(smallestPlanet, largestPlanet);
                int modelIndex = order[orderedItemIndex % order.Length];
                GameObject planet = AddPlanetModel(transform, "Planet_" + (orderedItemIndex + 1), modelIndex, size);
                float planetSpan = VisualSpan(planet);

                // Measures the final visible size because imported planet models may use different source scales.
                planet.transform.localPosition = direction * planetDistance;
                planet.transform.rotation = Random.rotation;
                SpinMotion spin = planet.AddComponent<SpinMotion>();
                spin.axis = Random.onUnitSphere;
                spin.degreesPerSecond = Random.Range(3f, 10f);
                int moonCount = Random.Range(1, 3);

                for (int moonIndex = 0; moonIndex < moonCount; moonIndex++)
                {
                    // Sizes and spaces moons as a fraction of their parent planet's visible span.
                    float moonSize = planetSpan * Random.Range(0.10f, 0.15f);
                    int moonModel = order[(orderedItemIndex + moonIndex + 3) % order.Length];
                    GameObject moon = AddPlanetModel(transform, "Moon_" + (orderedItemIndex + 1) + "_" + (moonIndex + 1), moonModel, moonSize);
                    OrbitMotion orbit = moon.AddComponent<OrbitMotion>();
                    orbit.pivot = planet.transform;
                    orbit.axis = Random.onUnitSphere;
                    orbit.radius = planetSpan * Random.Range(0.75f, 1.15f);
                    orbit.degreesPerSecond = Random.Range(12f, 28f);
                    orbit.angle = Random.Range(0f, 360f);
                    SpinMotion moonSpin = moon.AddComponent<SpinMotion>();
                    moonSpin.axis = Random.onUnitSphere;
                    moonSpin.degreesPerSecond = Random.Range(8f, 22f);
                }
            }
        }

        private static int[] ShuffledModelOrder()
        {
            // Shuffles model indexes so each run distributes the available planet assets differently.
            int[] order = new int[PlanetModels.Length];

            for (int orderIndex = 0; orderIndex < order.Length; orderIndex++)
            {
                order[orderIndex] = orderIndex;
            }

            for (int entryIndex = order.Length - 1; entryIndex > 0; entryIndex--)
            {
                // Swaps each item with an earlier random item to produce a Fisher-Yates style shuffle.
                int swap = Random.Range(0, entryIndex + 1);
                int temp = order[entryIndex];
                order[entryIndex] = order[swap];
                order[swap] = temp;
            }

            return order;
        }

        private static GameObject AddPlanetModel(Transform parent, string name, int modelIndex, float size)
        {
            // Loads a resource model by ID and falls back to a simple sphere when the asset is unavailable.
            string identifier = PlanetModels[modelIndex % PlanetModels.Length];
            GameObject source = Resources.Load<GameObject>("Planets/" + identifier);
            if (source == null)
            {
                return SpaceSky.AddSphere(parent, name, new Color(0.35f, 0.35f, 0.38f), size);
            }

            GameObject planet = Object.Instantiate(source, parent);
            planet.name = name;
            planet.transform.localScale = Vector3.one;
            float meshSize = VisualSpan(planet);
            if (meshSize < 0.01f)
            {
                meshSize = 2.4f;
            }

            float scale = size / meshSize;

            // Divides desired visible size by imported bounds to normalise differently sized source models.
            planet.transform.localScale = new Vector3(scale, scale, scale);
            Texture2D texture = Resources.Load<Texture2D>("Planets/" + identifier);
            if (texture != null)
            {
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
            }

            Material material = VisualUtil.MakeUnlitTexture(texture);
            if (material == null)
            {
                material = VisualUtil.MakeUnlit(new Color(0.55f, 0.55f, 0.6f, 1f));
            }

            MeshRenderer[] renderers = planet.GetComponentsInChildren<MeshRenderer>(true);

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                // Applies the same unlit texture and disables shadows across every mesh piece in the planet model.
                if (material != null)
                {
                    renderers[rendererIndex].sharedMaterial = material;
                }

                VisualUtil.HideShadows(renderers[rendererIndex]);
            }

            Collider[] colliders = planet.GetComponentsInChildren<Collider>(true);

            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                // Removes colliders because distant background planets should never block player movement or bullets.
                Object.Destroy(colliders[colliderIndex]);
            }

            return planet;
        }

        private static float VisualSpan(GameObject body)
        {
            // Measures combined renderer bounds so moon size and spacing work across imported model scales.
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return 2.4f;
            }

            Bounds bounds = renderers[0].bounds;

            for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            {
                // Expands combined bounds to include all child meshes before calculating the largest visual dimension.
                bounds.Encapsulate(renderers[rendererIndex].bounds);
            }

            Vector3 size = bounds.size;
            float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (span < 0.01f)
            {
                return 2.4f;
            }

            return span;
        }
    }
}
