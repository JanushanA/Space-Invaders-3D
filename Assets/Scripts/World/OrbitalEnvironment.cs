using System.Collections.Generic;
using Game.Graphics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Builds each sector's planet, station and asteroid scenery from reusable materials and generated meshes.
namespace Game.World
{
    // Scenery is transform-relative; physical asteroid colliders shift with the root.
    public sealed class OrbitalEnvironment : MonoBehaviour
    {
        public int seed = 81511;
        private readonly List<Object> owned = new List<Object>();
        private GameObject scenery;
        private Material atmosphere;
        private Transform clouds;
        private Light sun;
        private System.Random random;

        private void Start()
        {
            if (scenery != null)
            {
                return;
            }

            seed += Game.Core.GameSession.SelectedLevel * 211;

            random = new System.Random(seed); // Does not consume Unity's gameplay random stream.
            scenery = new GameObject("Orbital environment");
            scenery.transform.SetParent(transform, false);

            foreach (Light candidate in FindObjectsOfType<Light>())
            {
                if (candidate.type == LightType.Directional && (sun == null || candidate.intensity > sun.intensity))
                {
                    sun = candidate;
                }
            }

            BuildPlanet();
            BuildStation();
            BuildAsteroids();
        }

        private void Update()
        {
            if (clouds != null)
            {
                clouds.Rotate(Vector3.up, Time.deltaTime * 0.025f, Space.Self);
            }

            if (atmosphere != null && sun != null)
            {
                atmosphere.SetVector("_SunDirection", -sun.transform.forward);
            }
        }

        private void OnDestroy()
        {
            if (scenery != null)
            {
                Release(scenery);
            }

            foreach (Object value in owned)
            {
                if (value != null)
                {
                    Release(value);
                }
            }
        }

        private static void Release(Object value)
        {
            if (Application.isPlaying)
            {
                Destroy(value);
            }
            else
            {
                DestroyImmediate(value);
            }
        }

        private T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        private float Range(float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private void BuildPlanet()
        {
            int level = Mathf.Clamp(Game.Core.GameSession.SelectedLevel, 1, 8);
            var root = new GameObject("Campaign planet " + level);
            root.transform.SetParent(scenery.transform, false);
            root.transform.localPosition = new Vector3(1400, -1800, 2800);
            root.transform.localRotation = Quaternion.Euler(17, -28, -12);
            Mesh sphere = Own(Sphere("Planet sphere", 128, 64, 0));
            Material terrain = Own(GraphicsMaterials.Lit("Aster terrain", Color.white, 0.08f, 0.38f));
            terrain.SetTexture("_BaseColorMap", Resources.Load<Texture2D>(level == 1 ? "Orbital/PlanetTerrain" : "Orbital/Planet" + level));
            GameObject body = Render(root.transform, "Terrain and ocean", sphere, terrain);
            body.transform.localScale = Vector3.one * 1800f;
            Material cloudMaterial = Own(GraphicsMaterials.Lit("Aster cloud layer", Color.white, 0f, 0.12f));
            cloudMaterial.SetTexture("_BaseColorMap", Resources.Load<Texture2D>("Orbital/PlanetClouds"));
            cloudMaterial.SetFloat("_SurfaceType", 1f);
            cloudMaterial.SetFloat("_BlendMode", 0f);
            cloudMaterial.SetFloat("_ZWrite", 0f);
            cloudMaterial.SetFloat("_TransparentZWrite", 0f);
            cloudMaterial.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f);
            HDMaterial.ValidateMaterial(cloudMaterial);
            clouds = Render(root.transform, "High altitude cloud bands", sphere, cloudMaterial).transform;
            clouds.localScale = Vector3.one * 1807f;
            clouds.gameObject.SetActive(level == 1 || level == 3 || level == 5 || level == 8);
            atmosphere = Own(new Material(Resources.Load<Shader>("Shaders/AtmosphereRim")) { name = "Aster atmospheric scattering" });
            atmosphere.SetColor("_Color", new Color(0.12f, 0.32f, 0.62f, 0.68f));
            Color[] atmosphereColors = { new Color(0.12f, 0.32f, 0.62f, 0.68f), new Color(0.6f, 0.19f, 0.07f, 0.55f), new Color(0.3f, 0.7f, 0.85f, 0.6f), new Color(0.5f, 0.08f, 0.015f, 0.35f), new Color(0.65f, 0.42f, 0.22f, 0.6f), new Color(0.3f, 0.45f, 0.65f, 0.35f), new Color(0.25f, 0.12f, 0.4f, 0.2f), new Color(0.03f, 0.4f, 0.36f, 0.65f) };
            atmosphere.SetColor("_Color", atmosphereColors[level - 1]);
            if (sun != null)
            {
                atmosphere.SetVector("_SunDirection", -sun.transform.forward);
            }

            Render(root.transform, "Thin atmospheric limb", sphere, atmosphere).transform.localScale = Vector3.one * 1830f;
            if (level == 6)
            {
                BuildRings(root.transform);
            }

            if (level == 7)
            {
                var moon = Render(scenery.transform, "Eclipse moon", sphere, Own(GraphicsMaterials.Lit("Eclipse basalt", new Color(0.025f, 0.025f, 0.035f), 0.2f, 0.2f)));
                moon.transform.localPosition = new Vector3(650, -450, 1600);
                moon.transform.localScale = Vector3.one * 580;
            }
        }

        void BuildRings(Transform planet)
        {
            var ringMesh = new Mesh();
            var vertices = new List<Vector3>();
            var triangleIndices = new List<int>();

            for (int ringSegmentIndex = 0; ringSegmentIndex <= 180; ringSegmentIndex++)
            {
                float angleRadians = ringSegmentIndex * Mathf.PI * 2 / 180;
                vertices.Add(new Vector3(Mathf.Cos(angleRadians) * 2300, 0, Mathf.Sin(angleRadians) * 2300));
                vertices.Add(new Vector3(Mathf.Cos(angleRadians) * 3200, 0, Mathf.Sin(angleRadians) * 3200));
                if (ringSegmentIndex < 180)
                {
                    int vertexIndex = ringSegmentIndex * 2;
                    triangleIndices.AddRange(new[] { vertexIndex, vertexIndex + 2, vertexIndex + 1, vertexIndex + 1, vertexIndex + 2, vertexIndex + 3, vertexIndex + 1, vertexIndex + 2, vertexIndex, vertexIndex + 3, vertexIndex + 2, vertexIndex + 1 });
                }
            }

            ringMesh.SetVertices(vertices);
            ringMesh.SetTriangles(triangleIndices, 0);
            ringMesh.RecalculateNormals();
            Own(ringMesh);
            var rings = Render(planet, "Icy orbital rings", ringMesh, Own(GraphicsMaterials.Lit("Ring ice", new Color(0.4f, 0.38f, 0.3f), 0.1f, 0.15f)));
            rings.transform.localRotation = Quaternion.Euler(18, 0, 14);
        }

        private void BuildStation()
        {
            var root = new GameObject("Orbital transfer station");
            root.transform.SetParent(scenery.transform, false);
            root.transform.localPosition = new Vector3(-240, 140, 490);
            root.transform.localRotation = Quaternion.Euler(16, -24, 13);
            var hull = new Batch();
            var dark = new Batch();
            var solar = new Batch();
            var lamps = new Batch();

            // Segmented docking torus, hub, spoke trusses and six distinct pressurised service modules.
            const int segments = 24;

            for (int stationSegmentIndex = 0; stationSegmentIndex < segments; stationSegmentIndex++)
            {
                float angle = stationSegmentIndex * 360f / segments;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0);
                Vector3 centre = radial * 68;
                Quaternion rotation = Quaternion.Euler(0, 0, angle + 90);
                hull.Box(centre, new Vector3(15.5f, 6, 11), rotation);
                dark.Box(centre + Vector3.back * 5.8f, new Vector3(13, 3.8f, 1), rotation);
                lamps.Box(centre + Vector3.back * 6.35f, new Vector3(6, 0.25f, 0.3f), rotation);
                if (stationSegmentIndex % 4 != 0)
                {
                    continue;
                }

                hull.Beam(radial * 15 + Vector3.forward * 12, centre + Vector3.forward * 5, 2.3f);
                hull.Beam(radial * 18 + Vector3.back * 11, centre + Vector3.back * 3, 1.2f);
                Vector3 tangent = new Vector3(-radial.y, radial.x, 0);

                for (int comparisonIndex = 0; comparisonIndex < 4; comparisonIndex++)
                {
                    float start = 20 + comparisonIndex * 10;
                    hull.Beam(radial * start + tangent * 3 + Vector3.forward * 4,
                        radial * (start + 10) - tangent * 3 + Vector3.forward * 4, 0.65f);
                }

                hull.Box(radial * 85, new Vector3(13, 27, 19), rotation);
                dark.Box(radial * 85 + Vector3.back * 9.7f, new Vector3(9, 18, 0.7f), rotation);
                hull.Box(radial * 89 + Vector3.forward * 15, new Vector3(8, 16, 10), rotation);
            }

            hull.Box(Vector3.zero, new Vector3(24, 24, 55), Quaternion.Euler(0, 0, 45));
            dark.Box(new Vector3(0, 0, -28), new Vector3(17, 17, 2), Quaternion.Euler(0, 0, 45));
            hull.Beam(new Vector3(0, 0, 23), new Vector3(0, 0, 95), 4);
            hull.Box(new Vector3(0, 0, 82), new Vector3(12, 10, 18), Quaternion.identity);

            // Long photovoltaic wings have individually framed cell blankets and service booms.
            for (int side = -1; side <= 1; side += 2)
            {
                hull.Beam(new Vector3(side * 20, 0, 28), new Vector3(side * 158, 0, 28), 2);
                hull.Beam(new Vector3(side * 30, 0, 13), new Vector3(side * 110, 0, 28), 1);

                for (int bayIndex = 0; bayIndex < 4; bayIndex++)
                {
                    for (int row = -1; row <= 1; row += 2)
                    {
                        Vector3 position = new Vector3(side * (93 + bayIndex * 20), row * 24, 28);
                        hull.Box(position, new Vector3(18, 43, 0.8f), Quaternion.identity);
                        solar.Box(position + Vector3.back * 0.6f, new Vector3(16.8f, 41.8f, 0.3f), Quaternion.identity);
                        hull.Beam(new Vector3(position.x, 0, 28), new Vector3(position.x, row * 46, 28), 0.45f);
                    }
                }

                hull.Beam(new Vector3(side * 12, 12, 24), new Vector3(side * 14, 49, 39), 0.65f);
                lamps.Box(new Vector3(side * 14, 49, 39), Vector3.one * 1.2f, Quaternion.identity);
            }

            Material hullMaterial = Own(GraphicsMaterials.Lit("Station ceramic alloy", new Color(0.18f, 0.21f, 0.24f), 0.55f, 0.32f));
            Material darkMaterial = Own(GraphicsMaterials.Lit("Station recesses", new Color(0.035f, 0.045f, 0.053f), 0.75f, 0.32f));
            Material panelMaterial = Own(GraphicsMaterials.Lit("Station solar blankets", new Color(0.22f, 0.28f, 0.35f), 0.45f, 0.42f));
            panelMaterial.SetTexture("_BaseColorMap", Resources.Load<Texture2D>("Orbital/SolarCells"));
            Render(root.transform, "Pressure hull and trusses", Own(hull.Mesh("Station hull")), hullMaterial);
            Render(root.transform, "Docking ports and panel recesses", Own(dark.Mesh("Station recesses")), darkMaterial);
            Render(root.transform, "Photovoltaic arrays", Own(solar.Mesh("Station solar cells")), panelMaterial);
            Render(root.transform, "Docking guidance lights", Own(lamps.Mesh("Station lamps")), Own(GraphicsMaterials.Unlit("Station warm navigation", new Color(1.8f, 0.8f, 0.24f), true)));
        }

        private void BuildAsteroids()
        {
            Material stone = Own(GraphicsMaterials.Lit("Carbonaceous regolith", new Color(0.7f, 0.68f, 0.64f), 0.08f, 0.19f));
            stone.SetTexture("_BaseColorMap", Resources.Load<Texture2D>("Orbital/RockSurface"));
            Mesh[] near = new Mesh[6];
            Mesh[] distantMeshes = new Mesh[6];

            for (int meshVariantIndex = 0; meshVariantIndex < 6; meshVariantIndex++)
            {
                near[meshVariantIndex] = Own(Sphere("Asteroid detailed " + meshVariantIndex, 32, 20, meshVariantIndex + 1));
                distantMeshes[meshVariantIndex] = Own(Sphere("Asteroid distant " + meshVariantIndex, 12, 8, meshVariantIndex + 1));
            }

            int missionSeed = Game.Campaign.CampaignSession.Seed != 0 ? Game.Campaign.CampaignSession.Seed : 81511 + Game.Core.GameSession.SelectedLevel * 101;
            var navigation = Game.Campaign.SectorGenerator.Generate(missionSeed, Game.Core.GameSession.SelectedLevel);

            for (int asteroidIndex = 0; asteroidIndex < 48; asteroidIndex++)
            {
                float angle = Range(0, Mathf.PI * 2);
                float distance = Range(110, 360);
                var rock = new GameObject("Drifting rock " + asteroidIndex.ToString("00"));
                rock.transform.SetParent(scenery.transform, false);
                rock.transform.localPosition = new Vector3(Mathf.Cos(angle) * distance, Range(-95, 100), Mathf.Sin(angle) * distance);
                rock.transform.localRotation = Quaternion.Euler(Range(0, 360), Range(0, 360), Range(0, 360));
                float size = Range(2.5f, 13);
                rock.transform.localScale = new Vector3(size * Range(0.75f, 1.3f), size * Range(0.7f, 1.2f), size);

                // Keep objective interaction volumes reachable even when decorative scenery starts first.
                float clearance = size * 1.6f + 18;
                bool reserve = (rock.transform.localPosition - navigation.Position(navigation.start)).magnitude < clearance;

                foreach (int cell in navigation.objectives)
                {
                    reserve |= (rock.transform.localPosition - navigation.Position(cell)).magnitude < clearance;
                }

                foreach (int cell in navigation.relays)
                {
                    reserve |= (rock.transform.localPosition - navigation.Position(cell)).magnitude < clearance;
                }

                if (reserve)
                {
                    var position = rock.transform.localPosition;
                    position.y = clearance + 12;
                    rock.transform.localPosition = position;
                }

                MeshRenderer closeRenderer = Render(rock.transform, "Detailed rock", near[asteroidIndex % 6], stone).GetComponent<MeshRenderer>();
                MeshRenderer distantRenderer = Render(rock.transform, "Distant rock", distantMeshes[asteroidIndex % 6], stone).GetComponent<MeshRenderer>();
                rock.layer = LayerMask.NameToLayer(Game.Core.GameLayers.LevelGeometry);
                var collider = rock.AddComponent<MeshCollider>();
                collider.sharedMesh = distantMeshes[asteroidIndex % 6];
                collider.convex = true;
                var detailLevels = rock.AddComponent<LODGroup>();
                detailLevels.SetLODs(new[] { new LOD(0.10f, new Renderer[] { closeRenderer }), new LOD(0.006f, new Renderer[] { distantRenderer }) });
                detailLevels.RecalculateBounds();
            }
        }

        private static GameObject Render(Transform parent, string name, Mesh mesh, Material material)
        {
            var sceneObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            sceneObject.transform.SetParent(parent, false);
            sceneObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = sceneObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return sceneObject;
        }

        public static Mesh NavigationAsteroidMesh(int variant)
        {
            var mesh = Sphere("Navigation regolith " + variant, 24, 16, 1 + Mathf.Abs(variant % 6));

            // Fit the existing radius-0.5 navigation collider without changing route clearance.
            var vertices = mesh.vertices;

            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                vertices[vertexIndex] *= 0.415f;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Sphere(string name, int longitude, int latitude, int rockVariant)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var textureCoordinates = new List<Vector2>();
            var indices = new List<int>();

            for (int latitudeIndex = 0; latitudeIndex <= latitude; latitudeIndex++)
            {
                for (int longitudeIndex = 0; longitudeIndex <= longitude; longitudeIndex++)
                {
                    float longitudeFraction = (float)longitudeIndex / longitude;
                    float latitudeFraction = (float)latitudeIndex / latitude;
                    float longitudeRadians = longitudeFraction * Mathf.PI * 2;
                    float theta = latitudeFraction * Mathf.PI;
                    Vector3 direction = new Vector3(Mathf.Sin(theta) * Mathf.Cos(longitudeRadians), -Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(longitudeRadians));
                    float radius = 1;
                    if (rockVariant > 0)
                    {
                        float offset = rockVariant * 11.17f;
                        float surfaceNoise = Noise(direction * 2.3f, offset);
                        float fine = Noise(direction * 9, offset + 5);
                        radius = 0.77f + surfaceNoise * 0.36f + fine * 0.065f;
                        Vector3 crater = new Vector3(Mathf.Sin(offset), 0.3f, Mathf.Cos(offset)).normalized;
                        float distance = Vector3.Distance(direction, crater);
                        radius -= Mathf.Exp(-distance * distance * 28) * 0.13f;
                    }

                    vertices.Add(direction * radius);
                    normals.Add(direction);
                    textureCoordinates.Add(new Vector2(longitudeFraction, latitudeFraction));
                    if (longitudeIndex == longitude || latitudeIndex == latitude)
                    {
                        continue;
                    }

                    int quadStartIndex = latitudeIndex * (longitude + 1) + longitudeIndex;
                    indices.Add(quadStartIndex);
                    indices.Add(quadStartIndex + longitude + 1);
                    indices.Add(quadStartIndex + 1);
                    indices.Add(quadStartIndex + 1);
                    indices.Add(quadStartIndex + longitude + 1);
                    indices.Add(quadStartIndex + longitude + 2);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, textureCoordinates);
            mesh.SetTriangles(indices, 0);
            if (rockVariant > 0)
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Noise(Vector3 samplePosition, float offset)
        {
            return (Mathf.PerlinNoise(samplePosition.x + offset, samplePosition.y + offset) + Mathf.PerlinNoise(samplePosition.y + offset, samplePosition.z + offset) + Mathf.PerlinNoise(samplePosition.z + offset, samplePosition.x + offset)) / 3;
        }

        private sealed class Batch
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            public void Beam(Vector3 startPosition, Vector3 endPosition, float thickness)
            {
                Box((startPosition + endPosition) * 0.5f, new Vector3(thickness, thickness, Vector3.Distance(startPosition, endPosition)), Quaternion.LookRotation(endPosition - startPosition));
            }

            public void Box(Vector3 centre, Vector3 size, Quaternion rotation)
            {
                Vector3[] corners = new Vector3[8];

                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    corners[cornerIndex] = centre + rotation * Vector3.Scale(new Vector3((cornerIndex & 1) == 0 ? -0.5f : 0.5f, (cornerIndex & 2) == 0 ? -0.5f : 0.5f, (cornerIndex & 4) == 0 ? -0.5f : 0.5f), size);
                }

                Quad(corners[0], corners[2], corners[3], corners[1]);
                Quad(corners[4], corners[5], corners[7], corners[6]);
                Quad(corners[0], corners[1], corners[5], corners[4]);
                Quad(corners[2], corners[6], corners[7], corners[3]);
                Quad(corners[0], corners[4], corners[6], corners[2]);
                Quad(corners[1], corners[3], corners[7], corners[5]);
            }

            private void Quad(Vector3 firstPoint, Vector3 secondPoint, Vector3 thirdPoint, Vector3 fourthPoint)
            {
                int start = vertices.Count;
                vertices.Add(firstPoint);
                vertices.Add(secondPoint);
                vertices.Add(thirdPoint);
                vertices.Add(fourthPoint);
                uv.Add(Vector2.zero);
                uv.Add(Vector2.up);
                uv.Add(Vector2.one);
                uv.Add(Vector2.right);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }

            public Mesh Mesh(string name)
            {
                var result = new Mesh { name = name };
                result.SetVertices(vertices);
                result.SetUVs(0, uv);
                result.SetTriangles(triangles, 0);
                result.RecalculateNormals();
                result.RecalculateTangents();
                result.RecalculateBounds();
                return result;
            }
        }
    }
}
