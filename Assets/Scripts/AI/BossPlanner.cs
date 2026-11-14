using System;
using UnityEngine;
namespace Game.AI
{
    // Fixed allocation UCT tree: 96 rollouts, four attack decisions, six actions.
    public sealed class BossPlanner
    {
        const int Budget = 96;
        const int Depth = 4;
        readonly System.Random random;
        readonly int[,] children = new int[Budget + 1, 6];
        readonly int[] visits = new int[Budget + 1];
        readonly float[] values = new float[Budget + 1];
        readonly int[] path = new int[Depth + 1];
        int nodes;

        public int LastSimulations
        {
            get; private set;
        }

        struct State
        {
            public float range;
            public float speed;
            public float pressure;
            public float stamina;
        }

        public BossPlanner(int seed)
        {
            random = new System.Random(seed);
        }

        public int Choose(float range, float lateralSpeed, float healthFraction, bool enraged)
        {
            Array.Clear(children, 0, children.Length);
            Array.Clear(visits, 0, visits.Length);
            Array.Clear(values, 0, values.Length);
            nodes = 1;

            for (int iteration = 0; iteration < Budget; iteration++)
            {
                State state = new State
                {
                    range = Mathf.Clamp(range, 4, 80),
                    speed = Mathf.Abs(lateralSpeed),
                    pressure = 1 - healthFraction,
                    stamina = 1
                };
                int node = 0;
                int pathLength = 1;
                path[0] = 0;
                float score = 0;
                float discount = 1;
                bool expanded = false;

                // Expand at most one new tree node, then finish the rollout with random actions.
                for (int depth = 0; depth < Depth; depth++)
                {
                    int action;
                    if (expanded)
                    {
                        action = random.Next(6);
                    }
                    else
                    {
                        action = Select(node);
                        if (children[node, action] == 0)
                        {
                            children[node, action] = nodes++;
                            expanded = true;
                        }

                        node = children[node, action];
                        path[pathLength++] = node;
                    }

                    score += discount * Respond(ref state, action, enraged);
                    discount *= 0.85f;
                }

                // Credit every visited node with the same discounted rollout score.
                for (int pathIndex = 0; pathIndex < pathLength; pathIndex++)
                {
                    visits[path[pathIndex]]++;
                    values[path[pathIndex]] += score;
                }
            }

            LastSimulations = Budget;

            // Choose the root attack with the highest observed average reward.
            int bestAction = 0;
            float bestScore = float.NegativeInfinity;

            for (int actionIndex = 0; actionIndex < 6; actionIndex++)
            {
                int childNode = children[0, actionIndex];
                float value = childNode == 0 ? float.NegativeInfinity : values[childNode] / visits[childNode];
                if (value > bestScore)
                {
                    bestScore = value;
                    bestAction = actionIndex;
                }
            }

            return bestAction;
        }

        int Select(int node)
        {
            int bestAction = 0;
            double bestScore = double.NegativeInfinity;

            for (int actionIndex = 0; actionIndex < 6; actionIndex++)
            {
                int childNode = children[node, actionIndex];
                if (childNode == 0)
                {
                    return actionIndex;
                }

                // Favor strong average rewards while still exploring less-visited attacks.
                double explorationScore = values[childNode] / visits[childNode] + 1.4 * Math.Sqrt(Math.Log(visits[node] + 1.0) / visits[childNode]);
                if (explorationScore > bestScore)
                {
                    bestScore = explorationScore;
                    bestAction = actionIndex;
                }
            }

            return bestAction;
        }

        float Respond(ref State state, int action, bool enraged)
        {
            // Sample evade, reverse, or close-in response. Pressure persists into future attacks.
            int response = random.Next(3);
            float evasionSpeed = state.speed * (response == 0 ? 1.2f : response == 1 ? 0.65f : 0.25f) * state.stamina;
            float hitProbability;
            switch (action)
            {
                case 0:
                    hitProbability = Mathf.Clamp01(0.8f - evasionSpeed * 0.08f + state.pressure * 0.15f);

                    break; // aimed beam
                case 1:
                    hitProbability = Mathf.Clamp01(0.35f + evasionSpeed * 0.035f + state.pressure * 0.2f);

                    break; // sweep catches lateral dodge
                case 2:
                    state.pressure = Mathf.Min(2, state.pressure + 0.65f);
                    hitProbability = 0.16f;

                    break; // escorts constrain later responses
                case 3:
                    hitProbability = Mathf.Clamp01(0.8f - state.range * 0.018f + state.pressure * 0.1f);
                    state.range += response == 2 ? -4 : 5;

                    break; // charge
                case 4:
                    hitProbability = Mathf.Clamp01(0.65f - state.range * 0.009f + evasionSpeed * 0.02f);

                    break; // nova
                default:
                    hitProbability = Mathf.Clamp01(0.5f - evasionSpeed * 0.035f + state.pressure * 0.12f);

                    break; // volley
            }

            state.stamina = Mathf.Clamp01(state.stamina - 0.13f + (response == 2 ? 0.18f : 0));
            state.pressure *= 0.85f;
            state.range = Mathf.Clamp(state.range + (response == 2 ? -5 : 3), 5, 65);
            return hitProbability * (enraged ? 1.2f : 1) - 0.08f;
        }
    }
}
