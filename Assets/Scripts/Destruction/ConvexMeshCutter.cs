using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Destruction
{
    // Plane clipping for closed convex meshes. Concave imported hulls are deliberately not accepted as modules.
    public static class ConvexMeshCutter
    {
        const float Epsilon = 0.00001f;

        sealed class Builder
        {
            public readonly List<Vector3> vertices = new List<Vector3>(96);
            public readonly List<int> surface = new List<int>(96);
            public readonly List<int> cap = new List<int>(48);

            public void Triangle(Vector3 firstVertex, Vector3 secondVertex, Vector3 thirdVertex, bool isCutSurface = false)
            {
                if (Vector3.Cross(secondVertex - firstVertex, thirdVertex - firstVertex).sqrMagnitude < 1e-14f)
                {
                    return;
                }

                int index = vertices.Count;
                vertices.Add(firstVertex);
                vertices.Add(secondVertex);
                vertices.Add(thirdVertex);
                var indices = isCutSurface ? cap : surface;
                indices.Add(index);
                indices.Add(index + 1);
                indices.Add(index + 2);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh
                {
                    name = name
                };
                mesh.SetVertices(vertices);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(surface, 0);
                mesh.SetTriangles(cap, 1);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public static bool Split(Mesh source, Plane plane, out Mesh positive, out Mesh negative)
        {
            positive = null;
            negative = null;
            if (source == null || source.vertexCount > 1024 || plane.normal.sqrMagnitude < 0.5f)
            {
                return false;
            }

            var positiveBuilder = new Builder();
            var negativeBuilder = new Builder();
            var cutBoundary = new List<Vector3>(24);
            Vector3[] vertices = source.vertices;
            int[] triangles = source.triangles;
            if (triangles.Length > 6144)
            {
                return false;
            }

            // Clip each source triangle independently into both half-spaces.
            var polygon = new List<Vector3>(4);

            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
            {
                Vector3 triangleStart = vertices[triangles[triangleIndex]];
                Vector3 triangleMiddle = vertices[triangles[triangleIndex + 1]];
                Vector3 triangleEnd = vertices[triangles[triangleIndex + 2]];
                ClipTriangle(triangleStart, triangleMiddle, triangleEnd, plane, true, positiveBuilder, polygon);
                ClipTriangle(triangleStart, triangleMiddle, triangleEnd, plane, false, negativeBuilder, polygon);
                Intersections(triangleStart, triangleMiddle, plane, cutBoundary);
                Intersections(triangleMiddle, triangleEnd, plane, cutBoundary);
                Intersections(triangleEnd, triangleStart, plane, cutBoundary);
            }

            if (cutBoundary.Count < 3 || positiveBuilder.surface.Count == 0 || negativeBuilder.surface.Count == 0)
            {
                return false;
            }

            // Sort the cut boundary around its center so triangle fans can close both halves.
            Vector3 center = Vector3.zero;

            foreach (Vector3 point in cutBoundary)
            {
                center += point;
            }

            center /= cutBoundary.Count;
            Vector3 normal = plane.normal.normalized;
            Vector3 planeHorizontal = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 planeVertical = Vector3.Cross(normal, planeHorizontal);
            cutBoundary.Sort((firstPoint, secondPoint) =>
                Mathf.Atan2(
                    Vector3.Dot(firstPoint - center, planeVertical),
                    Vector3.Dot(firstPoint - center, planeHorizontal))
                .CompareTo(Mathf.Atan2(
                    Vector3.Dot(secondPoint - center, planeVertical),
                    Vector3.Dot(secondPoint - center, planeHorizontal))));

            // Opposite winding makes each cap face outward from its own fragment.
            for (int triangleIndex = 0; triangleIndex < cutBoundary.Count; triangleIndex++)
            {
                Vector3 triangleStart = cutBoundary[triangleIndex];
                Vector3 triangleMiddle = cutBoundary[(triangleIndex + 1) % cutBoundary.Count];
                positiveBuilder.Triangle(center, triangleMiddle, triangleStart, true);
                negativeBuilder.Triangle(center, triangleStart, triangleMiddle, true);
            }

            positive = positiveBuilder.Build("Runtime cut positive");
            negative = negativeBuilder.Build("Runtime cut negative");
            return true;
        }

        static void ClipTriangle(Vector3 firstVertex, Vector3 secondVertex, Vector3 thirdVertex, Plane plane, bool keepPositive, Builder output, List<Vector3> polygon)
        {
            polygon.Clear();
            ClipEdge(firstVertex, secondVertex, plane, keepPositive, polygon);
            ClipEdge(secondVertex, thirdVertex, plane, keepPositive, polygon);
            ClipEdge(thirdVertex, firstVertex, plane, keepPositive, polygon);

            for (int triangleIndex = 1; triangleIndex + 1 < polygon.Count; triangleIndex++)
            {
                output.Triangle(polygon[0], polygon[triangleIndex], polygon[triangleIndex + 1]);
            }
        }

        static void ClipEdge(Vector3 firstVertex, Vector3 secondVertex, Plane plane, bool positive, List<Vector3> polygon)
        {
            float startDistance = plane.GetDistanceToPoint(firstVertex);
            float endDistance = plane.GetDistanceToPoint(secondVertex);
            bool startInside = positive ? startDistance >= 0 : startDistance <= 0;
            bool endInside = positive ? endDistance >= 0 : endDistance <= 0;
            if (startInside)
            {
                polygon.Add(firstVertex);
            }

            if (startInside != endInside)
            {
                polygon.Add(Vector3.LerpUnclamped(firstVertex, secondVertex, startDistance / (startDistance - endDistance)));
            }
        }

        static void Intersections(Vector3 firstVertex, Vector3 secondVertex, Plane plane, List<Vector3> points)
        {
            float startDistance = plane.GetDistanceToPoint(firstVertex);
            float endDistance = plane.GetDistanceToPoint(secondVertex);
            if (Mathf.Abs(startDistance) < Epsilon)
            {
                AddUnique(points, firstVertex);
            }

            if (startDistance * endDistance < 0)
            {
                AddUnique(points, Vector3.LerpUnclamped(firstVertex, secondVertex, startDistance / (startDistance - endDistance)));
            }
        }

        static void AddUnique(List<Vector3> points, Vector3 value)
        {
            foreach (Vector3 point in points)
            {
                if ((point - value).sqrMagnitude < Epsilon * Epsilon)
                {
                    return;
                }
            }

            points.Add(value);
        }

        public static float Volume(Mesh mesh)
        {
            double volume = 0;
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;

            for (int triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                volume += Vector3.Dot(vertices[indices[triangleIndex]], Vector3.Cross(vertices[indices[triangleIndex + 1]], vertices[indices[triangleIndex + 2]])) / 6.0;
            }

            return Mathf.Abs((float)volume);
        }

        // Geometric edge pairing across duplicated triangle vertices, including the cut polygon.
        public static bool IsClosed(Mesh mesh)
        {
            // Mesh triangles may duplicate their corner vertices; weld positions for edge counting.
            var unique = new List<Vector3>();
            Vector3[] vertices = mesh.vertices;
            var vertexIds = new int[vertices.Length];

            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                int vertexId = -1;

                for (int uniqueIndex = 0; uniqueIndex < unique.Count; uniqueIndex++)
                {
                    if ((vertices[vertexIndex] - unique[uniqueIndex]).sqrMagnitude < 1e-8f)
                    {
                        vertexId = uniqueIndex;
                        break;
                    }
                }

                if (vertexId < 0)
                {
                    vertexId = unique.Count;
                    unique.Add(vertices[vertexIndex]);
                }

                vertexIds[vertexIndex] = vertexId;
            }

            // A closed surface uses each undirected edge exactly twice.
            var edges = new Dictionary<long, int>();
            int[] indices = mesh.triangles;

            for (int triangleIndex = 0; triangleIndex < indices.Length; triangleIndex += 3)
            {
                for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
                {
                    int firstVertex = vertexIds[indices[triangleIndex + edgeIndex]];
                    int secondVertex = vertexIds[indices[triangleIndex + (edgeIndex + 1) % 3]];
                    if (firstVertex == secondVertex)
                    {
                        return false;
                    }

                    long key = ((long)Mathf.Min(firstVertex, secondVertex) << 32) | (uint)Mathf.Max(firstVertex, secondVertex);
                    edges.TryGetValue(key, out int count);
                    edges[key] = count + 1;
                }
            }

            foreach (int count in edges.Values)
            {
                if (count != 2)
                {
                    return false;
                }
            }

            return edges.Count > 0;
        }
    }

    public static class StructuralGraph
    {
        public static bool[] Connected(bool[] intact, int[,] links, int root)
        {
            var connected = new bool[intact.Length];
            if (root < 0 || root >= intact.Length || !intact[root])
            {
                return connected;
            }

            // Visit intact neighbors reachable from the structural root.
            var queue = new Queue<int>();
            queue.Enqueue(root);
            connected[root] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                for (int linkIndex = 0; linkIndex < links.GetLength(0); linkIndex++)
                {
                    int firstModule = links[linkIndex, 0];
                    int secondModule = links[linkIndex, 1];
                    if (firstModule < 0 || secondModule < 0 || firstModule >= intact.Length || secondModule >= intact.Length)
                    {
                        continue;
                    }

                    int nextModule = firstModule == current ? secondModule : secondModule == current ? firstModule : -1;
                    if (nextModule >= 0 && intact[nextModule] && !connected[nextModule])
                    {
                        connected[nextModule] = true;
                        queue.Enqueue(nextModule);
                    }
                }
            }

            return connected;
        }
    }
}
