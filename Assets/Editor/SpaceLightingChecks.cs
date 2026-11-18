using System;
using Game.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Checks repair of invalid ambient lighting while retaining valid authored lighting settings.
public static class SpaceLightingChecks
{
    [MenuItem("Tools/Group Game/Check ambient probe repair")]
    public static void Run()
    {
        var original = RenderSettings.ambientProbe;
        var originalMode = RenderSettings.ambientMode;
        var originalAmbient = RenderSettings.ambientLight;
        var originalSkybox = RenderSettings.skybox;
        float originalReflection = RenderSettings.reflectionIntensity;
        try
        {
            // Flat mode returns Unity's generated constant probe rather than our injected SH.
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientLight = new Color(0.2f, 0.3f, 0.4f);
            var invalid = new SphericalHarmonicsL2();
            invalid[1, 4] = float.NaN;
            invalid[2, 8] = float.PositiveInfinity;
            RenderSettings.ambientProbe = invalid;
            var injected = RenderSettings.ambientProbe;
            Require(float.IsNaN(injected[1, 4]) && float.IsInfinity(injected[2, 8]), "Fixture failed to inject non-finite probe");
            SpaceLighting.EnsureFiniteAmbientProbe();
            var repaired = RenderSettings.ambientProbe;
            var expected = new SphericalHarmonicsL2();
            expected.AddAmbientLight(RenderSettings.ambientLight);

            for (int channelIndex = 0; channelIndex < 3; channelIndex++)
            {
                for (int coefficientIndex = 0; coefficientIndex < 9; coefficientIndex++)
                {
                    Require(!float.IsNaN(repaired[channelIndex, coefficientIndex]) && !float.IsInfinity(repaired[channelIndex, coefficientIndex]) &&
                    Mathf.Abs(repaired[channelIndex, coefficientIndex] - expected[channelIndex, coefficientIndex]) < 0.00001f, "Invalid probe not repaired from authored ambient");
                }
            }

            var authored = new SphericalHarmonicsL2();

            for (int channelIndex2 = 0; channelIndex2 < 3; channelIndex2++)
            {
                for (int savedCoefficientIndex = 0; savedCoefficientIndex < 9; savedCoefficientIndex++)
                {
                    authored[channelIndex2, savedCoefficientIndex] = (channelIndex2 * 9 + savedCoefficientIndex - 12) * 0.125f;
                }
            }

            RenderSettings.ambientProbe = authored;
            SpaceLighting.EnsureFiniteAmbientProbe();
            var preserved = RenderSettings.ambientProbe;

            for (int channelIndex3 = 0; channelIndex3 < 3; channelIndex3++)
            {
                for (int restoredCoefficientIndex = 0; restoredCoefficientIndex < 9; restoredCoefficientIndex++)
                {
                    Require(preserved[channelIndex3, restoredCoefficientIndex] == authored[channelIndex3, restoredCoefficientIndex], "Valid authored probe was changed");
                }
            }

            Require(RenderSettings.skybox == originalSkybox && RenderSettings.reflectionIntensity == originalReflection &&
                RenderSettings.ambientMode == AmbientMode.Skybox && RenderSettings.ambientLight == new Color(0.2f, 0.3f, 0.4f),
                "Repair changed unrelated lighting settings");
            Debug.Log("SPACE LIGHTING CHECKS: PASS non-finite repair, finite authored probe preservation and unrelated lighting preservation");
        }
        finally
        {
            RenderSettings.ambientMode = originalMode;
            RenderSettings.ambientLight = originalAmbient;
            RenderSettings.skybox = originalSkybox;
            RenderSettings.reflectionIntensity = originalReflection;
            RenderSettings.ambientProbe = original;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
