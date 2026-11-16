using UnityEngine;
using Game.Core;

// Checks the entire distance a ship wants to move, so fast movement cannot jump through an asteroid.
namespace Game.World
{
    // Conservative ship sphere swept over the entire displacement, including dash and separation.
    public static class SweptMovement
    {
        const float Skin = 0.035f;
        static SphereCollider probe;

        public static int GeometryMask => LayerMask.GetMask(GameLayers.LevelGeometry);

        static SphereCollider Probe
        {
            get
            {
                if (probe == null)
                {
                    // Keep the native PhysX shape initialized. An inactive GameObject whose
                    // collider has never been enabled can make ComputePenetration return false.
                    var sceneObject = new GameObject("Collision query sphere") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                    sceneObject.transform.position = new Vector3(0, -100000, 0);
                    probe = sceneObject.AddComponent<SphereCollider>();
                    probe.isTrigger = true;
                }

                return probe;
            }
        }

        public static Vector3 Move(Transform mover, Vector3 displacement, float radius = 2.4f)
        {
            Physics.SyncTransforms();
            Vector3 start = mover.position;
            Vector3 position = Resolve(start, radius);

            // On contact, keep only the movement along the surface. Repeat for corners.
            for (int slide = 0; slide < 5 && displacement.sqrMagnitude > 0.000001f; slide++)
            {
                float distance = displacement.magnitude;
                Vector3 direction = displacement / distance;
                if (!Physics.SphereCast(position, radius, direction, out var hit, distance + Skin, GeometryMask, QueryTriggerInteraction.Ignore))
                {
                    position += displacement;
                    break;
                }

                float travel = Mathf.Max(0, hit.distance - Skin);
                position += direction * travel;
                displacement = Vector3.ProjectOnPlane(displacement - direction * travel, hit.normal);
            }

            mover.position = Resolve(position, radius);
            return mover.position - start;
        }

        public static Vector3 Resolve(Vector3 position, float radius)
        {
            var shape = Probe;
            shape.radius = radius;
            Physics.SyncTransforms();

            // A spawn or moving obstacle can leave the ship overlapping geometry.
            // Push it out in bounded steps before attempting further travel.
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;

                foreach (var other in Physics.OverlapSphere(position, radius, GeometryMask, QueryTriggerInteraction.Ignore))
                {
                    if (Physics.ComputePenetration(shape, position, Quaternion.identity, other, other.transform.position, other.transform.rotation, out Vector3 direction, out float distance))
                    {
                        position += direction * (distance + Skin);
                        moved = true;
                    }
                }

                if (!moved)
                {
                    break;
                }
            }

            return position;
        }

        public static bool Clear(Vector3 startPosition, Vector3 endPosition, float radius)
        {
            if (Physics.CheckSphere(endPosition, radius, GeometryMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Vector3 delta = endPosition - startPosition;
            return delta.sqrMagnitude < 0.001f || !Physics.SphereCast(startPosition, radius, delta.normalized, out _, delta.magnitude, GeometryMask, QueryTriggerInteraction.Ignore);
        }
    }
}
