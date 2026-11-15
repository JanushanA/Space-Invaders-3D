using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Campaign
{
    public readonly struct SectorLink
    {
        public readonly int From;
        public readonly int To;
        public readonly float Cost;

        public SectorLink(int from, int destinationSector, float cost)
        {
            From = from;
            To = destinationSector;
            Cost = cost;
        }
    }

    public sealed class SectorNetwork
    {
        readonly List<SectorLink> edges = new List<SectorLink>();
        readonly Vector2[] positions = new Vector2[8];

        public IReadOnlyList<SectorLink> Edges => edges;

        public IReadOnlyList<Vector2> Positions => positions;

        public int Seed
        {
            get;
        }

        public int Count => positions.Length;

        sealed class UnionFind
        {
            readonly int[] parent;
            readonly int[] rank;

            public UnionFind(int count)
            {
                parent = new int[count];
                rank = new int[count];

                for (int index = 0; index < count; index++)
                {
                    parent[index] = index;
                }
            }

            // Path compression shortens future lookups; ranks keep merged trees shallow.
            int Find(int nodeIndex)
            {
                while (parent[nodeIndex] != nodeIndex)
                {
                    parent[nodeIndex] = parent[parent[nodeIndex]];
                    nodeIndex = parent[nodeIndex];
                }

                return nodeIndex;
            }

            public bool Join(int firstRoot, int secondRoot)
            {
                firstRoot = Find(firstRoot);
                secondRoot = Find(secondRoot);
                if (firstRoot == secondRoot)
                {
                    return false;
                }

                if (rank[firstRoot] < rank[secondRoot])
                {
                    int previousRoot = firstRoot;
                    firstRoot = secondRoot;
                    secondRoot = previousRoot;
                }

                parent[secondRoot] = firstRoot;
                if (rank[firstRoot] == rank[secondRoot])
                {
                    rank[firstRoot]++;
                }

                return true;
            }
        }

        public SectorNetwork(int seed)
        {
            Seed = seed;
            var random = new System.Random(seed);
            var candidates = new List<SectorLink>();

            for (int sectorIndex = 0; sectorIndex < 8; sectorIndex++)
            {
                float angle = (sectorIndex * 45 + (float)random.NextDouble() * 24 - 12) * Mathf.Deg2Rad;
                float radius = 0.65f + (float)random.NextDouble() * 0.3f;
                positions[sectorIndex] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            for (int firstSector = 0; firstSector < 8; firstSector++)
            {
                for (int secondSector = firstSector + 1; secondSector < 8; secondSector++)
                {
                    candidates.Add(new SectorLink(firstSector + 1, secondSector + 1, Vector2.Distance(positions[firstSector], positions[secondSector]) * (0.4f + (float)random.NextDouble() * 1.4f)));
                }
            }

            candidates.Sort((firstLink, secondLink) =>
            {
                int costComparison = firstLink.Cost.CompareTo(secondLink.Cost);
                return costComparison != 0 ? costComparison : (firstLink.From * 8 + firstLink.To).CompareTo(secondLink.From * 8 + secondLink.To);
            });

            // Kruskal's algorithm connects every sector with the cheapest cycle-free links.
            var connectedSectors = new UnionFind(8);

            foreach (var edge in candidates)
            {
                if (connectedSectors.Join(edge.From - 1, edge.To - 1))
                {
                    edges.Add(edge);
                }
            }

            // Two extra jump lanes create genuine choices beyond the minimum spanning tree.
            int extraLinks = 0;

            foreach (var edge in candidates)
            {
                if (!Adjacent(edge.From, edge.To))
                {
                    edges.Add(edge);
                    if (++extraLinks == 2)
                    {
                        break;
                    }
                }
            }
        }

        public bool Adjacent(int from, int destinationSector)
        {
            foreach (var edge in edges)
            {
                if (edge.From == from && edge.To == destinationSector || edge.To == from && edge.From == destinationSector)
                {
                    return true;
                }
            }

            return false;
        }

        public List<int> Neighbours(int from)
        {
            var result = new List<int>();

            foreach (var edge in edges)
            {
                if (edge.From == from)
                {
                    result.Add(edge.To);
                }
                else if (edge.To == from)
                {
                    result.Add(edge.From);
                }
            }

            result.Sort();
            return result;
        }
    }
}
