using System;
using System.Reflection;
using Game.World;
using UnityEditor;
using UnityEngine;

// Checks scenery budgets, colliders and mesh bounds without disturbing gameplay randomness.
public static class OrbitalEnvironmentChecks
{
    [MenuItem("Tools/Group Game/Check orbital environment")]
    public static void Run()
    {
        var root = new GameObject("Orbital environment check");
        UnityEngine.Random.State before = UnityEngine.Random.state;
        try
        {
            foreach (string texture in new[] { "PlanetTerrain", "PlanetClouds", "RockSurface", "SolarCells" })
            {
                Require(Resources.Load<Texture2D>("Orbital/" + texture) != null, "Missing generated orbital texture " + texture);
            }

            var environment = root.AddComponent<OrbitalEnvironment>();
            typeof(OrbitalEnvironment).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(environment, null);
            Require(UnityEngine.Random.state.Equals(before), "Decorative geometry consumed gameplay randomness");
            Require(root.GetComponentsInChildren<Collider>().Length == 48, "Every decorative asteroid needs a physical collider");
            Require(root.GetComponentsInChildren<MeshRenderer>().Length <= 110, "Scenery exceeded its bounded renderer budget");
            LODGroup[] rocks = root.GetComponentsInChildren<LODGroup>();
            Require(rocks.Length == 48, "Expected the bounded 48-rock field");

            foreach (LODGroup rock in rocks)
            {
                float radius = rock.size * Mathf.Max(rock.transform.lossyScale.x, Mathf.Max(rock.transform.lossyScale.y, rock.transform.lossyScale.z)) * 0.5f;
                Require(rock.transform.position.magnitude - radius >= 80f, "A decorative rock entered the clear combat arena");
                Require(rock.GetLODs().Length == 2, "Asteroid lacks its near/far LOD pair");
                var collider = rock.GetComponent<MeshCollider>();
                Require(collider != null && collider.convex && !collider.isTrigger && rock.gameObject.layer == LayerMask.NameToLayer(Game.Core.GameLayers.LevelGeometry), "Asteroid collision must block all ship sweeps");
            }

            Transform planet = root.transform.Find("Orbital environment/Campaign planet " + Mathf.Clamp(Game.Core.GameSession.SelectedLevel, 1, 8));
            Require(planet != null, "Planet horizon missing");
            Vector3 oldPosition = planet.position;
            root.transform.position += new Vector3(-1200, 20, 300);
            Require(Vector3.Distance(planet.position, oldPosition + root.transform.position) < 0.01f, "Scenery did not follow the shifted root");
            Mesh sphere = planet.GetComponentInChildren<MeshFilter>().sharedMesh;
            Vector3[] vertices = sphere.vertices;
            int[] triangles = sphere.triangles;
            int middle = triangles.Length / 2;
            middle -= middle % 3;
            Vector3 firstPoint = vertices[triangles[middle]];
            Vector3 secondPoint = vertices[triangles[middle + 1]];
            Vector3 thirdPoint = vertices[triangles[middle + 2]];
            Require(Vector3.Dot(Vector3.Cross(secondPoint - firstPoint, thirdPoint - firstPoint), firstPoint + secondPoint + thirdPoint) > 0, "Planet sphere faces inward");

            for (int variant = 0; variant < 6; variant++)
            {
                var navigation = OrbitalEnvironment.NavigationAsteroidMesh(variant);
                try
                {
                    foreach (var vertex in navigation.vertices)
                    {
                        Require(vertex.magnitude <= 0.5f, "Navigation art exceeds validated collider bounds");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(navigation);
                }
            }

            Debug.Log("ORBITAL ENVIRONMENT CHECKS: PASS assets, preserved random state, 48 physical colliders, bounded draw budget, clear arena, LODs, origin shift, outward sphere topology and navigation mesh bounds");
        }
        finally
        {
            var environment = root.GetComponent<OrbitalEnvironment>();
            if (environment != null)
            {
                typeof(OrbitalEnvironment).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(environment, null);
            }

            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Random.state = before;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
