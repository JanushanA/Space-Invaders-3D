using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Campaign
{
    // Four-connected explicit navigation graph. A* heuristic remains admissible with hazard costs >=0.
    public sealed class NavigationGraph
    {
        readonly SectorPlan plan;
        readonly bool[] blocked;
        readonly float[] costs;

        public NavigationGraph(SectorPlan source)
        {
            plan = source;
            blocked = (bool[])source.blocked.Clone();
            costs = new float[blocked.Length];
        }

        public void SetBlocked(int node, bool value) => blocked[node] = value;

        public void SetHazard(int node, float cost) => costs[node] = float.IsNaN(cost) ? 0 : Mathf.Max(0, cost);

        public bool IsBlocked(int node) => blocked[node];

        public List<int> FindPath(int from, int destinationNode, Func<int, int, bool> edgeClear = null)
        {
            var result = new List<int>();
            if (from < 0 || destinationNode < 0 || from >= blocked.Length || destinationNode >= blocked.Length || blocked[destinationNode])
            {
                return result;
            }

            // Track the cheapest known route to each cell and its predecessor for reconstruction.
            var frontier = new IndexedMinHeap<int>();
            var travelCosts = new float[blocked.Length];
            var previousNodes = new int[blocked.Length];

            for (int nodeIndex = 0; nodeIndex < travelCosts.Length; nodeIndex++)
            {
                travelCosts[nodeIndex] = float.PositiveInfinity;
                previousNodes[nodeIndex] = -1;
            }

            travelCosts[from] = 0;
            frontier.Set(from, 0, from);

            while (frontier.Pop(out _, out int currentNode))
            {
                if (currentNode == destinationNode)
                {
                    for (int node = destinationNode; node != from; node = previousNodes[node])
                    {
                        result.Add(node);
                    }

                    result.Reverse();
                    return result;
                }

                foreach (int nextNode in SectorGenerator.Neighbours(currentNode))
                {
                    if (blocked[nextNode] || edgeClear != null && !edgeClear(currentNode, nextNode))
                    {
                        continue;
                    }

                    float candidateCost = travelCosts[currentNode] + SectorPlan.Spacing + costs[nextNode];
                    if (candidateCost >= travelCosts[nextNode])
                    {
                        continue;
                    }

                    travelCosts[nextNode] = candidateCost;
                    previousNodes[nextNode] = currentNode;

                    // Grid distance ignores hazards, so it never overestimates the remaining cost.
                    float estimatedRemainingCost = (Mathf.Abs(nextNode % SectorPlan.Size - destinationNode % SectorPlan.Size) + Mathf.Abs(nextNode / SectorPlan.Size - destinationNode / SectorPlan.Size)) * SectorPlan.Spacing;
                    frontier.Set(nextNode, candidateCost + estimatedRemainingCost, nextNode);
                }
            }

            return result;
        }
    }
}
