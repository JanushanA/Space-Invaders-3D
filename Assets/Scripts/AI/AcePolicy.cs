using System;
using UnityEngine;
namespace Game.AI
{
    [Serializable]
    public sealed class AcePolicy
    {
        // Field names are the on-disk contract shared with Tools/TrainAce.py.
        public int version;
        public int inputs;
        public int hidden;
        public int actions;
        public float[] w1;
        public float[] b1;
        public float[] w2;
        public float[] b2;
        [NonSerialized]
        readonly float[] activation = new float[24];
        [NonSerialized]
        readonly float[] logits = new float[9];

        public static AcePolicy Load()
        {
            TextAsset asset = Resources.Load<TextAsset>("AI/AcePolicy");
            if (asset == null)
            {
                return null;
            }

            AcePolicy policy = JsonUtility.FromJson<AcePolicy>(asset.text);
            if (policy == null || policy.version != 1
                || policy.inputs != 12 || policy.hidden != 24 || policy.actions != 9
                || policy.w1 == null || policy.w1.Length != 288
                || policy.b1 == null || policy.b1.Length != 24
                || policy.w2 == null || policy.w2.Length != 216
                || policy.b2 == null || policy.b2.Length != 9)
            {
                return null;
            }

            // Reject invalid weights before they can propagate through steering and aim.
            foreach (var parameters in new[] { policy.w1, policy.b1, policy.w2, policy.b2 })
            {
                foreach (float parameter in parameters)
                {
                    if (float.IsNaN(parameter) || float.IsInfinity(parameter))
                    {
                        return null;
                    }
                }
            }

            return policy;
        }

        public int Decide(float[] observation, float sample = -1)
        {
            // The flattened weights store all outputs for each input consecutively.
            for (int hiddenIndex = 0; hiddenIndex < 24; hiddenIndex++)
            {
                float value = b1[hiddenIndex];

                for (int inputIndex = 0; inputIndex < 12; inputIndex++)
                {
                    value += Mathf.Clamp(observation[inputIndex], -2, 2) * w1[inputIndex * 24 + hiddenIndex];
                }

                activation[hiddenIndex] = (float)Math.Tanh(value);
            }

            int bestAction = 0;
            float maximum = float.NegativeInfinity;

            for (int actionIndex = 0; actionIndex < 9; actionIndex++)
            {
                float value = b2[actionIndex];

                for (int hiddenIndex = 0; hiddenIndex < 24; hiddenIndex++)
                {
                    value += activation[hiddenIndex] * w2[hiddenIndex * 9 + actionIndex];
                }

                logits[actionIndex] = value;
                if (value > maximum)
                {
                    maximum = value;
                    bestAction = actionIndex;
                }
            }

            // A supplied sample selects from softmax probabilities; otherwise use the largest logit.
            if (sample >= 0)
            {
                float probabilityTotal = 0;

                for (int actionIndex = 0; actionIndex < 9; actionIndex++)
                {
                    logits[actionIndex] = Mathf.Exp(logits[actionIndex] - maximum);
                    probabilityTotal += logits[actionIndex];
                }

                float threshold = Mathf.Clamp01(sample) * probabilityTotal;

                for (int actionIndex = 0; actionIndex < 9; actionIndex++)
                {
                    threshold -= logits[actionIndex];
                    if (threshold <= 0)
                    {
                        return actionIndex;
                    }
                }

                return 8;
            }

            return bestAction;
        }
    }
}
