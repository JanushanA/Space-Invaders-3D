using System.Collections.Generic;
using Game.Combat;
using Game.Graphics;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the cockpit structure from mesh pieces and changes its visibility with the camera mode.
namespace Game.Player
{
    // Camera-local physical cabin. Four combined meshes, no colliders or camera pose ownership.
    [DefaultExecutionOrder(120)]
    public sealed class CockpitRig : MonoBehaviour
    {
        private GameObject cabin;
        private Health health;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();

        private void Awake()
        {
            health = GetComponent<Health>();
        }

        private void LateUpdate()
        {
            FlightView view = FlightView.Instance;
            bool visible = view != null && view.gameObject == gameObject && view.IsFirstPerson &&
                view.ViewCamera != null && (health == null || health.IsAlive);
            if (visible && cabin == null)
            {
                Build();
            }

            if (cabin == null)
            {
                return;
            }

            cabin.SetActive(visible);
            if (!visible)
            {
                return;
            }

            Transform cameraTransform = view.ViewCamera.transform;
            if (cabin.transform.parent != cameraTransform)
            {
                cabin.transform.SetParent(cameraTransform, false);
            }

            // Preserve the composed perimeter at different FOV/aspect settings.
            float heightScale = Mathf.Tan(view.ViewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(35f * Mathf.Deg2Rad);
            cabin.transform.localScale = new Vector3(heightScale * view.ViewCamera.aspect / (16f / 9f), heightScale, 1f);
        }

        private void OnDisable()
        {
            if (cabin != null)
            {
                cabin.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (cabin != null)
            {
                Release(cabin);
            }

            foreach (Mesh mesh in meshes)
            {
                if (mesh != null)
                {
                    Release(mesh);
                }
            }

            foreach (Material material in materials)
            {
                if (material != null)
                {
                    Release(material);
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

        private void Build()
        {
            cabin = new GameObject("Pilot cabin · runtime geometry");
            var shell = new MeshBatch();
            var trim = new MeshBatch();
            var amber = new MeshBatch();
            var cyan = new MeshBatch();

            // Chamfered, overlapping console shells provide visible depth and bevel highlights.
            shell.Panel(new Vector3(0, -0.57f, 0.99f), new Vector3(0.91f, 0.28f, 0.45f), Quaternion.Euler(14, 0, 0));
            trim.Panel(new Vector3(0, -0.454f, 0.81f), new Vector3(0.73f, 0.025f, 0.14f), Quaternion.Euler(14, 0, 0));

            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion consoleRotation = Quaternion.Euler(10, side * -15, side * -4);
                shell.Panel(new Vector3(side * 0.79f, -0.58f, 0.91f), new Vector3(0.70f, 0.29f, 0.52f), consoleRotation);
                trim.Panel(new Vector3(side * 0.82f, -0.445f, 0.78f), new Vector3(0.57f, 0.023f, 0.17f), consoleRotation);

                // Lower windscreen rails and diagonal canopy ribs remain outside the aiming area.
                Vector3 heel = new Vector3(side * 0.56f, -0.43f, 0.99f);
                Vector3 shoulder = new Vector3(side * 1.25f, 0.69f, 1.36f);
                shell.Beam(heel, shoulder, 0.063f, 0.11f);
                trim.Beam(heel + new Vector3(side * -0.025f, 0, -0.06f), shoulder + new Vector3(side * -0.025f, 0, -0.06f), 0.012f, 0.018f);
                amber.Beam(heel + new Vector3(side * -0.038f, 0.015f, -0.073f), shoulder + new Vector3(side * -0.038f, -0.08f, -0.073f), 0.003f, 0.006f);
                shell.Beam(new Vector3(side * 0.40f, -0.44f, 0.98f), new Vector3(side * 1.36f, -0.43f, 1.1f), 0.055f, 0.11f);
                amber.Beam(new Vector3(side * 0.51f, -0.417f, 0.917f), new Vector3(side * 1.13f, -0.413f, 1.01f), 0.004f, 0.006f);

                // Recessed side housing, fasteners and short status lights.
                shell.Panel(new Vector3(side * 1.10f, -0.30f, 0.81f), new Vector3(0.18f, 0.36f, 0.28f), Quaternion.Euler(0, side * -18, side * -12));

                for (int canopySupportIndex = 0; canopySupportIndex < 4; canopySupportIndex++)
                {
                    trim.Panel(new Vector3(side * (0.68f + canopySupportIndex * 0.087f), -0.59f, 0.627f), new Vector3(0.048f, 0.032f, 0.014f), Quaternion.identity);
                    cyan.Panel(new Vector3(side * (0.68f + canopySupportIndex * 0.087f), -0.58f, 0.615f), new Vector3(0.023f, 0.003f, 0.005f), Quaternion.identity);
                }

                for (int instrumentIndex = 0; instrumentIndex < 5; instrumentIndex++)
                {
                    trim.Beam(new Vector3(side * 1.03f, -0.39f - instrumentIndex * 0.018f, 0.65f), new Vector3(side * 1.11f, -0.39f - instrumentIndex * 0.018f, 0.65f), 0.005f, 0.008f);
                }

                // Small side hologram pedestals, physically raised from the dashboard.
                Ring(trim, new Vector3(side * 0.56f, -0.40f, 0.85f), new Vector2(0.10f, 0.035f), 0.009f);
                Ring(cyan, new Vector3(side * 0.56f, -0.392f, 0.835f), new Vector2(0.092f, 0.031f), 0.002f);
            }

            shell.Beam(new Vector3(-1.25f, 0.69f, 1.36f), new Vector3(1.25f, 0.69f, 1.36f), 0.056f, 0.10f);
            trim.Beam(new Vector3(-1.23f, 0.66f, 1.30f), new Vector3(1.23f, 0.66f, 1.30f), 0.008f, 0.013f);

            // Radar projector recess: the live bearing display is rendered directly above this console.
            Ring(trim, new Vector3(0, -0.494f, 0.91f), new Vector2(0.27f, 0.08f), 0.014f);
            Ring(amber, new Vector3(0, -0.486f, 0.895f), new Vector2(0.25f, 0.073f), 0.003f);
            AddBatch("Graphite cabin", shell, GraphicsMaterials.Lit("Cabin graphite", new Color(0.075f, 0.09f, 0.11f), 0.35f, 0.48f));
            AddBatch("Machined edges", trim, GraphicsMaterials.Lit("Cabin brushed alloy", new Color(0.19f, 0.23f, 0.26f), 0.88f, 0.66f));
            AddBatch("Amber light guides", amber, GraphicsMaterials.Unlit("Cabin amber emission", GameHud.Amber * 1.8f));
            AddBatch("Cyan light guides", cyan, GraphicsMaterials.Unlit("Cabin cyan emission", GameHud.Cyan * 1.5f));
        }

        private static void Ring(MeshBatch batch, Vector3 centre, Vector2 radius, float thickness)
        {
            const int count = 48;

            for (int segmentIndex = 0; segmentIndex < count; segmentIndex++)
            {
                float startAngle = segmentIndex * Mathf.PI * 2 / count;
                float endAngle = (segmentIndex + 1) * Mathf.PI * 2 / count;
                batch.Beam(centre + new Vector3(Mathf.Cos(startAngle) * radius.x, Mathf.Sin(startAngle) * radius.y, 0),
                    centre + new Vector3(Mathf.Cos(endAngle) * radius.x, Mathf.Sin(endAngle) * radius.y, 0), thickness, thickness);
            }
        }

        private void AddBatch(string name, MeshBatch batch, Material material)
        {
            var sceneObject = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            sceneObject.transform.SetParent(cabin.transform, false);
            Mesh mesh = batch.Build(name);
            meshes.Add(mesh);
            materials.Add(material);
            sceneObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = sceneObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        }

        private sealed class MeshBatch
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<int> triangles = new List<int>();

            public void Beam(Vector3 startPosition, Vector3 endPosition, float width, float depth)
            {
                Vector3 direction = endPosition - startPosition;
                Panel((startPosition + endPosition) * 0.5f, new Vector3(width, depth, direction.magnitude), Quaternion.LookRotation(direction));
            }

            public void Panel(Vector3 centre, Vector3 size, Quaternion rotation)
            {
                // Octagonal extrusion with clipped corners: real front, rear and side surfaces.
                Vector2[] outline = { new Vector2(-0.40f,-0.5f), new Vector2(0.40f,-0.5f), new Vector2(0.5f,-0.40f),
                    new Vector2(0.5f,0.40f), new Vector2(0.40f,0.5f), new Vector2(-0.40f,0.5f), new Vector2(-0.5f,0.40f), new Vector2(-0.5f,-0.40f) };
                Vector3[] front = new Vector3[8];
                Vector3[] back = new Vector3[8];

                for (int vertexIndex = 0; vertexIndex < 8; vertexIndex++)
                {
                    front[vertexIndex] = centre + rotation * new Vector3(outline[vertexIndex].x * size.x, outline[vertexIndex].y * size.y, -size.z * 0.5f);
                    back[vertexIndex] = centre + rotation * new Vector3(outline[vertexIndex].x * size.x, outline[vertexIndex].y * size.y, size.z * 0.5f);
                }

                for (int faceTriangleIndex = 1; faceTriangleIndex < 7; faceTriangleIndex++)
                {
                    Triangle(front[0], front[faceTriangleIndex + 1], front[faceTriangleIndex]);
                    Triangle(back[0], back[faceTriangleIndex], back[faceTriangleIndex + 1]);
                }

                for (int edgeIndex = 0; edgeIndex < 8; edgeIndex++)
                {
                    int next = (edgeIndex + 1) % 8;
                    Triangle(front[edgeIndex], front[next], back[next]);
                    Triangle(front[edgeIndex], back[next], back[edgeIndex]);
                }
            }

            private void Triangle(Vector3 firstPoint, Vector3 secondPoint, Vector3 thirdPoint)
            {
                int start = vertices.Count;
                vertices.Add(firstPoint);
                vertices.Add(secondPoint);
                vertices.Add(thirdPoint);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                return mesh;
            }
        }
    }
}
