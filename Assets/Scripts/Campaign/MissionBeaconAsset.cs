using System.Collections.Generic;
using Game.Graphics;
using UnityEngine;
namespace Game.Campaign
{
    // Reusable mesh-built mission props. Faceted housings, inset luminous bands, separate
    // ribs and crystal optics match the cyan/chamfered navigation language of the HUD.
    public sealed class MissionBeaconAsset : MonoBehaviour
    {
        readonly List<Object> owned = new List<Object>();

        public enum Kind
        {
            NavigationCore, Relay, Extraction
        }

        public static Transform Create(Transform parent, Kind kind, Color accent, string label)
        {
            var root = new GameObject(label);
            root.transform.SetParent(parent, false);
            root.AddComponent<MissionBeaconAsset>().Build(kind, accent);
            return root.transform;
        }

        T Own<T>(T value) where T : Object
        {
            owned.Add(value);
            return value;
        }

        void Part(string label, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
        {
            var partObject = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            partObject.transform.SetParent(transform, false);
            partObject.transform.localPosition = position;
            partObject.transform.localRotation = rotation;
            partObject.GetComponent<MeshFilter>().sharedMesh = Own(mesh);
            partObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void Build(Kind kind, Color accent)
        {
            var alloy = Own(GraphicsMaterials.Lit("Ceramic titanium", new Color(0.20f, 0.28f, 0.32f), 0.78f, 0.52f));
            var dark = Own(GraphicsMaterials.Lit("Inset graphite", new Color(0.025f, 0.045f, 0.055f), 0.62f, 0.36f));
            var light = Own(GraphicsMaterials.Unlit("Navigation optic", accent * 2));
            bool core = kind == Kind.NavigationCore;
            float radius = core ? 2.3f : kind == Kind.Extraction ? 3.8f : 2;
            Part("Armoured octagonal foot", Ring(radius, 0.60f, radius * 0.72f, 8), alloy, Vector3.down * 1.5f, Quaternion.identity);
            Part("Recessed guidance band", Ring(radius * 0.96f, 0.12f, radius * 0.84f, 32), light, Vector3.down * 1.12f, Quaternion.identity);
            Part("Inner machine spindle", Ring(0.48f, core ? 2.8f : 5, 0.24f, 12), dark, Vector3.up * (core ? 0 : 1), Quaternion.identity);
            int rings = core ? 2 : 3;

            for (int index = 0; index < rings; index++)
            {
                float height = core ? -0.55f + index * 1.6f : 0.5f + index * 1.55f;
                float collarRadius = radius * (core ? 0.82f : 1 - index * 0.19f);
                Part("Segmented sensor collar " + index, Ring(collarRadius, 0.28f, collarRadius - 0.25f, 12), alloy, Vector3.up * height, Quaternion.Euler(0, index * 15, 0));
                Part("Inset optical track " + index, Ring(collarRadius + 0.03f, 0.07f, collarRadius - 0.04f, 48), light, Vector3.up * (height + 0.05f), Quaternion.identity);
            }

            for (int index = 0; index < 6; index++)
            {
                float angle = index * 60 * Mathf.Deg2Rad;
                Vector3 ribPosition = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius * 0.78f;
                Part("Tapered cage rib " + index, Fin(core ? 2.9f : 5, core ? 0.34f : 0.52f), alloy, ribPosition + Vector3.up * (core ? -0.9f : -1), Quaternion.Euler(0, -index * 60, 0));
                Part("Status optic " + index, Crystal(0.18f, 0.5f), light, ribPosition + Vector3.up * (core ? 0.9f : 3.6f), Quaternion.identity);
            }

            Part(core ? "Encoded navigation crystal" : "Beacon transmitter", Crystal(core ? 0.8f : 0.42f, core ? 1.65f : 2.2f), light, Vector3.up * (core ? 0.1f : 3.5f), Quaternion.identity);
            if (kind == Kind.Extraction)
            {
                Part("Jump aperture outer ring", Ring(4.6f, 0.32f, 4.2f, 32), alloy, Vector3.up * 5, Quaternion.Euler(90, 0, 0));
                Part("Jump aperture illumination", Ring(4.24f, 0.10f, 4.11f, 48), light, Vector3.up * 5, Quaternion.Euler(90, 0, 0));
            }
        }

        static Mesh Ring(float radius, float height, float inner, int sides)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int index = 0; index < sides; index++)
            {
                float startAngle = index * Mathf.PI * 2 / sides;
                float endAngle = (index + 1) * Mathf.PI * 2 / sides;
                Vector3 outerStart = new Vector3(Mathf.Cos(startAngle) * radius, 0, Mathf.Sin(startAngle) * radius);
                Vector3 outerEnd = new Vector3(Mathf.Cos(endAngle) * radius, 0, Mathf.Sin(endAngle) * radius);
                Vector3 innerStart = outerStart * (inner / radius);
                Vector3 innerEnd = outerEnd * (inner / radius);
                Vector3 heightOffset = Vector3.up * height;

                // Add the outside, inside, top and underside with consistent face winding.
                Quad(vertices, triangles, outerStart, outerEnd, outerEnd + heightOffset, outerStart + heightOffset);
                Quad(vertices, triangles, innerEnd, innerStart, innerStart + heightOffset, innerEnd + heightOffset);
                Quad(vertices, triangles, outerStart + heightOffset, outerEnd + heightOffset, innerEnd + heightOffset, innerStart + heightOffset);
                Quad(vertices, triangles, innerStart, innerEnd, outerEnd, outerStart);
            }

            return Mesh(vertices, triangles, "Faceted machine ring");
        }

        static Mesh Crystal(float radius, float height)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int index = 0; index < 6; index++)
            {
                float startAngle = index * Mathf.PI / 3;
                float endAngle = (index + 1) * Mathf.PI / 3;
                Vector3 edgeStart = new Vector3(Mathf.Cos(startAngle) * radius, 0, Mathf.Sin(startAngle) * radius);
                Vector3 edgeEnd = new Vector3(Mathf.Cos(endAngle) * radius, 0, Mathf.Sin(endAngle) * radius);
                int firstVertex = vertices.Count;
                vertices.AddRange(new[] { edgeStart, Vector3.up * height * 0.6f, edgeEnd, edgeEnd, Vector3.down * height * 0.4f, edgeStart });
                triangles.AddRange(new[] { firstVertex, firstVertex + 1, firstVertex + 2, firstVertex + 3, firstVertex + 4, firstVertex + 5 });
            }

            return Mesh(vertices, triangles, "Hexagonal optic crystal");
        }

        static Mesh Fin(float height, float width)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Vector3 lowerLeft = new Vector3(-width, 0, -0.22f);
            Vector3 lowerRight = new Vector3(width, 0, -0.22f);
            Vector3 upperRight = new Vector3(width * 0.35f, height, -0.10f);
            Vector3 upperLeft = new Vector3(-width * 0.35f, height, -0.10f);
            Vector3 depthOffset = Vector3.forward * 0.32f;
            Quad(vertices, triangles, lowerLeft, lowerRight, upperRight, upperLeft);
            Quad(vertices, triangles, lowerRight + depthOffset, lowerLeft + depthOffset, upperLeft + depthOffset, upperRight + depthOffset);
            Quad(vertices, triangles, lowerLeft, lowerLeft + depthOffset, lowerRight + depthOffset, lowerRight);
            Quad(vertices, triangles, upperLeft, upperRight, upperRight + depthOffset, upperLeft + depthOffset);
            Quad(vertices, triangles, lowerRight, lowerRight + depthOffset, upperRight + depthOffset, upperRight);
            Quad(vertices, triangles, lowerLeft + depthOffset, lowerLeft, upperLeft, upperLeft + depthOffset);
            return Mesh(vertices, triangles, "Tapered structural rib");
        }

        static void Quad(List<Vector3> vertices, List<int> triangles, Vector3 firstCorner, Vector3 secondCorner, Vector3 thirdCorner, Vector3 fourthCorner)
        {
            int firstVertex = vertices.Count;
            vertices.AddRange(new[] { firstCorner, secondCorner, thirdCorner, fourthCorner });
            triangles.AddRange(new[] { firstVertex, firstVertex + 1, firstVertex + 2, firstVertex, firstVertex + 2, firstVertex + 3 });
        }

        static Mesh Mesh(List<Vector3> vertices, List<int> triangles, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var value in owned)
            {
                if (value != null)
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
            }
        }
    }
}
