using System;
using Game.Combat;
using Game.Enemies;
using Game.Net;
using UnityEditor;
using UnityEngine;

// Checks replicated boss warnings without granting the visual copy combat authority.
public static class NetBossPresentationChecks
{
    [MenuItem("Tools/Group Game/Check boss network presentation")]
    public static void Run()
    {
        var ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            var health = ghost.AddComponent<Health>();
            health.maxHealth = 20f;
            health.ResetHealth();
            var original = ghost.GetComponent<MeshRenderer>().sharedMaterial;
            var view = ghost.AddComponent<NetBossPresentation>();
            var state = new BossPresentationState
            {
                Stage = 2,
                Phase = 1,
                Invulnerable = true,
                BeamVisible = true,
                BeamLocked = true,
                BeamOrigin = new Vector3(3, 4, 5),
                BeamDirection = Vector3.right,
                HullTinted = true,
                HullColor = Color.red
            };
            view.Apply(state);
            var beam = ghost.GetComponentInChildren<LineRenderer>();
            Require(beam != null && beam.enabled, "Warning beam missing");
            Require(beam.GetPosition(0) == state.BeamOrigin && beam.GetPosition(1) == state.BeamOrigin + Vector3.right * 90f, "Beam geometry changed");
            Require(ghost.GetComponent<MotherShipBoss>() == null && ghost.GetComponent<Weapon>() == null,
                "Client presentation must not create authoritative combat");
            Require(health.CurrentHealth == 20f && !health.invulnerable, "Presentation changed health authority");
            Require(ghost.GetComponent<MeshRenderer>().sharedMaterial == original, "Presentation replaced group material");
            Require(ghost.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains("LASER LOCK"), "Locked warning label missing");
            state.Phase = 2;
            state.BeamFiring = true;
            view.Apply(state);
            Require(Mathf.Approximately(beam.startWidth, 2.4f) && beam.startColor.g < 0.2f, "Damaging beam did not replace warning");
            state.BeamVisible = false;
            state.HullTinted = false;
            view.Apply(state);
            Require(!beam.enabled && !ghost.GetComponent<MeshRenderer>().HasPropertyBlock(), "Old warning/tint was not cleared");
            Debug.Log("BOSS PRESENTATION CHECKS: PASS geometry, visual-only authority, material preservation and state cleanup");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ghost);
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
