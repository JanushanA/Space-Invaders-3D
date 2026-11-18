using System;
using System.Collections.Generic;
using Game.Campaign;
using Game.World;
using UnityEditor;
using UnityEngine;

// Checks campaign scheduling, travel gates and navigation, including physical escort obstacles.
public static class MissionSystemsChecks
{
    [MenuItem("Tools/Group Game/Check mission systems")]
    public static void Run()
    {
        HeapAndTimeline();
        SectorNetworks();
        LaunchGating();
        Navigation();
        CollisionAndReplanning();
        Debug.Log("MISSION_SYSTEMS_CHECKS_OK: indexed scheduling, connected seeded jump graph, spaced relay reachability, hazard A*, swept collision and live escort replanning");
    }

    static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new Exception("Mission systems: " + message);
        }
    }

    static void HeapAndTimeline()
    {
        var heap = new IndexedMinHeap<string>();
        heap.Set(2, 5, "two");
        heap.Set(1, 5, "one");
        heap.Set(3, 1, "three");
        heap.Set(2, 0.5, "changed");
        Require(heap.Pop(out int identifier, out string text) && identifier == 2 && text == "changed", "decrease key");
        heap.Set(3, 10, "later");
        Require(heap.Pop(out identifier, out _) && identifier == 1, "increase key repairs index");
        Require(heap.Remove(3) && heap.Count == 0 && !heap.Remove(3), "indexed cancellation");

        // Repeated replacements exercise the heap index before checking the final priority order.
        var random = new System.Random(17);
        var oracle = new SortedDictionary<int, double>();

        for (int heapOperation = 0; heapOperation < 1000; heapOperation++)
        {
            int key = random.Next(100);
            double priority = random.NextDouble();
            heap.Set(key, priority, key.ToString());
            oracle[key] = priority;
        }

        double previous = -1;

        while (heap.Pop(out identifier, out _))
        {
            Require(oracle[identifier] >= previous, "heap ordered after replacements");
            previous = oracle[identifier];
            oracle.Remove(identifier);
        }

        Require(oracle.Count == 0, "all events retained");

        // Scheduled callbacks must honor cancellation, changed times and a bounded amount of work per tick.
        var schedule = new BattleSchedule();
        var order = new List<int>();
        int cancelled = schedule.Schedule(1, () => order.Add(0));
        int delayed = schedule.Schedule(2, () => order.Add(2));
        schedule.Schedule(2, () => order.Add(3));
        Require(schedule.Cancel(cancelled) && schedule.Reschedule(delayed, 1), "cancel and reschedule event");
        Require(schedule.Advance(0.9) == 0, "countdown cannot fire early");
        schedule.Advance(2);
        Require(order.Count == 2 && order[0] == 2 && order[1] == 3, "scheduled execution order");
        Action recurring = null;
        recurring = () => schedule.Schedule(0, recurring);
        schedule.Schedule(0, recurring);
        Require(schedule.Advance(0, 8) == 8 && schedule.Count == 1, "recurring work is bounded per tick");
        schedule.Clear();
        Require(schedule.Count == 0, "scene cleanup cancels recurrence");
    }

    static void SectorNetworks()
    {
        // Replay each seed exactly, then compare seeds to confirm the jump topology can change.
        string first = null;
        bool changed = false;

        for (int seed = 1; seed <= 64; seed++)
        {
            var graph = new SectorNetwork(seed);
            var replay = new SectorNetwork(seed);
            var visited = new HashSet<int> { 1 };
            var queue = new Queue<int>();
            queue.Enqueue(1);

            while (queue.Count > 0)
            {
                foreach (int next in graph.Neighbours(queue.Dequeue()))
                {
                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            Require(visited.Count == 8 && graph.Edges.Count == 9, "Kruskal graph connects all missions with two extra choices");
            string signature = "";

            for (int edgeIndex = 0; edgeIndex < graph.Edges.Count; edgeIndex++)
            {
                var sectorLink = graph.Edges[edgeIndex];
                var replayedLink = replay.Edges[edgeIndex];
                Require(sectorLink.From == replayedLink.From && sectorLink.To == replayedLink.To && sectorLink.Cost == replayedLink.Cost, "same seed replays edges and weights");
                signature += sectorLink.From + "-" + sectorLink.To + ",";
            }

            if (first == null)
            {
                first = signature;
            }
            else
            {
                changed |= signature != first;
            }

            // Check physical mission reachability separately from the abstract jump network.
            var plan = SectorGenerator.Generate(seed, 1 + seed % 8);
            Require(SectorGenerator.Validate(plan, out _), "ordered generated mission solvable");

            for (int firstSector = 0; firstSector < 3; firstSector++)
            {
                for (int secondSector = firstSector + 1; secondSector < 3; secondSector++)
                {
                    Require(Vector3.Distance(plan.Position(plan.relays[firstSector]), plan.Position(plan.relays[secondSector])) >= 110, "relays spread across map");
                }
            }
        }

        Require(changed, "jump lanes change across run seeds");
    }

    static void LaunchGating()
    {
        var state = Game.Progression.PilotInventory.State;
        int savedSeed = state.sectorSeed;
        int savedCurrent = state.currentSector;
        int savedMask = state.completedSectorsMask;
        try
        {
            // Travel starts at sector one; an uncleared destination must allow retry without opening onward travel.
            state.sectorSeed = 81511;
            state.currentSector = 1;
            state.completedSectorsMask = 0;
            Require(CampaignSession.CanLaunch(1), "new run allows first mission");

            foreach (int destination in CampaignSession.Network.Neighbours(1))
            {
                Require(!CampaignSession.CanLaunch(destination), "new run locks adjacent sectors until first victory");
            }

            int current = CampaignSession.Network.Neighbours(1)[0];
            state.currentSector = current;
            state.completedSectorsMask = 1;
            Require(CampaignSession.CanLaunch(current), "uncleared current sector allows retry");

            foreach (int destination in CampaignSession.Network.Neighbours(current))
            {
                Require(!CampaignSession.CanLaunch(destination), "failed current mission cannot skip to adjacent sectors");
            }

            state.completedSectorsMask |= 1 << (current - 1);

            foreach (int destination in CampaignSession.Network.Neighbours(current))
            {
                Require(CampaignSession.CanLaunch(destination), "cleared current mission unlocks its graph jumps");
            }
        }
        finally
        {
            state.sectorSeed = savedSeed;
            state.currentSector = savedCurrent;
            state.completedSectorsMask = savedMask;
        }
    }

    static void Navigation()
    {
        var plan = new SectorPlan();
        var graph = new NavigationGraph(plan);
        int from = 6 * 13 + 2;
        int destinationNode = 6 * 13 + 10;

        // Establish the shortest route before adding danger costs, one obstacle and finally a complete barrier.
        var direct = graph.FindPath(from, destinationNode);
        Require(direct.Count == 8, "shortest empty route");
        int middle = 6 * 13 + 6;
        graph.SetHazard(middle, 1000);
        var safer = graph.FindPath(from, destinationNode);
        Require(!safer.Contains(middle) && safer.Count > direct.Count, "A* trades distance for hazard cost");
        graph.SetHazard(middle, 0);
        graph.SetBlocked(middle, true);
        Require(!graph.FindPath(from, destinationNode).Contains(middle), "new block excluded");

        for (int gridRow = 0; gridRow < 13; gridRow++)
        {
            graph.SetBlocked(gridRow * 13 + 6, true);
        }

        Require(graph.FindPath(from, destinationNode).Count == 0, "disconnected goal reports no route");
    }

    static void CollisionAndReplanning()
    {
        var root = new GameObject("Mission systems test fixture");
        root.transform.position = new Vector3(3500, 2500, 3500);
        var ship = new GameObject("Sweep test ship");
        ship.transform.SetParent(root.transform, false);
        var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rock.transform.SetParent(root.transform, false);
        rock.layer = LayerMask.NameToLayer(Game.Core.GameLayers.LevelGeometry);
        rock.transform.localPosition = Vector3.forward * 10;
        rock.transform.localScale = new Vector3(40, 40, 2);
        try
        {
            // A long movement step must stop at the wall while preserving sideways sliding.
            Physics.SyncTransforms();
            SweptMovement.Move(ship.transform, new Vector3(8, 0, 1000), 2);
            Require(ship.transform.localPosition.z < 7.1f && ship.transform.localPosition.x > 7, "boost cannot tunnel and tangential movement slides");
            root.transform.position += new Vector3(-2000, -2000, -2000);
            ship.transform.localPosition = Vector3.zero;
            Physics.SyncTransforms();
            SweptMovement.Move(ship.transform, Vector3.forward * 1000, 2);
            Require(ship.transform.localPosition.z < 7.1f, "shifted collider blocks boosted motion");
            ship.transform.localPosition = rock.transform.localPosition;
            SweptMovement.Move(ship.transform, Vector3.zero, 2);
            Require(!Physics.CheckSphere(ship.transform.position, 1.99f, SweptMovement.GeometryMask), "initial overlap resolves at " + ship.transform.localPosition);
            UnityEngine.Object.DestroyImmediate(rock);
            rock = null;

            // Replace the wall with a new obstacle on an active escort route and require eventual arrival.
            var plan = new SectorPlan();
            var goal = MissionBeaconAsset.Create(root.transform, MissionBeaconAsset.Kind.Relay, Color.cyan, "Navigation destination");
            goal.localPosition = plan.Position(12);
            Require(goal.GetComponentsInChildren<Collider>().Length == 0, "beacon optics leave convoy arrival volume unobstructed");
            ship.transform.localPosition = plan.Position(0);
            var escortNavigator = ship.AddComponent<EscortNavigator>();
            escortNavigator.Bind(plan, root.transform, goal);
            Require(escortNavigator.CurrentPath.Count > 1, "escort consumes navigation graph");
            int blockedCell = escortNavigator.CurrentPath[1];
            rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.layer = LayerMask.NameToLayer(Game.Core.GameLayers.LevelGeometry);
            rock.transform.SetParent(root.transform, false);
            rock.transform.localPosition = plan.Position(blockedCell);
            rock.transform.localScale = Vector3.one * 12;
            Physics.SyncTransforms();
            int before = escortNavigator.ReplanCount;
            escortNavigator.Tick(7, 0.1f);
            Require(escortNavigator.ReplanCount > before && !Contains(escortNavigator.CurrentPath, blockedCell), "new physical obstacle genuinely replans active escort route");
            Require(ship.GetComponent<LineRenderer>().positionCount > 2, "current path visible in world");

            for (int simulationStep = 0; simulationStep < 3000 && !escortNavigator.Arrived; simulationStep++)
            {
                if (simulationStep % 48 == 0)
                {
                    escortNavigator.Replan();
                }

                escortNavigator.Tick(7, 1f / 60);
            }

            Require(escortNavigator.Arrived, "periodic replanning retains forward progress and arrives around obstacle");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static bool Contains(IReadOnlyList<int> cells, int value)
    {
        foreach (int cell in cells)
        {
            if (cell == value)
            {
                return true;
            }
        }

        return false;
    }
}
