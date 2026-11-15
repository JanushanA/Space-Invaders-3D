using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Campaign
{
    [Serializable]
    public sealed class SectorPlan
    {
        public const int Size = 13;
        public const float Spacing = 22;
        public bool[] blocked = new bool[Size * Size];
        public int[] objectives = new int[3];
        public int[] relays = new int[3];
        public List<int> encounters = new List<int>();
        public int start = Size * (Size / 2) + Size / 2;
        public int seed;
        public int repairs;

        public Vector3 Position(int index) => new Vector3((index % Size - Size / 2) * Spacing, 0, (index / Size - Size / 2) * Spacing);
    }

    public static class SectorGenerator
    {
        public static SectorPlan Generate(int seed, int difficulty)
        {
            for (int retry = 0; retry < 12; retry++)
            {
                var random = new System.Random(unchecked(seed + retry * 7919));
                var plan = new SectorPlan { seed = seed };

                for (int index = 0; index < plan.blocked.Length; index++)
                {
                    plan.blocked[index] = plan.Position(index).magnitude > 48 && random.NextDouble() < 0.22;
                }

                var candidateCells = new List<int>();

                for (int index = 0; index < plan.blocked.Length; index++)
                {
                    if (!plan.blocked[index] && plan.Position(index).magnitude > 55)
                    {
                        candidateCells.Add(index);
                    }
                }

                // Shuffle once per attempt so backtracking is deterministic for the same seed.
                for (int index = candidateCells.Count - 1; index > 0; index--)
                {
                    int otherIndex = random.Next(index + 1);
                    int previousCell = candidateCells[index];
                    candidateCells[index] = candidateCells[otherIndex];
                    candidateCells[otherIndex] = previousCell;
                }

                if (!Assign(plan, candidateCells, 0))
                {
                    continue;
                }

                if (!AssignRelays(plan, candidateCells, seed))
                {
                    continue;
                }

                // Keep the relay chain open before validating the full objective sequence.
                int previous = plan.objectives[0];

                foreach (int relay in plan.relays)
                {
                    ClearCorridor(plan, previous, relay);
                    previous = relay;
                }

                ClearCorridor(plan, previous, plan.objectives[2]);

                // Propagate objective clearance constraints before placing difficulty-budgeted encounters.
                foreach (int point in plan.objectives)
                {
                    foreach (int adjacent in Neighbours(point))
                    {
                        plan.blocked[adjacent] = false;
                    }
                }

                foreach (int cell in candidateCells)
                {
                    if (!plan.blocked[cell] && plan.Position(cell).magnitude > 66 && Array.IndexOf(plan.objectives, cell) < 0 && plan.encounters.Count < Mathf.Clamp(difficulty + 1, 2, 9))
                    {
                        plan.encounters.Add(cell);
                    }
                }

                if (Validate(plan, out _))
                {
                    return plan;
                }

                // Repair with Manhattan corridors; ship radius 2m fits inside 22m cells with <=7m rocks.
                int previousObjective = plan.start;

                foreach (int goal in plan.objectives)
                {
                    int column = previousObjective % SectorPlan.Size;
                    int row = previousObjective / SectorPlan.Size;

                    while (column != goal % SectorPlan.Size)
                    {
                        column += Math.Sign(goal % SectorPlan.Size - column);
                        plan.blocked[row * SectorPlan.Size + column] = false;
                        plan.repairs++;
                    }

                    while (row != goal / SectorPlan.Size)
                    {
                        row += Math.Sign(goal / SectorPlan.Size - row);
                        plan.blocked[row * SectorPlan.Size + column] = false;
                        plan.repairs++;
                    }

                    previousObjective = goal;
                }

                if (Validate(plan, out _))
                {
                    return plan;
                }
            }

            throw new InvalidOperationException("No valid sector after bounded regeneration");
        }

        static bool AssignRelays(SectorPlan plan, List<int> candidateCells, int seed)
        {
            for (int relay = 0; relay < 3; relay++)
            {
                var random = new System.Random(unchecked(seed ^ (104729 * (relay + 1))));
                var candidates = new List<int>(candidateCells);

                for (int index = candidates.Count - 1; index > 0; index--)
                {
                    int otherIndex = random.Next(index + 1);
                    int previousCell = candidates[index];
                    candidates[index] = candidates[otherIndex];
                    candidates[otherIndex] = previousCell;
                }

                bool assigned = false;

                foreach (int cell in candidates)
                {
                    bool clear = Array.IndexOf(plan.objectives, cell) < 0;

                    for (int index = 0; index < relay; index++)
                    {
                        if (Vector3.Distance(plan.Position(cell), plan.Position(plan.relays[index])) < 110)
                        {
                            clear = false;
                        }
                    }

                    foreach (int objective in plan.objectives)
                    {
                        if (Vector3.Distance(plan.Position(cell), plan.Position(objective)) < 35)
                        {
                            clear = false;
                        }
                    }

                    if (!clear)
                    {
                        continue;
                    }

                    plan.relays[relay] = cell;
                    assigned = true;
                    break;
                }

                if (!assigned)
                {
                    return false;
                }
            }

            return true;
        }

        static void ClearCorridor(SectorPlan plan, int from, int goal)
        {
            int column = from % SectorPlan.Size;
            int row = from / SectorPlan.Size;
            plan.blocked[from] = false;

            while (column != goal % SectorPlan.Size)
            {
                column += Math.Sign(goal % SectorPlan.Size - column);
                plan.blocked[row * SectorPlan.Size + column] = false;
            }

            while (row != goal / SectorPlan.Size)
            {
                row += Math.Sign(goal / SectorPlan.Size - row);
                plan.blocked[row * SectorPlan.Size + column] = false;
            }

            foreach (int neighbourCell in Neighbours(goal))
            {
                plan.blocked[neighbourCell] = false;
            }
        }

        static bool Assign(SectorPlan plan, List<int> candidateCells, int depth)
        {
            // Try each valid placement, backing up when later objectives have no compatible cell.
            if (depth == 3)
            {
                return true;
            }

            foreach (int cell in candidateCells)
            {
                bool compatible = true;

                for (int otherIndex = 0; otherIndex < depth; otherIndex++)
                {
                    if (Vector3.Distance(plan.Position(cell), plan.Position(plan.objectives[otherIndex])) < 55)
                    {
                        compatible = false;
                        break;
                    }
                }

                if (!compatible)
                {
                    continue;
                }

                plan.objectives[depth] = cell;
                if (Assign(plan, candidateCells, depth + 1))
                {
                    return true; // Backtracks if later variables exhaust their domain.

                } // Backtracks if later variables exhaust their domain.
            }

            return false;
        }

        public static IEnumerable<int> Neighbours(int cell)
        {
            int column = cell % SectorPlan.Size;
            int row = cell / SectorPlan.Size;
            if (column > 0)
            {
                yield return cell - 1;
            }

            if (column < SectorPlan.Size - 1)
            {
                yield return cell + 1;
            }

            if (row > 0)
            {
                yield return cell - SectorPlan.Size;
            }

            if (row < SectorPlan.Size - 1)
            {
                yield return cell + SectorPlan.Size;
            }
        }

        public static List<int> Route(SectorPlan sectorPlan, int from, int destinationNode)
        {
            return new NavigationGraph(sectorPlan).FindPath(from, destinationNode);
        }

        public static bool Validate(SectorPlan sectorPlan, out string reason)
        {
            reason = "";
            if (sectorPlan == null || sectorPlan.blocked == null || sectorPlan.blocked.Length != 169 || sectorPlan.objectives == null || sectorPlan.objectives.Length != 3 || sectorPlan.relays == null || sectorPlan.relays.Length != 3)
            {
                reason = "Invalid grid";
                return false;
            }

            for (int index = 0; index < 3; index++)
            {
                if (sectorPlan.objectives[index] < 0 || sectorPlan.objectives[index] >= 169 || sectorPlan.blocked[sectorPlan.objectives[index]])
                {
                    reason = "Blocked objective";
                    return false;
                }
            }

            if (sectorPlan.objectives[0] == sectorPlan.objectives[1] || sectorPlan.objectives[1] == sectorPlan.objectives[2] || sectorPlan.objectives[0] == sectorPlan.objectives[2])
            {
                reason = "Overlapping objectives";
                return false;
            }

            if (sectorPlan.start < 0 || sectorPlan.start >= 169 || sectorPlan.blocked[sectorPlan.start])
            {
                reason = "Blocked start";
                return false;
            }

            foreach (int encounterCell in sectorPlan.encounters)
            {
                if (encounterCell < 0 || encounterCell >= 169 || sectorPlan.Position(encounterCell).magnitude < 48 || sectorPlan.blocked[encounterCell])
                {
                    reason = "Unsafe encounter";
                    return false;
                }
            }

            foreach (int relay in sectorPlan.relays)
            {
                if (relay < 0 || relay >= 169 || sectorPlan.blocked[relay] || Route(sectorPlan, sectorPlan.start, relay).Count == 0)
                {
                    reason = "Unreachable relay";
                    return false;
                }
            }

            // Independent state-space BFS: state includes resource/dependency stage. A reachable goal alone is insufficient.
            // Encode each search state as the objective stage followed by its grid-cell index.
            var queue = new Queue<int>();
            var visited = new bool[169 * 4];
            queue.Enqueue(sectorPlan.start);
            visited[sectorPlan.start] = true;

            while (queue.Count > 0)
            {
                int state = queue.Dequeue();
                int stage = state / 169;
                int cell = state % 169;
                if (stage == 3)
                {
                    return true;
                }

                if (cell == sectorPlan.objectives[stage])
                {
                    int nextState = (stage + 1) * 169 + cell;
                    if (!visited[nextState])
                    {
                        visited[nextState] = true;
                        queue.Enqueue(nextState);
                    }
                }

                foreach (int neighbourCell in Neighbours(cell))
                {
                    // Relay and extraction gates require the previously recovered resource.
                    if (sectorPlan.blocked[neighbourCell] || (neighbourCell == sectorPlan.objectives[1] && stage < 1) || (neighbourCell == sectorPlan.objectives[2] && stage < 2))
                    {
                        continue;
                    }

                    int nextState = stage * 169 + neighbourCell;
                    if (!visited[nextState])
                    {
                        visited[nextState] = true;
                        queue.Enqueue(nextState);
                    }
                }
            }

            reason = "Resource-ordered route is unreachable";
            return false;
        }

        public static void VerifySeeds()
        {
            for (int seed = 0; seed < 256; seed++)
            {
                var firstPlan = Generate(seed, 1 + seed % 8);
                if (!Validate(firstPlan, out string error))
                {
                    throw new Exception(error);
                }

                var repeatedPlan = Generate(seed, 1 + seed % 8);
                if (JsonUtility.ToJson(firstPlan) != JsonUtility.ToJson(repeatedPlan))
                {
                    throw new Exception("Seed not deterministic");
                }
            }

            var broken = Generate(1, 1);

            foreach (int neighbourCell in Neighbours(broken.start))
            {
                broken.blocked[neighbourCell] = true;
            }

            if (Validate(broken, out _))
            {
                throw new Exception("Isolated start accepted");
            }

            broken = Generate(2, 1);
            broken.objectives[2] = broken.objectives[0];
            if (Validate(broken, out _))
            {
                throw new Exception("Overlapping objective accepted");
            }
        }
    }
}
