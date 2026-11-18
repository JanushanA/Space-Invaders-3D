using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using Game.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Checks legacy key migration without losing equipped skills or allowing the reserved view key.
public static class FlightBindingChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Group Game/Check reserved flight view binding")]
    public static void Run()
    {
        var host = new GameObject("FlightBindingFixture");
        bool originalSuppressSave = GameSession.SuppressSave;
        var keys = new List<string> { "pilot_saved" };

        for (int abilitySlot = 0; abilitySlot < PlayerSkills.SlotCount; abilitySlot++)
        {
            keys.Add("pilot_slot_" + abilitySlot);
            keys.Add("pilot_bind_" + abilitySlot);
        }

        var saved = new Dictionary<string, int>();

        foreach (string key in keys)
        {
            if (PlayerPrefs.HasKey(key))
            {
                saved[key] = PlayerPrefs.GetInt(key);
            }
        }

        Keyboard previousKeyboard = Keyboard.current;
        Keyboard keyboard = null;
        try
        {
            GameSession.SuppressSave = true;
            var tree = host.AddComponent<SkillTree>();
            if (!tree.IsActiveSkill(SkillId.Critical))
            {
                Call(tree, "BuildNodes");
            }

            var unlocked = (HashSet<SkillId>)typeof(SkillTree).GetField("unlocked", Private).GetValue(tree);
            unlocked.Add(SkillId.Critical);
            unlocked.Add(SkillId.Dash);
            unlocked.Add(SkillId.Shield);
            var skills = host.AddComponent<PlayerSkills>();
            Call(skills, "Awake");
            Seed(KeyCode.V, KeyCode.R, KeyCode.T);
            Call(skills, "LoadSavedHotbar");
            Require(skills.GetBind(0) == KeyCode.X && skills.GetBind(1) == KeyCode.R && skills.GetBind(2) == KeyCode.T,
                "V migration collided with a later saved binding or changed an existing binding");
            Require(skills.GetSlot(0) == SkillId.Critical && skills.GetSlot(1) == SkillId.Dash && skills.GetSlot(2) == SkillId.Shield,
                "Binding migration removed or reordered equipped skills");
            Call(skills, "SaveHotbarNow");
            Require(PlayerPrefs.GetInt("pilot_bind_0") == (int)KeyCode.V, "SuppressSave allowed migrated preferences to be written");
            GameSession.SuppressSave = false;
            Call(skills, "SaveHotbarNow");
            Require(PlayerPrefs.GetInt("pilot_bind_0") == (int)KeyCode.X && PlayerPrefs.GetInt("pilot_slot_0") == (int)SkillId.Critical,
                "Normal save did not persist the migrated binding with its original skill");
            GameSession.SuppressSave = true;
            Seed(KeyCode.V, KeyCode.V, KeyCode.X);
            Call(skills, "LoadSavedHotbar");
            Require(skills.GetBind(0) == KeyCode.R && skills.GetBind(1) == KeyCode.T && skills.GetBind(2) == KeyCode.X,
                "Multiple old V bindings were not migrated to distinct free keys");
            Seed(KeyCode.C, KeyCode.B, KeyCode.F);
            Call(skills, "LoadSavedHotbar");
            Require(skills.GetBind(0) == KeyCode.C && skills.GetBind(1) == KeyCode.B && skills.GetBind(2) == KeyCode.F,
                "Migration changed unrelated custom bindings");

            for (int bindingCycle = 0; bindingCycle < 18; bindingCycle++)
            {
                skills.CycleBind(0);
                Require(skills.GetBind(0) != KeyCode.V && skills.GetBind(0) != skills.GetBind(1) && skills.GetBind(0) != skills.GetBind(2),
                    "Cycling skill keys offered the view key or an occupied key");
            }

            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V));
            InputSystem.Update();

            // Editor updates expose held state; player-frame edge semantics are
            // checked by the play-mode flight smoke test.
            Require(keyboard.vKey.isPressed, "Input fixture did not hold V");
            bool skillPressed = (bool)typeof(PlayerSkills).GetMethod("WasBindPressed", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { KeyCode.V });
            Require(!skillPressed, "Pressing the view key can still trigger a stale V skill binding");
            Debug.Log("FLIGHT BINDING CHECKS: PASS collision-free save migration, skill preservation, suppression, cycling and reserved V input");
        }
        finally
        {
            GameSession.SuppressSave = true;
            if (keyboard != null)
            {
                InputSystem.RemoveDevice(keyboard);
            }

            if (previousKeyboard != null)
            {
                previousKeyboard.MakeCurrent();
            }

            UnityEngine.Object.DestroyImmediate(host);

            foreach (string key in keys)
            {
                if (saved.TryGetValue(key, out int value))
                {
                    PlayerPrefs.SetInt(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            PlayerPrefs.Save();
            GameSession.SuppressSave = originalSuppressSave;
        }
    }

    private static void Seed(KeyCode first, KeyCode second, KeyCode third)
    {
        PlayerPrefs.SetInt("pilot_saved", 1);
        var binds = new[] { first, second, third };
        var skills = new[] { SkillId.Critical, SkillId.Dash, SkillId.Shield };

        for (int abilitySlot = 0; abilitySlot < binds.Length; abilitySlot++)
        {
            PlayerPrefs.SetInt("pilot_slot_" + abilitySlot, (int)skills[abilitySlot]);
            PlayerPrefs.SetInt("pilot_bind_" + abilitySlot, (int)binds[abilitySlot]);
        }
    }

    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
