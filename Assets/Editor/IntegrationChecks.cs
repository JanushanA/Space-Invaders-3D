using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Checks legacy adapters, persistence and serialized scene wiring for the combined game.
public static class IntegrationChecks
{
    public static void RunAdapters()
    {
        var results = new List<string>();
        Check("legacy player damage shares Health and kills once", () => CheckHealth<PlayerHealth>(), results);
        Check("legacy enemy damage shares Health and kills once", () => CheckHealth<Enemies>(), results);
        Check("flight diagonal speed is bounded", () =>
        {
            var method = typeof(PlayerMovement).GetMethod("ComputeFlightVelocity");
            Require(method != null, "Flight adapter missing");
            var velocity = (Vector3)method.Invoke(null, new object[] { Vector2.one, Vector3.forward, Vector3.right, 12f, 0f, 9f });
            Require(Mathf.Abs(velocity.magnitude - 12f) < 0.001f, "Diagonal movement exceeds configured speed");
        }, results);
        Check("camera pitch clamps at both limits", () =>
        {
            var method = typeof(PlayerCam).GetMethod("ClampPitch");
            Require(method != null, "Pitch adapter missing");
            Require((float)method.Invoke(null, new object[] { -999f, 40f, 55f }) == -40f, "Up clamp");
            Require((float)method.Invoke(null, new object[] { 999f, 40f, 55f }) == 55f, "Down clamp");
        }, results);
        Check("client health accepts host snapshots only", () =>
        {
            var sceneObject = new GameObject("Client health check");
            try
            {
                var health = sceneObject.AddComponent<Health>();
                health.ResetHealth();
                health.deactivateOnDeath = false;
                NetSession.Active = true;
                NetSession.IsHost = false;
                health.ApplyDamage(new DamageInfo(99, DamageSource.Script, null, Vector3.zero, Vector3.up));
                Require(health.CurrentHealth == 100, "Client applied independent damage");
                health.SetNetworkHealth(30);
                health.Heal(50);
                Require(health.CurrentHealth == 30, "Client ignored host snapshot or healed independently");
            }
            finally
            {
                NetSession.Reset();
                UnityEngine.Object.DestroyImmediate(sceneObject);
            }
        }, results);
        Check("progression settings and loadout round-trip without losing user save", CheckPersistence, results);
        Check("suppressed diagnostics cannot overwrite best score", () =>
        {
            int saved = PlayerPrefs.GetInt("pilot_best_score", 0);
            int score = GameSession.RunScore;
            string killer = GameSession.LastKiller;
            bool suppression = GameSession.SuppressSave;
            try
            {
                GameSession.SuppressSave = true;
                GameSession.RunScore = saved + 100;
                GameSession.NoteDeath("regression test");
                Require(PlayerPrefs.GetInt("pilot_best_score", 0) == saved, "Suppressed run wrote best score");
            }
            finally
            {
                GameSession.RunScore = score;
                GameSession.LastKiller = killer;
                GameSession.SuppressSave = suppression;
            }
        }, results);
        Finish("adapter-checks", results);
    }

    private static void CheckPersistence()
    {
        string[] keys = { "sector_stars_1", "sector_unlocked_2", "pilot_saved", "pilot_level", "pilot_xp", "pilot_points", "invert_look", "pilot_slot_0", "pilot_bind_0" };
        var savedPreferences = new Dictionary<string, int>();

        foreach (string key in keys)
        {
            if (PlayerPrefs.HasKey(key))
            {
                savedPreferences[key] = PlayerPrefs.GetInt(key);
            }
        }

        bool hadSkills = PlayerPrefs.HasKey("pilot_skills");
        string skills = PlayerPrefs.GetString("pilot_skills");
        bool suppressed = GameSession.SuppressSave;
        try
        {
            GameSession.SuppressSave = false;
            GameSession.CompleteLevel(1, 3);
            Require(GameSession.GetStars(1) == 3 && GameSession.IsUnlocked(2), "Sector save/unlock failed");
            GameSession.SavePilotStats(4, 15, 3);
            GameSession.LoadPilotStats(out int level, out int experiencePoints, out int points);
            Require(level == 4 && experiencePoints == 15 && points == 3, "Pilot save failed");
            GameSession.SetInvertLook(true);
            Require(GameSession.InvertLook, "Settings save failed");
            GameSession.SaveSkillList("1,2");
            Require(GameSession.LoadSkillList() == "1,2", "Skills save failed");
            GameSession.SaveHotbar(0, 2, (int)KeyCode.R);
            GameSession.LoadHotbarSlot(0, out int skill, out int bind, out bool found);
            Require(found && skill == 2 && bind == (int)KeyCode.R, "Loadout save failed");
        }
        finally
        {
            foreach (string key in keys)
            {
                if (savedPreferences.TryGetValue(key, out int value))
                {
                    PlayerPrefs.SetInt(key, value);
                }
                else
                {
                    PlayerPrefs.DeleteKey(key);
                }
            }

            if (hadSkills)
            {
                PlayerPrefs.SetString("pilot_skills", skills);
            }
            else
            {
                PlayerPrefs.DeleteKey("pilot_skills");
            }

            PlayerPrefs.Save();
            GameSession.SuppressSave = suppressed;
        }
    }

    private static void CheckHealth<T>() where T : Component
    {
        NetSession.Reset();
        var sceneObject = new GameObject("Legacy health regression");
        try
        {
            var health = sceneObject.AddComponent<Health>();
            health.maxHealth = 100f;
            health.deactivateOnDeath = false;
            health.ResetHealth();
            var adapter = sceneObject.AddComponent<T>();
            var damage = typeof(T).GetMethod("TakeDamage");
            int deaths = 0;
            health.Died += (health2, info) => deaths++;
            damage.Invoke(adapter, new object[] { 10f });
            Require(health.CurrentHealth == 90f, "Legacy API did not update authoritative health");
            damage.Invoke(adapter, new object[] { 100f });
            damage.Invoke(adapter, new object[] { 100f });
            Require(deaths == 1 && health.CurrentHealth == 0, "Duplicate death or inconsistent health");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sceneObject);
            NetSession.Reset();
        }
    }

    public static void RunSceneChecks()
    {
        var results = new List<string>();
        Check("build entry is group menu followed by combat", () =>
        {
            var scenes = EditorBuildSettings.scenes;
            Require(scenes.Length == 2 && scenes[0].enabled && scenes[1].enabled, "Two enabled scenes required");
            Require(scenes[0].path == "Assets/Space-Invaders-3D.unity", "Wrong entry scene");
            Require(scenes[1].path == "Assets/Scenes/Combat_Test.unity", "Wrong combat scene");
        }, results);
        Check("menu controller and UI navigation are serialized", () =>
        {
            EditorSceneManager.OpenScene("Assets/Space-Invaders-3D.unity");
            var menu = UnityEngine.Object.FindObjectOfType<MainMenuEvents>();
            Require(menu != null, "Missing MainMenuEvents");
            var uiDocument = menu.GetComponent<UIDocument>();
            Require(uiDocument != null && uiDocument.visualTreeAsset != null && uiDocument.panelSettings != null, "Menu assets missing");
            var tree = uiDocument.visualTreeAsset.CloneTree();

            foreach (string name in new[] { "StartGameButton", "MultiplayerButton", "SettingsButton", "SectorList", "LobbyPanel", "JoinCode" })
            {
                Require(tree.Q(name) != null, "Missing UI element: " + name);
            }

            ValidateSceneObjects();
        }, results);
        Check("combat has one pilot and group visual", () =>
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Combat_Test.unity");
            var players = UnityEngine.Object.FindObjectsOfType<Game.Player.PlayerController>();
            Require(players.Length == 1, "Expected one local player");
            Require(players[0].GetComponent<PlayerHealth>() != null, "Missing group health adapter");
            Require(UnityEngine.Object.FindObjectsOfType<Camera>().Length == 1, "Competing cameras");
            Require(UnityEngine.Object.FindObjectsOfType<AudioListener>().Length == 1, "Competing audio listeners");
            Require(players[0].GetComponent<Rigidbody>() == null, "Competing Rigidbody movement");
            var hull = players[0].transform.Find("Hull");
            Require(hull != null && AssetDatabase.GetAssetPath(hull.GetComponent<MeshFilter>().sharedMesh).Contains("ArvisMag_Assets"), "Group mesh not used");
            GroupVisualDiagnostics.InspectHull(hull);
            var spawner = UnityEngine.Object.FindObjectOfType<Game.Enemies.EnemyWaveSpawner>();
            Require(spawner != null && spawner.enemyPrefab != null && spawner.enemyPrefab.GetComponent<Enemies>() != null, "Enemy wiring missing");

            foreach (var collider in spawner.enemyPrefab.GetComponentsInChildren<Collider>())
            {
                Require(collider.gameObject.layer == LayerMask.NameToLayer(GameLayers.Enemy), "Enemy collider layer mismatches player hit mask");
            }

            foreach (string layer in GameLayers.RequiredLayers)
            {
                Require(LayerMask.NameToLayer(layer) >= 0, "Missing layer " + layer);
            }

            ValidateSceneObjects();
        }, results);
        Finish("scene-checks", results);
    }

    private static void ValidateSceneObjects()
    {
        Require(RenderSettings.reflectionIntensity == 0f, "Unsafe generated environment reflections re-enabled");

        foreach (var sceneObject in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var childTransform in sceneObject.GetComponentsInChildren<Transform>(true))
            {
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(childTransform.gameObject) == 0, "Missing script: " + childTransform.name);
            }
        }
    }

    private static void Check(string name, Action action, List<string> results)
    {
        try
        {
            action();
            results.Add("PASS " + name);
        }
        catch (Exception exception)
        {
            results.Add("FAIL " + name + ": " + exception.GetBaseException().Message);
        }
    }

    private static void Finish(string name, List<string> results)
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllLines("Logs/" + name + ".txt", results);

        foreach (var result in results)
        {
            Debug.Log(result);
        }

        if (results.Exists(checkResult => checkResult.StartsWith("FAIL")))
        {
            throw new Exception(name + " failed; see Logs/" + name + ".txt");
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
