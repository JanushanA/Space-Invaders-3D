using System;
using Game.AI;
using Game.Combat;
using Game.Enemies;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Checks trained-policy parity, bounded planning, squad movement and host-only AI ownership.
public static class AiChecks
{
    [Serializable]
    public class GoldenCase
    {
        public float[] observation;
        public float sample;
        public int action;
    }

    [Serializable]
    public class GoldenCases
    {
        public GoldenCase[] cases;
    }

    [MenuItem("Tools/Group Game/Check computing AI")]
    public static void Run()
    {
        // Use the exported inference fixtures to catch disagreement between training and C# inference.
        var policy = AcePolicy.Load();
        Require(policy != null, "trained policy loads");
        float[] observation = new float[12];
        observation[0] = 0.6f;
        observation[10] = 1;
        var golden = JsonUtility.FromJson<GoldenCases>(Resources.Load<TextAsset>("AI/AcePolicyCases").text);

        foreach (var fixture in golden.cases)
        {
            Require(policy.Decide(fixture.observation, fixture.sample) == fixture.action, "Python/C# categorical inference parity");
        }

        int action = policy.Decide(observation);
        Require(action >= 0 && action < 9, "finite neural action");

        // Bound the search work and replay its seeded choice before exercising squad movement.
        var planner = new BossPlanner(234);
        int attack = planner.Choose(24, 6, 1, false);
        Require(attack >= 0 && attack < 6 && planner.LastSimulations == 96, "bounded MCTS decision");
        Require(new BossPlanner(234).Choose(24, 6, 1, false) == attack, "seeded planning replay");
        var shipPositions = new[] { Vector3.right * 10, Vector3.zero };
        var slots = new[] { Vector3.zero, Vector3.right * 10 };
        int[] assignment = FormationSolver.Assign(shipPositions, slots);
        Require(assignment[0] == 1 && assignment[1] == 0, "minimum travel assignment");
        Vector3 limitedVelocity = AvoidanceSolver.Limit(Vector3.forward * 4, Vector3.right * 8, 12, 90, 0.1f);
        Require((limitedVelocity - Vector3.forward * 4).magnitude <= 1.201f, "acceleration bound");
        Require(Vector3.Angle(Vector3.forward, limitedVelocity) <= 9.01f, "turn rate bound");
        Require(AvoidanceSolver.CollisionRisk(Vector3.forward * 10, Vector3.back * 5, 2, 3) > 0, "head-on collision prediction");
        Require(AvoidanceSolver.CollisionRisk(Vector3.forward * 10, Vector3.forward * 5, 2, 3) == 0, "separating ships safe");
        var obstacleObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            obstacleObject.transform.position = Vector3.forward * 8;
            obstacleObject.transform.localScale = new Vector3(3, 5, 3);
            Physics.SyncTransforms();
            var obstacle = obstacleObject.GetComponent<Collider>();
            Vector3 position = Vector3.zero;
            Vector3 velocity = Vector3.zero;
            float minimumClearance = float.PositiveInfinity;

            for (int simulationStep = 0; simulationStep < 120; simulationStep++)
            {
                Vector3 desired = Vector3.ClampMagnitude((Vector3.forward * 22 - position) * 0.8f, 8);
                Vector3 next = AvoidanceSolver.Choose(position, velocity, desired, 0.1f, null, null, new[] { obstacle }, 1);
                Require((next - velocity).magnitude <= 1.201f, "avoidance honors acceleration during obstacle pass");
                velocity = next;
                position += velocity * 0.1f;
                minimumClearance = Mathf.Min(minimumClearance, (obstacle.ClosestPoint(position) - position).magnitude);
            }

            Require(minimumClearance > 1.1f, "predictive steering clears asteroid hull");
            Require(position.z > 12, "squadron passes asteroid instead of stopping indefinitely");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(obstacleObject);
        }

        // Measure repeated decisions separately from the behavior assertions.
        var timer = System.Diagnostics.Stopwatch.StartNew();

        for (int inferenceIteration = 0; inferenceIteration < 10000; inferenceIteration++)
        {
            policy.Decide(observation, 0.4567f);
        }

        timer.Stop();
        Debug.Log("AI inference 10000 decisions ms=" + timer.Elapsed.TotalMilliseconds.ToString("F2"));
        timer.Restart();

        for (int planningIteration = 0; planningIteration < 100; planningIteration++)
        {
            planner.Choose(24, 6, 1, false);
        }

        timer.Stop();
        Debug.Log("AI MCTS 100 decisions ms=" + timer.Elapsed.TotalMilliseconds.ToString("F2"));
        var bossObject = new GameObject("AiCheckBoss");
        try
        {
            bossObject.AddComponent<Health>();
            var boss = bossObject.AddComponent<MotherShipBoss>();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var laser = typeof(MotherShipBoss).GetMethod("LaserDamage", flags);
            var ramDamageMethod = typeof(MotherShipBoss).GetMethod("RamDamage", flags);
            Require(Mathf.Abs((float)laser.Invoke(boss, null) - 1.1f) < 0.0001f, "phase one beam reduction");
            Require(Mathf.Abs((float)ramDamageMethod.Invoke(boss, null) - 0.55f) < 0.0001f, "phase one ram reduction");
            typeof(MotherShipBoss).GetField("enraged", flags).SetValue(boss, true);
            Require(Mathf.Abs((float)laser.Invoke(boss, null) - 1.65f) < 0.0001f, "phase two beam reduction");
            Require(Mathf.Abs((float)ramDamageMethod.Invoke(boss, null) - 1.1f) < 0.0001f, "phase two ram reduction");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bossObject);
        }

        var ship = new GameObject("AiCheckShip");
        try
        {
            ship.AddComponent<Health>();
            var controller = ship.AddComponent<EnemyController>();
            var acePilot = AcePilot.Attach(ship);
            Require(acePilot != null && controller.enabled, "ace preserves enemy lifecycle");
            Require(Mathf.Abs(MotherShipBoss.OutgoingDamageScale - 0.55f) < 0.0001f, "boss damage scaling");
            bool active = Game.Net.NetSession.Active;
            bool host = Game.Net.NetSession.IsHost;
            try
            {
                Game.Net.NetSession.Active = true;

                // A guest must not create authoritative ace or squad behavior.
                Game.Net.NetSession.IsHost = false;
                Require(AcePilot.Attach(ship) == null, "guest cannot attach authoritative ace");
                Require(SquadronCoordinator.Create(new List<GameObject> { ship, ship }) == null, "guest cannot create squadron");
            }
            finally
            {
                Game.Net.NetSession.Active = active;
                Game.Net.NetSession.IsHost = host;
            }

            var second = new GameObject("AiCheckWingman");
            var target = new GameObject("AiCheckTarget");
            SquadronCoordinator squad = null;
            try
            {
                target.tag = "Player";
                second.AddComponent<Health>();
                second.AddComponent<EnemyController>();
                squad = SquadronCoordinator.Create(new List<GameObject> { ship, second });
                Require(squad != null && ship.GetComponent<SquadronMember>() != null, "actual squad attach hook");
                second.SetActive(false);
                typeof(SquadronCoordinator).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(squad, null);
                Require(squad.Members.Length == 1 && squad.Members[0].gameObject == ship, "casualty removes slot and reassigns survivors");
            }
            finally
            {
                if (squad != null)
                {
                    UnityEngine.Object.DestroyImmediate(squad.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ship);
        }

        Debug.Log("AI_CHECKS_OK: policy, bounded MCTS, assignment, predictive avoidance, acceleration, turn limits, attach hook");
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("AI check: " + message);
        }
    }
}
