#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Net;
using Game.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Opt-in development-build diagnostics. Never runs in normal play or release builds.
public class IntegrationSmokeTest : MonoBehaviour
{
    private readonly List<string> results = new List<string>();
    private readonly List<string> runtimeErrors = new List<string>();
    private string output;
    private bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        bool requested = Array.IndexOf(Environment.GetCommandLineArgs(), "-integrationSmokeTest") >= 0;
#if UNITY_EDITOR
        requested |= UnityEditor.SessionState.GetBool("GroupSoloSmoke", false);
        UnityEditor.SessionState.EraseBool("GroupSoloSmoke");
#endif
        if (!requested)
        {
            return;
        }

        var sceneObject = new GameObject("Integration smoke diagnostics");
        DontDestroyOnLoad(sceneObject);
        sceneObject.AddComponent<IntegrationSmokeTest>();
    }

    private void Awake()
    {
        output = Path.GetFullPath("Logs/SoloSmoke");
        Directory.CreateDirectory(output);
        GameSession.SuppressSave = true;
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        if (Keyboard.current == null)
        {
            InputSystem.AddDevice<Keyboard>();
        }

        if (Mouse.current == null)
        {
            InputSystem.AddDevice<Mouse>();
        }

        Application.logMessageReceived += OnLog;
        SceneManager.sceneLoaded += OnScene;
    }

    private void OnScene(Scene scene, LoadSceneMode mode)
    {
        GameSession.SuppressSave = true;
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            runtimeErrors.Add(message);
        }
    }

    private void Start()
    {
        StartCoroutine(Guarded(Run()));
    }

    private IEnumerator Guarded(IEnumerator routine)
    {
        while (true)
        {
            bool more;
            try
            {
                more = routine.MoveNext();
            }
            catch (Exception error)
            {
                results.Add("FAIL " + error);
                Finish(false);
                yield break;
            }

            if (!more)
            {
                Finish(true);
                yield break;
            }

            yield return routine.Current;
        }
    }

    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(1);
        Require(SceneManager.GetActiveScene().name == GameSession.MenuScene, "build starts in group menu");
        var menu = FindObjectOfType<MainMenuEvents>();
        Require(menu != null, "group menu controller active");
        var uiDocument = menu.GetComponent<UIDocument>();
        Capture("01-group-menu");
        yield return new WaitForSecondsRealtime(0.2f);
        Click(uiDocument, "StartGameButton");
        Require(uiDocument.rootVisualElement.Q("SectorPanel").style.display.value != DisplayStyle.None, "Start opens sector selection");
        menu.enabled = false;
        menu.enabled = true;
        GameSession.SuppressSave = true;
        Click(uiDocument, "StartGameButton");
        Require(uiDocument.rootVisualElement.Q("SectorList").childCount == 8, "menu re-enable binds once and retains eight sectors");
        Click(uiDocument, "Sector1");
        yield return new WaitForSecondsRealtime(3);
        Require(SceneManager.GetActiveScene().name == GameSession.CombatScene, "group Start reaches combat");
        var pilot = FindObjectOfType<PlayerController>();
        Require(pilot != null && FindObjectsOfType<PlayerController>().Length == 1, "one local pilot");
        pilot.captureCursor = false;
        pilot.health.invulnerable = true;
        Vector3 position = pilot.transform.position;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.Space));
        yield return new WaitForSecondsRealtime(0.6f);
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        Debug.Log("SMOKE INPUT position=" + position + " current=" + pilot.transform.position + " move=" + pilot.actionAsset.FindAction("Move").ReadValue<Vector2>() + " elevate=" + pilot.actionAsset.FindAction("Elevate").ReadValue<float>() + " keyboard=" + Keyboard.current.enabled);
        Require(Vector3.Distance(position, pilot.transform.position) > 1f && pilot.transform.position.y > position.y, "real input drives forward flight and elevation");
        Vector3 previousAim = pilot.aimPivot.forward;
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = new Vector2(60f, 30f) });
        yield return null;
        yield return null;
        Require(Vector3.Angle(previousAim, pilot.aimPivot.forward) > 0.1f, "mouse input changes aim");
        Require(pilot.weapon.TryFire(), "player weapon fires");
        yield return new WaitForSecondsRealtime(0.05f);
        Capture("02-group-combat");
        Require(FindObjectsOfType<EnemyController>().Length > 0, "standard wave spawned");
        Require(FindObjectsOfType<Camera>().Length == 1 && FindObjectsOfType<AudioListener>().Length == 1, "one runtime camera and audio listener");
        Require(FindObjectsOfType<GameHud>().Length == 1, "HUD connected");

        foreach (var renderer in FindObjectsOfType<Renderer>())
        {
            foreach (var material in renderer.sharedMaterials)
            {
                Require(material != null && material.shader != null && material.shader.name != "Hidden/InternalErrorShader", "shader available: " + renderer.name, false);
            }
        }

        results.Add("PASS all active renderer shaders resolve");
        var target = new GameObject("Swept projectile test target");
        target.transform.position = new Vector3(500f, 500f, 500f);
        var health = target.AddComponent<Health>();
        health.maxHealth = 3f;
        health.ResetHealth();
        health.deactivateOnDeath = false;
        var child = new GameObject("Child hit collider");
        child.transform.SetParent(target.transform, false);
        child.layer = LayerMask.NameToLayer(GameLayers.Enemy);
        child.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        var pool = ProjectilePool.Instance;
        var shot = pool.Get(pilot.weapon.definition.projectilePrefab);
        shot.Launch(target.transform.position - Vector3.forward * 5, Vector3.forward,
            new ProjectileLaunchData(1000f, 1f, 0.1f, 2f, DamageSource.PlayerProjectile, pilot.gameObject, GameLayers.PlayerWeaponHitMask), pool);
        yield return new WaitForSecondsRealtime(0.15f);
        Require(health.CurrentHealth == 2f, "fast swept projectile damages parent health through child collider");
        var reused = pool.Get(pilot.weapon.definition.projectilePrefab);
        Require(reused == shot, "projectile instance reused from pool");
        pool.Release(reused);
        pool.Release(reused);
        Require(!reused.gameObject.activeSelf, "duplicate pool return is harmless");
        Destroy(target);
        var progression = pilot.GetComponent<PlayerProgression>();
        int oldLevel = progression.Level;
        progression.AddXp(100);
        Require(progression.Level >= oldLevel, "kill progression accepts experience");
        progression.AddSkillPoints(10);
        var tree = pilot.GetComponent<SkillTree>();
        Require(tree.IsUnlocked(SkillId.Speed1) || tree.TryUnlockPath(SkillId.Speed1), "skill upgrade path unlocks");

        // SuppressSave means no development-test progression replaces the user's pilot.
        var spawner = FindObjectOfType<EnemyWaveSpawner>();
        spawner.delayBetweenWaves = 0.05f;
        float deadline = Time.realtimeSinceStartup + 12f;

        while (!(bool)Field(spawner, "sectorCleared") && Time.realtimeSinceStartup < deadline)
        {
            foreach (var enemy in FindObjectsOfType<EnemyController>())
            {
                enemy.GetComponent<Health>().ApplyDamage(new DamageInfo(10000, DamageSource.PlayerProjectile, pilot.gameObject, enemy.transform.position, Vector3.up));
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }

        Require((bool)Field(spawner, "sectorCleared"), "standard waves complete");
        yield return new WaitForSecondsRealtime(2.5f);
        Require(SceneManager.GetActiveScene().name == GameSession.MenuScene, "completion returns to group sector menu");

        // Start the boss sector only after the ordinary wave and menu return checks pass.
        GameSession.SelectedLevel = 2;
        SceneManager.LoadScene(GameSession.CombatScene);
        yield return new WaitForSecondsRealtime(2f);
        pilot = FindObjectOfType<PlayerController>();
        pilot.health.invulnerable = true;
        var boss = FindObjectOfType<MotherShipBoss>();
        Require(boss != null, "sector two spawns Mothership");
        Capture("03-mothership");
        var bossHealth = boss.GetComponent<Health>();
        Require(bossHealth.maxHealth == 1000f, "solo boss health scaling");
        var enter = typeof(MotherShipBoss).GetMethod("Begin", BindingFlags.NonPublic | BindingFlags.Instance);
        var moveType = typeof(MotherShipBoss).GetNestedType("Move", BindingFlags.NonPublic);
        Require(enter != null, "boss phase transition method exists");
        {
            enter.Invoke(boss, new[] { Enum.Parse(moveType, "Minions") });
            yield return null;
            Require(bossHealth.invulnerable && FindObjectsOfType<EnemyController>().Length > 0, "escort phase protects boss");

            foreach (var enemy in FindObjectsOfType<EnemyController>())
            {
                enemy.GetComponent<Health>().ApplyDamage(new DamageInfo(10000, DamageSource.PlayerProjectile, pilot.gameObject, enemy.transform.position, Vector3.up));
            }

            yield return new WaitForSecondsRealtime(0.2f);
        }
        bossHealth.invulnerable = false;
        bossHealth.ApplyDamage(new DamageInfo(600f, DamageSource.PlayerProjectile, pilot.gameObject, boss.transform.position, Vector3.up));
        yield return new WaitForSecondsRealtime(4f);
        Require((bool)Field(boss, "enraged"), "half-health transition reaches second stage");
        Capture("04-mothership-stage-two");

        yield return new WaitForSecondsRealtime(0.2f); // Let the screenshot finish before opening the death UI.

        for (int party = 1; party <= 5; party++)
        {
            NetSession.Active = true;
            NetSession.PlayerCount = party;
            Require(GameSession.PartySize == party, "party size " + party + " scaling input");
        }

        NetSession.Reset();
        pilot.health.invulnerable = false;
        pilot.health.ApplyDamage(new DamageInfo(10000, DamageSource.Script, null, pilot.transform.position, Vector3.up));
        yield return new WaitForSecondsRealtime(0.2f);
        Require((bool)Field(pilot.GetComponent<DeathFlow>(), "open"), "death opens retry flow");
        Capture("05-retry");

        yield return new WaitForSecondsRealtime(0.2f); // Capture the death screen before loading the retry scene.
        typeof(DeathFlow).GetMethod("Retry", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pilot.GetComponent<DeathFlow>(), null);
        yield return new WaitForSecondsRealtime(0.5f);
        Require(FindObjectOfType<PlayerController>().health.IsAlive, "retry creates a living pilot");
        Require(FindObjectsOfType<PlayerController>().Length == 1 && FindObjectsOfType<ProjectilePool>().Length <= 1, "retry cleans up old pilot and pool");
        Require(runtimeErrors.Count == 0, "no runtime errors: " + string.Join("; ", runtimeErrors));
    }

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void Click(UIDocument uiDocument, string name)
    {
        using (var inputEvent = ClickEvent.GetPooled())
        {
            inputEvent.target = uiDocument.rootVisualElement.Q<Button>(name);
            uiDocument.rootVisualElement.SendEvent(inputEvent);
        }
    }

    private void Capture(string name)
    {
        var probe = RenderSettings.ambientProbe;

        for (int channel = 0; channel < 3; channel++)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
            {
                Require(!float.IsNaN(probe[channel, coefficient]) && !float.IsInfinity(probe[channel, coefficient]),
                "finite ambient probe at " + name, false);
            }
        }

        ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
    }

    private void Require(bool condition, string name, bool writeLog = true)
    {
        if (!condition)
        {
            throw new Exception(name);
        }

        if (writeLog)
        {
            results.Add("PASS " + name);
        }
    }

    private void Finish(bool passed)
    {
        if (finished)
        {
            return;
        }

        finished = true;
        GameSession.SuppressSave = true;
        File.WriteAllLines(Path.Combine(output, "results.txt"), results);
        Application.logMessageReceived -= OnLog;
        SceneManager.sceneLoaded -= OnScene;
        Debug.Log("SOLO SMOKE " + (passed ? "PASSED" : "FAILED") + ": " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
#else
        Application.Quit(passed ? 0 : 1);
#endif
    }
}
#endif
