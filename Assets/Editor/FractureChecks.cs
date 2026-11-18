using System;
using Game.Destruction;
using UnityEditor;
using UnityEngine;

// Checks that mesh cuts conserve closed volume and severed sections lose their structural connection.
public static class FractureChecks
{
    [MenuItem("Tools/Group Game/Check runtime fracture")]
    public static void Run()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Mesh source = cube.GetComponent<MeshFilter>().sharedMesh;
            Require(Mathf.Abs(ConvexMeshCutter.Volume(source) - 1) < 0.0001f, "source cube volume");

            for (int cutScenario = 0; cutScenario < 24; cutScenario++)
            {
                Vector3 normal = new Vector3(Mathf.Sin(cutScenario * 0.7f) + 0.2f, Mathf.Cos(cutScenario * 0.4f), 0.6f).normalized;
                var plane = new Plane(normal, normal * ((cutScenario % 5 - 2) * 0.07f));
                Require(ConvexMeshCutter.Split(source, plane, out Mesh positive, out Mesh negative), "interior plane must split");
                try
                {
                    Require(ConvexMeshCutter.Volume(positive) > 0.01f && ConvexMeshCutter.Volume(negative) > 0.01f, "positive-volume fragments");
                    Require(Mathf.Abs(ConvexMeshCutter.Volume(positive) + ConvexMeshCutter.Volume(negative) - 1) < 0.0002f, "clipped volume conserved");
                    Require(ConvexMeshCutter.IsClosed(positive) && ConvexMeshCutter.IsClosed(negative), "cut faces close both surfaces");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(positive);
                    UnityEngine.Object.DestroyImmediate(negative);
                }
            }

            Require(!ConvexMeshCutter.Split(source, new Plane(Vector3.right, Vector3.right * 3), out Mesh outsideA, out Mesh outsideB), "outside plane cannot create fragments");

            // Check structural disconnection separately from the geometry of the mesh cuts.
            int[,] links = { { 0, 1 }, { 1, 2 }, { 0, 3 }, { 3, 4 } };
            bool[] connected = StructuralGraph.Connected(new[] { true, false, true, true, true }, links, 0);
            Require(connected[0] && !connected[1] && !connected[2] && connected[3] && connected[4], "severed wing releases attached engine only");
            connected = StructuralGraph.Connected(new[] { false, true, true, true, true }, links, 0);

            foreach (bool node in connected)
            {
                Require(!node, "destroyed core releases all sections");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cube);
        }

        Debug.Log("FRACTURE_CHECKS_OK: 24 cuts, closed surfaces, volume conservation, outside plane, graph connectivity");
    }

    static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new Exception("Fracture check: " + message);
        }
    }
}
