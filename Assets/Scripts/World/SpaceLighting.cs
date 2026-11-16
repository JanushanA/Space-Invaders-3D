using UnityEngine;
using UnityEngine.Rendering;
namespace Game.World
{
    // Some imported baked environments contain NaN SH coefficients in players.
    // Preserve authored lighting and replace only an unusable ambient probe.
    public static class SpaceLighting
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            Camera.onPreCull -= BeforeCamera;
#if UNITY_EDITOR || DEVELOPMENT_BUILD

            // Keep the original faulty state reproducible in the opt-in A/B probe.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-integrationVisualProbe") >= 0)
            {
                return;
            }
#endif

            Camera.onPreCull += BeforeCamera;
            EnsureFiniteAmbientProbe();
        }

        private static void BeforeCamera(Camera camera)
        {
            EnsureFiniteAmbientProbe();
        }

        public static void EnsureFiniteAmbientProbe()
        {
            var probe = RenderSettings.ambientProbe;
            bool invalid = false;

            for (int channel = 0; channel < 3; channel++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    float value = probe[channel, coefficient];
                    invalid |= float.IsNaN(value) || float.IsInfinity(value);
                }
            }

            if (!invalid)
            {
                return;
            }

            Color ambient = RenderSettings.ambientLight;
            ambient.r = FiniteIntensity(ambient.r);
            ambient.g = FiniteIntensity(ambient.g);
            ambient.b = FiniteIntensity(ambient.b);
            var replacement = new SphericalHarmonicsL2();
            replacement.AddAmbientLight(ambient);
            RenderSettings.ambientProbe = replacement;
            Debug.Log("[SpaceLighting] Repaired non-finite ambient probe using the scene's ambient color.");
        }

        private static float FiniteIntensity(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0.2f : Mathf.Max(0f, value);
        }
    }
}
