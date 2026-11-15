using System.Collections.Generic;
using Game.World;
using UnityEngine;
namespace Game.Campaign
{
    public sealed class EscortNavigator : MonoBehaviour
    {
        const float Radius = 4.4f;
        SectorPlan plan;
        Transform frame;
        Transform destination;
        NavigationGraph graph;
        LineRenderer line;
        Material material;
        readonly List<int> route = new List<int>();
        int waypoint;
        float nextPlan;

        public IReadOnlyList<int> CurrentPath => route;

        public int ReplanCount
        {
            get; private set;
        }

        public bool Arrived => destination != null && Vector3.Distance(transform.position, destination.position) < 1;

        public void Bind(SectorPlan value, Transform coordinateFrame, Transform target)
        {
            plan = value;
            frame = coordinateFrame;
            destination = target;
            graph = new NavigationGraph(value);
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 0.28f;
            line.numCornerVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            material = Game.Graphics.GraphicsMaterials.Unlit("Convoy route", new Color(0.15f, 1.4f, 1.2f));
            line.sharedMaterial = material;
            Replan();
        }

        Vector3 Point(int cell) => frame.TransformPoint(plan.Position(cell));

        int Closest(Vector3 point, bool requireClear)
        {
            int closestCell = -1;
            float closestDistanceSquared = float.PositiveInfinity;

            for (int index = 0; index < plan.blocked.Length; index++)
            {
                if (!graph.IsBlocked(index))
                {
                    float distanceSquared = (Point(index) - point).sqrMagnitude;
                    if (distanceSquared < closestDistanceSquared && (!requireClear || SweptMovement.Clear(point, Point(index), Radius)))
                    {
                        closestDistanceSquared = distanceSquared;
                        closestCell = index;
                    }
                }
            }

            return closestCell;
        }

        public void Replan()
        {
            if (plan == null)
            {
                return;
            }

            Physics.SyncTransforms();
            ReplanCount++;
            nextPlan = Time.time + 0.8f;

            // Rebuild obstruction and danger costs from the current scene before finding a route.
            var enemies = FindObjectsOfType<Game.Enemies.EnemyController>();
            var mission = GetComponentInParent<CampaignMission>();

            for (int index = 0; index < plan.blocked.Length; index++)
            {
                Vector3 point = Point(index);
                graph.SetBlocked(index, plan.blocked[index] || Physics.CheckSphere(point, Radius, SweptMovement.GeometryMask, QueryTriggerInteraction.Ignore));
                float danger = 0;

                foreach (var enemy in enemies)
                {
                    if (enemy.isActiveAndEnabled)
                    {
                        danger += Mathf.Max(0, 44 - Vector3.Distance(point, enemy.transform.position)) * 2;
                    }
                }

                if (mission != null)
                {
                    danger += mission.HazardCost(point);
                }

                graph.SetHazard(index, danger);
            }

            int from = Closest(transform.position, true);
            int destinationNode = Closest(destination.position, true);
            route.Clear();
            waypoint = 0;
            if (from >= 0 && destinationNode >= 0)
            {
                var found = graph.FindPath(from, destinationNode, (fromCell, toCell) => SweptMovement.Clear(Point(fromCell), Point(toCell), Radius));
                if (from == destinationNode || found.Count > 0)
                {
                    route.Add(from);
                    route.AddRange(found);

                    // Preserve forward progress when periodic replanning starts behind the convoy.
                    if (found.Count > 0 && SweptMovement.Clear(transform.position, Point(found[0]), Radius))
                    {
                        waypoint = 1;
                    }
                    else if (Vector3.Distance(transform.position, Point(from)) < 0.5f)
                    {
                        waypoint = 1;
                    }
                }
            }

            Draw();
        }

        public void Tick(float speed, float deltaTime)
        {
            if (plan == null || Arrived)
            {
                Draw();
                return;
            }

            Vector3 target = waypoint < route.Count ? Point(route[waypoint]) : destination.position;
            if (Time.time >= nextPlan || !SweptMovement.Clear(transform.position, target, Radius))
            {
                Replan();
            }

            if (route.Count == 0)
            {
                Draw();
                return;
            }

            target = waypoint < route.Count ? Point(route[waypoint]) : destination.position;

            // Sweep each movement step even when the route was clear at planning time.
            Vector3 movement = Vector3.ClampMagnitude(target - transform.position, speed * deltaTime);
            SweptMovement.Move(transform, movement, Radius);
            if (movement.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(movement), 90 * deltaTime);
            }

            if (waypoint < route.Count && Vector3.Distance(transform.position, target) < 0.5f)
            {
                waypoint++;
            }

            Draw();
        }

        void Draw()
        {
            if (line == null)
            {
                return;
            }

            int remaining = Mathf.Max(0, route.Count - waypoint);
            line.positionCount = route.Count == 0 ? 0 : remaining + 2;
            if (line.positionCount == 0)
            {
                return;
            }

            line.SetPosition(0, transform.position + Vector3.up * 0.3f);

            for (int index = 0; index < remaining; index++)
            {
                line.SetPosition(index + 1, Point(route[waypoint + index]) + Vector3.up * 0.3f);
            }

            line.SetPosition(remaining + 1, destination.position + Vector3.up * 0.3f);
        }

        void OnDestroy()
        {
            if (material != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(material);
                }
                else
                {
                    DestroyImmediate(material);
                }
            }
        }
    }
}
