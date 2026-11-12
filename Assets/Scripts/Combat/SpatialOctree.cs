using System;
using System.Collections.Generic;
using UnityEngine;

// Divides space into smaller cubes so radar and targeting can find nearby objects without checking every object.
namespace Game.Combat
{
    // Point octree with leaf ownership: normal motion is O(depth), queries prune whole cubes.
    public sealed class SpatialOctree<T>
    {
        sealed class Entry
        {
            public T Value;
            public Vector3 Position;
            public Node Leaf;
        }

        sealed class Node
        {
            public Vector3 Centre;
            public float Half;
            public Node[] Children;
            public readonly List<Entry> Entries = new List<Entry>();

            public bool Contains(Vector3 position) => Mathf.Abs(position.x - Centre.x) <= Half && Mathf.Abs(position.y - Centre.y) <= Half && Mathf.Abs(position.z - Centre.z) <= Half;
        }
        readonly Dictionary<T, Entry> entries = new Dictionary<T, Entry>();
        readonly int capacity;
        Node root;

        public int Count => entries.Count;

        public SpatialOctree(Vector3 centre, float halfSize = 512, int leafCapacity = 12)
        {
            root = new Node { Centre = centre, Half = Mathf.Max(1, halfSize) };
            capacity = Math.Max(1, leafCapacity);
        }

        public void Upsert(T value, Vector3 position)
        {
            if (!Finite(position))
            {
                return;
            }

            if (entries.TryGetValue(value, out Entry entry))
            {
                entry.Position = position;

                // An object that stays inside its cube does not need to be reinserted.
                if (entry.Leaf.Contains(position))
                {
                    return;
                }

                entry.Leaf.Entries.Remove(entry);
            }
            else
            {
                entry = new Entry { Value = value, Position = position };
                entries.Add(value, entry);
            }

            // Grow the outer cube and rebuild only when an object moves beyond the indexed area.
            if (!root.Contains(position))
            {
                while (!root.Contains(position))
                {
                    root.Half *= 2;
                }

                root = new Node { Centre = root.Centre, Half = root.Half };

                foreach (Entry item in entries.Values)
                {
                    Insert(root, item, 0);
                }

                return;
            }

            Insert(root, entry, 0);
        }

        void Insert(Node node, Entry entry, int depth)
        {
            // Stop subdividing when a cube has room, or when the depth limit is reached.
            if (node.Children == null && (node.Entries.Count < capacity || depth >= 12))
            {
                node.Entries.Add(entry);
                entry.Leaf = node;
                return;
            }

            if (node.Children == null)
            {
                // Split a crowded cube into eight smaller cubes and move its entries into them.
                node.Children = new Node[8];
                float half = node.Half * 0.5f;

                for (int childOctant = 0; childOctant < 8; childOctant++)
                {
                    node.Children[childOctant] = new Node { Half = half, Centre = node.Centre + new Vector3((childOctant & 1) == 0 ? -half : half, (childOctant & 2) == 0 ? -half : half, (childOctant & 4) == 0 ? -half : half) };
                }

                foreach (Entry existingEntry in node.Entries)
                {
                    Insert(Child(node, existingEntry.Position), existingEntry, depth + 1);
                }

                node.Entries.Clear();
            }

            Insert(Child(node, entry.Position), entry, depth + 1);
        }

        // The three binary flags select the left/right, lower/upper and back/front half.
        static Node Child(Node node, Vector3 position) => node.Children[(position.x >= node.Centre.x ? 1 : 0) | (position.y >= node.Centre.y ? 2 : 0) | (position.z >= node.Centre.z ? 4 : 0)];

        public bool Remove(T value)
        {
            if (!entries.TryGetValue(value, out Entry entry))
            {
                return false;
            }

            entry.Leaf.Entries.Remove(entry);
            return entries.Remove(value);
        }

        public void Query(Vector3 centre, float radius, List<T> result)
        {
            if (radius < 0 || !Finite(centre) || float.IsNaN(radius))
            {
                return;
            }

            Query(root, centre, radius * radius, result);
        }

        static void Query(Node node, Vector3 queryCentre, float radiusSquared, List<T> result)
        {
            // Measure the shortest distance from the search centre to this cube.
            // If the cube is outside the search sphere, none of its descendants can match.
            Vector3 centreOffset = queryCentre - node.Centre;
            float distanceOutsideX = Mathf.Max(0, Mathf.Abs(centreOffset.x) - node.Half);
            float distanceOutsideY = Mathf.Max(0, Mathf.Abs(centreOffset.y) - node.Half);
            float distanceOutsideZ = Mathf.Max(0, Mathf.Abs(centreOffset.z) - node.Half);
            if (distanceOutsideX * distanceOutsideX + distanceOutsideY * distanceOutsideY + distanceOutsideZ * distanceOutsideZ > radiusSquared)
            {
                return;
            }

            foreach (Entry entry in node.Entries)
            {
                if ((entry.Position - queryCentre).sqrMagnitude <= radiusSquared)
                {
                    result.Add(entry.Value);
                }
            }

            if (node.Children != null)
            {
                foreach (Node child in node.Children)
                {
                    Query(child, queryCentre, radiusSquared, result);
                }
            }
        }

        // The world moves during an origin shift; move the index by the same amount.
        public void Shift(Vector3 offset)
        {
            ShiftNode(root, offset);

            foreach (Entry entry in entries.Values)
            {
                entry.Position += offset;
            }
        }

        static void ShiftNode(Node node, Vector3 offset)
        {
            node.Centre += offset;
            if (node.Children != null)
            {
                foreach (Node childNode in node.Children)
                {
                    ShiftNode(childNode, offset);
                }
            }
        }

        static bool Finite(Vector3 position) => !float.IsNaN(position.x + position.y + position.z) && !float.IsInfinity(position.x + position.y + position.z);
    }
}
