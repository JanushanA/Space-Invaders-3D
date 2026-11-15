using System;
using System.Collections.Generic;
namespace Game.Progression
{
    public readonly struct PriceEntry
    {
        public readonly int Id;
        public readonly int Price;

        public PriceEntry(int entryId, int price)
        {
            if (price < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(price));
            }

            Id = entryId;
            Price = price;
        }
    }

    // AVL catalogue: mutations O(log n), inclusive sorted price ranges O(log n + k).
    // The item ID breaks ties, so equal-price stock never overwrites another listing.
    public sealed class PriceCatalogue
    {
        sealed class Node
        {
            public PriceEntry Item;
            public Node Left;
            public Node Right;
            public int Height = 1;

            public Node(PriceEntry item)
            {
                Item = item;
            }
        }
        Node root;

        public int Count
        {
            get; private set;
        }

        static int Height(Node node) => node == null ? 0 : node.Height;

        static void Refresh(Node node)
        {
            node.Height = 1 + Math.Max(Height(node.Left), Height(node.Right));
        }

        static int Compare(PriceEntry first, PriceEntry second)
        {
            int priceComparison = first.Price.CompareTo(second.Price);
            return priceComparison != 0 ? priceComparison : first.Id.CompareTo(second.Id);
        }

        static Node Right(Node node)
        {
            var promotedNode = node.Left;
            node.Left = promotedNode.Right;
            promotedNode.Right = node;
            Refresh(node);
            Refresh(promotedNode);
            return promotedNode;
        }

        static Node Left(Node node)
        {
            var promotedNode = node.Right;
            node.Right = promotedNode.Left;
            promotedNode.Left = node;
            Refresh(node);
            Refresh(promotedNode);
            return promotedNode;
        }

        // Rotations restore the height limit without changing the catalogue's sorted order.
        static Node Balance(Node node)
        {
            if (node == null)
            {
                return null;
            }

            Refresh(node);
            int balance = Height(node.Left) - Height(node.Right);
            if (balance > 1)
            {
                if (Height(node.Left.Left) < Height(node.Left.Right))
                {
                    node.Left = Left(node.Left);
                }

                return Right(node);
            }

            if (balance < -1)
            {
                if (Height(node.Right.Right) < Height(node.Right.Left))
                {
                    node.Right = Right(node.Right);
                }

                return Left(node);
            }

            return node;
        }

        public void Insert(PriceEntry item)
        {
            bool added = false;
            root = Insert(root, item, ref added);
            if (added)
            {
                Count++;
            }
        }

        static Node Insert(Node node, PriceEntry item, ref bool added)
        {
            if (node == null)
            {
                added = true;
                return new Node(item);
            }

            int comparison = Compare(item, node.Item);
            if (comparison < 0)
            {
                node.Left = Insert(node.Left, item, ref added);
            }
            else if (comparison > 0)
            {
                node.Right = Insert(node.Right, item, ref added);
            }
            else
            {
                node.Item = item;
            }

            return Balance(node);
        }

        public bool Remove(int price, int entryId)
        {
            bool removed = false;
            root = Remove(root, new PriceEntry(entryId, price), ref removed);
            if (removed)
            {
                Count--;
            }

            return removed;
        }

        static Node Remove(Node node, PriceEntry item, ref bool removed)
        {
            if (node == null)
            {
                return null;
            }

            int comparison = Compare(item, node.Item);
            if (comparison < 0)
            {
                node.Left = Remove(node.Left, item, ref removed);
            }
            else if (comparison > 0)
            {
                node.Right = Remove(node.Right, item, ref removed);
            }
            else
            {
                removed = true;
                if (node.Left == null)
                {
                    return node.Right;
                }

                if (node.Right == null)
                {
                    return node.Left;
                }

                // Replace a two-child node with the next entry in sorted order.
                var successor = node.Right;

                while (successor.Left != null)
                {
                    successor = successor.Left;
                }

                node.Item = successor.Item;
                bool successorRemoved = false;
                node.Right = Remove(node.Right, successor.Item, ref successorRemoved);
            }

            return Balance(node);
        }

        public List<PriceEntry> Range(int minimum, int maximum)
        {
            var result = new List<PriceEntry>();
            if (minimum <= maximum)
            {
                Range(root, minimum, maximum, result);
            }

            return result;
        }

        static void Range(Node node, int minimum, int maximum, List<PriceEntry> result)
        {
            if (node == null)
            {
                return;
            }

            // Skip subtrees whose prices cannot fall within the requested bounds.
            if (node.Item.Price >= minimum)
            {
                Range(node.Left, minimum, maximum, result);
            }

            if (node.Item.Price >= minimum && node.Item.Price <= maximum)
            {
                result.Add(node.Item);
            }

            if (node.Item.Price <= maximum)
            {
                Range(node.Right, minimum, maximum, result);
            }
        }

        public bool IsBalanced()
        {
            return Check(root, out _, out var count) && count == Count;
        }

        static bool Check(Node node, out int height, out int count)
        {
            height = 0;
            count = 0;
            if (node == null)
            {
                return true;
            }

            bool leftBalanced = Check(node.Left, out int leftHeight, out int leftCount);
            bool rightBalanced = Check(node.Right, out int rightHeight, out int rightCount);
            height = 1 + Math.Max(leftHeight, rightHeight);
            count = 1 + leftCount + rightCount;
            return leftBalanced && rightBalanced && height == node.Height && Math.Abs(leftHeight - rightHeight) <= 1;
        }
    }

    // Integer prefix-sum tree: dynamic weights and inverse cumulative sampling O(log n).
    public sealed class FenwickWeights
    {
        readonly int[] tree;
        readonly int[] values;

        public int Total
        {
            get; private set;
        }

        public FenwickWeights(int size)
        {
            if (size < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            tree = new int[size + 1];
            values = new int[size];
        }

        public void Set(int index, int value)
        {
            if (index < 0 || index >= values.Length || value < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            int change = value - values[index];
            checked
            {
                Total += change;
            }
            values[index] = value;

            // Each step visits the next prefix-sum bucket that includes this item.
            for (int treeIndex = index + 1; treeIndex < tree.Length; treeIndex += treeIndex & -treeIndex)
            {
                tree[treeIndex] += change;
            }
        }

        public int Sample(int ticket)
        {
            if (ticket < 0 || ticket >= Total)
            {
                throw new ArgumentOutOfRangeException(nameof(ticket));
            }

            // Walk prefix sums from the largest step down to find the ticket's bucket.
            int index = 0;
            int searchStep = 1;

            while ((searchStep << 1) < tree.Length)
            {
                searchStep <<= 1;
            }

            for (; searchStep != 0; searchStep >>= 1)
            {
                int candidateIndex = index + searchStep;
                if (candidateIndex < tree.Length && tree[candidateIndex] <= ticket)
                {
                    ticket -= tree[candidateIndex];
                    index = candidateIndex;
                }
            }

            return index;
        }
    }
}
