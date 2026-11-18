using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Net;
using Game.Player;
using Game.Progression;
using UnityEditor;
using UnityEngine;

// Exercises equipment changes on real player components while restoring the prior inventory afterward.
public static class EquipmentChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Group Game/Check equipment and skill composition")]
    public static void Run()
    {
        bool suppress = GameSession.SuppressSave;
        bool active = NetSession.Active;
        var stateField = typeof(PilotInventory).GetField("state", BindingFlags.Static | BindingFlags.NonPublic);
        var suppressedField = typeof(PilotInventory).GetField("suppressed", BindingFlags.Static | BindingFlags.NonPublic);
        object previousState = stateField.GetValue(null);
        object previousSuppressed = suppressedField.GetValue(null);
        var fixture = new GameObject("EquipmentChecks fixture");
        WeaponDefinition original = null;
        WeaponDefinition instance = null;
        PilotEquipment equipment = null;
        try
        {
            GameSession.SuppressSave = true;
            NetSession.Active = false;
            PilotInventory.Reload();
            var inventory = PilotInventory.State;
            inventory.ship = 2;
            inventory.upgrade = 3;
            inventory.modules[0] = 3;
            inventory.modules[1] = 3;
            var health = fixture.AddComponent<Health>();
            health.maxHealth = 3;
            health.ResetHealth();
            var pilot = fixture.AddComponent<PlayerController>();
            pilot.captureCursor = false;
            pilot.moveSpeed = 10;
            pilot.verticalSpeed = 5;
            var tree = fixture.AddComponent<SkillTree>();
            var skills = fixture.AddComponent<PlayerSkills>();
            ((HashSet<SkillId>)typeof(SkillTree).GetField("unlocked", Private).GetValue(tree)).UnionWith(new[] { SkillId.Speed1, SkillId.Speed2 });
            Set(skills, "tree", tree);
            Set(skills, "player", pilot);
            Set(skills, "health", health);
            Set(skills, "baseMoveSpeed", 10f);
            Set(skills, "baseVerticalSpeed", 5f);
            var weapon = fixture.AddComponent<Weapon>();
            original = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.definition = original;
            equipment = fixture.AddComponent<PilotEquipment>();
            Call(equipment, "Start");
            instance = (WeaponDefinition)typeof(PilotEquipment).GetField("instance", Private).GetValue(equipment);
            Near(pilot.moveSpeed, 18.27f, "equipped hull combines with two speed ranks");
            Near(pilot.verticalSpeed, 8.584f, "vertical equipment and ranks compose");

            // A timed boost must survive inventory refreshes and compose with the equipped ship and skill ranks.
            Set(skills, "boostUntil", Time.time + 10);
            Call(skills, "ApplyBoostSpeed");
            Near(pilot.moveSpeed, 31.059f, "boost includes equipment");
            PilotInventory.Grant(0, 1, -1, 0);
            Near(pilot.moveSpeed, 31.059f, "pickup refresh preserves active boost");
            Set(skills, "boostUntil", 0f);
            skills.RefreshEquipmentBonuses();
            Near(pilot.moveSpeed, 18.27f, "boost expiry retains hull and ranks");
            skills.ApplyTreeBonuses();
            Near(pilot.moveSpeed, 18.27f, "tree refresh does not erase equipment");

            // Downgrading equipment must retain skill bonuses and keep hull values within the new maximum.
            inventory.ship = 0;
            inventory.upgrade = 0;
            inventory.modules[0] = 0;
            inventory.modules[1] = 0;
            Call(equipment, "Apply");
            Near(pilot.moveSpeed, 14, "hull downgrade preserves skill ranks");
            Require(health.CurrentHealth <= health.maxHealth + 0.0001f, "hull downgrade clamps current hull");

            // Repeated ship swaps must preserve damage instead of allowing free healing.
            health.SetNetworkHealth(0.5f);

            for (int hullSwapIteration = 0; hullSwapIteration < 3; hullSwapIteration++)
            {
                inventory.ship = 2;
                Call(equipment, "Apply");
                inventory.ship = 0;
                Call(equipment, "Apply");
            }

            Near(health.CurrentHealth, 0.5f, "repeated owned hull swaps cannot heal damage for free");

            // Multiplayer must use its baseline equipment rules rather than the solo inventory bonuses.
            NetSession.Active = true;
            skills.RefreshEquipmentBonuses();
            Near(pilot.moveSpeed, 14, "multiplayer keeps baseline equipment multiplier");
            Debug.Log("EQUIPMENT_CHECKS_OK: hull, ranks, boost, pickup, expiry, downgrade, network baseline");
        }
        finally
        {
            NetSession.Active = false;
            if (equipment != null)
            {
                Set(equipment, "instance", null);
            }

            UnityEngine.Object.DestroyImmediate(fixture);
            if (instance != null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            if (original != null)
            {
                UnityEngine.Object.DestroyImmediate(original);
            }

            GameSession.SuppressSave = suppress;
            NetSession.Active = active;
            stateField.SetValue(null, previousState);
            suppressedField.SetValue(null, previousSuppressed);
        }
    }

    static void Set(object target, string field, object value)
    {
        target.GetType().GetField(field, Private).SetValue(target, value);
    }

    static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, Private).Invoke(target, null);
    }

    static void Near(float actual, float expected, string message)
    {
        Require(Mathf.Abs(actual - expected) < 0.001f, message + ": " + actual + " expected " + expected);
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception("Equipment check: " + message);
        }
    }
}
