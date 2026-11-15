using System;
using System.Collections.Generic;
namespace Game.Campaign
{
    // Indexed heap: insert, replace, remove and pop are O(log n); lookup is O(1).
    public sealed class IndexedMinHeap<T>
    {
        struct Entry
        {
            public int Id;
            public double Priority;
            public T Value;
        }
        readonly List<Entry> heap = new List<Entry>();
        readonly Dictionary<int, int> index = new Dictionary<int, int>();

        public int Count => heap.Count;

        public double Minimum => heap.Count == 0 ? double.PositiveInfinity : heap[0].Priority;

        // IDs break priority ties so equal-time events always have a stable order.
        bool Before(int firstIndex, int secondIndex) => heap[firstIndex].Priority < heap[secondIndex].Priority || heap[firstIndex].Priority == heap[secondIndex].Priority && heap[firstIndex].Id < heap[secondIndex].Id;

        void Swap(int firstIndex, int secondIndex)
        {
            var previousEntry = heap[firstIndex];
            heap[firstIndex] = heap[secondIndex];
            heap[secondIndex] = previousEntry;
            index[heap[firstIndex].Id] = firstIndex;
            index[heap[secondIndex].Id] = secondIndex;
        }

        int Up(int entryIndex)
        {
            while (entryIndex > 0)
            {
                int parentIndex = (entryIndex - 1) / 2;
                if (!Before(entryIndex, parentIndex))
                {
                    break;
                }

                Swap(entryIndex, parentIndex);
                entryIndex = parentIndex;
            }

            return entryIndex;
        }

        void Down(int entryIndex)
        {
            while (entryIndex * 2 + 1 < heap.Count)
            {
                int childIndex = entryIndex * 2 + 1;
                if (childIndex + 1 < heap.Count && Before(childIndex + 1, childIndex))
                {
                    childIndex++;
                }

                if (!Before(childIndex, entryIndex))
                {
                    break;
                }

                Swap(childIndex, entryIndex);
                entryIndex = childIndex;
            }
        }

        public void Set(int eventId, double priority, T value)
        {
            if (double.IsNaN(priority) || double.IsInfinity(priority))
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }

            // A replaced priority can move either direction; repair both sides of the heap.
            if (index.TryGetValue(eventId, out int entryIndex))
            {
                heap[entryIndex] = new Entry { Id = eventId, Priority = priority, Value = value };
                Down(Up(entryIndex));
            }
            else
            {
                index[eventId] = heap.Count;
                heap.Add(new Entry { Id = eventId, Priority = priority, Value = value });
                Up(heap.Count - 1);
            }
        }

        public bool Remove(int eventId)
        {
            if (!index.TryGetValue(eventId, out int entryIndex))
            {
                return false;
            }

            int lastIndex = heap.Count - 1;
            index.Remove(eventId);
            if (entryIndex == lastIndex)
            {
                heap.RemoveAt(lastIndex);
                return true;
            }

            // Fill the gap with the last entry and update its lookup before reordering.
            heap[entryIndex] = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            index[heap[entryIndex].Id] = entryIndex;
            Down(Up(entryIndex));
            return true;
        }

        public bool Pop(out int eventId, out T value)
        {
            if (heap.Count == 0)
            {
                eventId = 0;
                value = default(T);
                return false;
            }

            var minimumEntry = heap[0];
            eventId = minimumEntry.Id;
            value = minimumEntry.Value;
            Remove(eventId);
            return true;
        }

        public void Clear()
        {
            heap.Clear();
            index.Clear();
        }
    }

    public sealed class BattleSchedule
    {
        readonly IndexedMinHeap<Action> events = new IndexedMinHeap<Action>();
        readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();
        int nextId;
        public const int Capacity = 512;

        public int Count => events.Count;

        public double NextTime => events.Minimum;

        public int Schedule(double scheduledTime, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (Count >= Capacity)
            {
                throw new InvalidOperationException("Battle schedule capacity exceeded");
            }

            int eventId = ++nextId;
            events.Set(eventId, scheduledTime, callback);
            actions.Add(eventId, callback);
            return eventId;
        }

        public bool Cancel(int eventId)
        {
            actions.Remove(eventId);
            return events.Remove(eventId);
        }

        public bool Reschedule(int eventId, double scheduledTime)
        {
            if (!actions.TryGetValue(eventId, out var callback))
            {
                return false;
            }

            events.Set(eventId, scheduledTime, callback);
            return true;
        }

        public int Advance(double currentTime, int budget = 64)
        {
            int callbacksRun = 0;

            // The budget bounds work even when callbacks schedule more events for this tick.
            while (callbacksRun < budget && events.Count > 0 && events.Minimum <= currentTime)
            {
                events.Pop(out int eventId, out Action callback);
                actions.Remove(eventId);
                callbacksRun++;
                callback();
            }

            return callbacksRun;
        }

        public void Clear()
        {
            events.Clear();
            actions.Clear();
        }
    }
}
