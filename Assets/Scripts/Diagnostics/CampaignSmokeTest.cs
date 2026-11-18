#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Campaign;
using Game.Core;
using Game.Player;
using Game.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
namespace Game.Diagnostics
{
    // Drives the built campaign through its real menus, mission interactions and rewards, recording captures and failures.
    public sealed class CampaignSmokeTest : MonoBehaviour
    {
        readonly List<string> results = new List<string>();
        readonly List<string> errors = new List<string>();
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-campaignSmokeTest") < 0)
            {
                return;
            }

            var sceneObject = new GameObject("Campaign validation");
            DontDestroyOnLoad(sceneObject);
            sceneObject.AddComponent<CampaignSmokeTest>();
        }

        void Awake()
        {
            output = Path.GetFullPath("Logs/CampaignSmoke");
            Directory.CreateDirectory(output);
            GameSession.SuppressSave = true;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (Keyboard.current == null)
            {
                InputSystem.AddDevice<Keyboard>();
            }

            Application.logMessageReceived += Log;
            SceneManager.sceneLoaded += Scene;
        }

        void Scene(Scene scene, LoadSceneMode mode)
        {
            GameSession.SuppressSave = true;
        }

        void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors.Add(message);
            }
        }

        void Start()
        {
            StartCoroutine(Guard(Run()));
        }

        IEnumerator Guard(IEnumerator routine)
        {
            // Drive nested coroutines explicitly so any inner failure is recorded and all active iterators are disposed.
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);

            while (stack.Count > 0)
            {
                object current = null;
                bool failed = false;
                try
                {
                    if (!stack.Peek().MoveNext())
                    {
                        (stack.Pop() as IDisposable)?.Dispose();
                        continue;
                    }

                    current = stack.Peek().Current;
                }
                catch (Exception errorMessage)
                {
                    errors.Add(errorMessage.ToString());
                    failed = true;
                }

                if (failed)
                {
                    while (stack.Count > 0)
                    {
                        (stack.Pop() as IDisposable)?.Dispose();
                    }

                    Finish();
                    yield break;
                }

                if (current is IEnumerator nested)
                {
                    stack.Push(nested);
                }
                else
                {
                    yield return current;
                }
            }

            Finish();
        }

        IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(4);
            Capture("01-main-menu");
            yield return null;

            // Choose a repeatable route containing the two mission types exercised by this run.
            int seed = 1;

            while (!new SectorNetwork(seed).Adjacent(1, 2))
            {
                seed++;
            }

            CampaignSession.BeginRun(seed);
            CampaignSession.OpenHideout();
            yield return new WaitForSecondsRealtime(3);
            var pilot = FindObjectOfType<PlayerController>();
            Require(pilot != null, "Hideout pilot exists");
            Game.Destruction.FractureSmokeProbe.Run();
            var menu = pilot.GetComponent<HangarMenu>();
            Require(menu != null && menu.IsOpen, "Hideout is interactive and open");
            Require(!FindObjectOfType<Game.Enemies.EnemyWaveSpawner>().enabled, "Hideout has no hostile waves");
            Require(pilot.transform.Find("Hull").GetComponentInChildren<MeshFilter>().sharedMesh == Resources.Load<GameObject>("Ships/Player0").GetComponentInChildren<MeshFilter>().sharedMesh, "New player model imported");
            Capture("02-hideout");
            yield return new WaitForSecondsRealtime(0.5f);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Tab));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            Require(!menu.IsOpen && PlayerSkills.MenuOpen, "Hideout Tab opens skill tree");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Tab));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            Require(menu.IsOpen, "Closing skills returns to hideout");

            // Fund the test inventory, then craft and equip through the actual hangar controls.
            var inventory = PilotInventory.State;

            for (int materialIndex = 0; materialIndex < 6; materialIndex++)
            {
                inventory.materials[materialIndex] = 200;
            }

            inventory.credits = 5000;
            menu.Open("FABRICATION");
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("03-fabrication");
            yield return null;
            yield return Assemble(menu, "weapon-3", "03b-ordered-ion-rail");
            menu.Open("CARGO");
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("04-inventory");
            yield return null;
            Click("Equip weapon 3");
            yield return null;
            Require(inventory.equipped == 3 && Mathf.Abs(pilot.weapon.definition.damagePerProjectile - 3.8f) < 0.001f, "UI equipped crafted weapon affects live combat");
            menu.Open("SHIPYARD");
            yield return Assemble(menu, "ship-1", "05-shipyard");

            foreach (string identifier in new[] { "module-0", "module-1", "module-2", "module-3" })
            {
                yield return Assemble(menu, identifier, null);
            }

            Require(inventory.ship == 1 && inventory.ModuleTier(0) == 1 && inventory.ModuleTier(1) == 1 && inventory.ModuleTier(2) == 1 && inventory.ModuleTier(3) == 1, "UI assembled ship and all four installed modules");
            Require(Mathf.Abs(pilot.health.incomingDamageMul - 0.96f) < 0.001f && Mathf.Abs(PilotEquipment.CruiseMultiplier - 1.135f) < 0.001f, "Installed shield and drive modules affect actual pilot stats");

            // Check purchase and sale accounting through the visible trading buttons.
            menu.Open("TRADE");
            yield return null;
            int credits = inventory.credits;
            int materials = inventory.materials[0];
            int stock = inventory.tradeStock[0];
            Click("Buy offer 0");
            yield return null;
            Require(inventory.credits == credits - 25 && inventory.materials[0] == materials + 5 && inventory.tradeStock[0] == stock - 1, "Live terminal purchase changes credits cargo and stock");
            Click("Sell offer 0");
            yield return null;
            Require(inventory.credits == credits - 13 && inventory.materials[0] == materials && inventory.tradeStock[0] == stock, "Live terminal sale returns finite stock at half price");
            Capture("05b-trading-terminal");
            yield return null;
            menu.Close();
            pilot.GetComponent<SkillMenu>().Open();
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("06-skill-tree");
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Tab));
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;

            // Capture the narrower skill layout before moving into the first campaign mission.
            Screen.SetResolution(1280, 960, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1);
            pilot.GetComponent<SkillMenu>().Open();
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("07-skill-tree-4x3");
            yield return null;
            Require(CampaignSession.Launch(1), "Earth mission launches through generated network");
            yield return new WaitForSecondsRealtime(2);
            pilot = FindObjectOfType<PlayerController>();
            pilot.health.invulnerable = true;
            var mission = CampaignMission.Instance;
            Require(mission != null && mission.Plan != null, "Campaign generated");
            Require(SectorGenerator.Validate(mission.Plan, out _), "Live sector passes independent validator");
            yield return WorldSystemsRuntimeChecks.Run(mission, pilot);
            yield return RadarProbe(pilot);
            pilot.transform.position = mission.NavigationCore.position + Vector3.back * 5;
            Capture("08a-recovery-objective");
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            Require(mission.Stage == 1, "E interaction retrieves navigation core");
            Capture("08-campaign-flight");
            yield return null;

            // Keep the pilot near the moving convoy and bound the wait for the real escort route to finish.
            Time.timeScale = 4;
            float deadline = Time.realtimeSinceStartup + 65;

            while (mission != null && mission.Stage == 1 && Time.realtimeSinceStartup < deadline)
            {
                var convoy = GameObject.Find("Civilian transport / protect");
                RequireTransient(convoy != null, "Escort exists");
                pilot.transform.position = convoy.transform.position + Vector3.back * 10;
                ClearEnemies(pilot);
                yield return null;
            }

            Require(mission.Stage == 2, "Escort follows validated asteroid-clear route");
            pilot.transform.position = mission.ExtractionBeacon.position + Vector3.up * 6;
            yield return null;
            Capture("08d-extraction-beacon");
            yield return null;
            deadline = Time.realtimeSinceStartup + 6;

            while (mission != null && !mission.Complete && Time.realtimeSinceStartup < deadline)
            {
                ClearEnemies(pilot);
                yield return null;
            }

            Require(mission.Complete, "Defended extraction completes campaign task");
            Time.timeScale = 1;
            yield return new WaitForSecondsRealtime(3);
            Require(CampaignSession.InHideout && PilotInventory.State.completedCampaigns >= 1, "Campaign completion unlocks next route and returns to hideout");

            // Defeat a boss through its actual Health death event and verify guaranteed legendary persistence.
            Require(CampaignSession.Launch(2), "Second story campaign launches");
            yield return new WaitForSecondsRealtime(3);
            pilot = FindObjectOfType<PlayerController>();
            pilot.health.invulnerable = true;
            mission = CampaignMission.Instance;
            yield return WorldSystemsRuntimeChecks.Run(mission, pilot);
            pilot.transform.position = mission.NavigationCore.position + Vector3.up * 5;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            Require(mission.Stage == 1, "Second campaign key recovered");

            // Activate the three relays through their normal interaction key in the required order.
            foreach (int index in new[] { 0, 1, 2 })
            {
                pilot.transform.position = mission.Relays[index].position + Vector3.up * 4;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
                yield return null;
                yield return null;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
                yield return null;
                if (index == 1)
                {
                    Capture("08b-relay-objective");
                    yield return null;
                }
            }

            Require(mission.Stage == 2, "Amber/cyan/violet relay puzzle accepts correct sequence");
            pilot.transform.position = mission.ExtractionBeacon.position + Vector3.up * 6;
            deadline = Time.realtimeSinceStartup + 14;

            while (!mission.Complete && Time.realtimeSinceStartup < deadline)
            {
                ClearEnemies(pilot);
                yield return null;
            }

            Require(mission.Complete, "Relay campaign extraction completes");
            var boss = FindObjectOfType<Game.Enemies.MotherShipBoss>();
            Require(boss != null, "Mothership spawned");
            boss.GetComponent<Game.Combat.Health>().ApplyDamage(new DamageInfo(100000, DamageSource.PlayerProjectile, pilot.gameObject, boss.transform.position, Vector3.up));
            yield return null;
            Require(PilotInventory.State.weapons.Contains(5), "Boss guarantees legendary drop before transition");
            Capture("09-legendary-reward");
            yield return null;
            yield return new WaitForSecondsRealtime(3);
            Require(PilotInventory.State.weapons.Contains(5), "Legendary survives return to hideout");

            // Capture additional environments after the campaign objectives and reward persistence pass.
            CampaignSession.Arcade();

            foreach (int level in new[] { 3, 6, 7 })
            {
                GameSession.SelectedLevel = level;
                SceneManager.LoadScene(GameSession.CombatScene);
                yield return new WaitForSecondsRealtime(2);
                pilot = FindObjectOfType<PlayerController>();
                pilot.health.invulnerable = true;
                Capture("planet-" + level);
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        IEnumerator RadarProbe(PlayerController pilot)
        {
            Vector3 saved = pilot.transform.position;
            pilot.transform.position = missionClearPoint(pilot);
            yield return new WaitForSecondsRealtime(0.4f);
            var view = FlightView.Instance.ViewCamera;
            Vector3 origin = view.transform.position;
            Vector3 forward = view.transform.forward;
            var prefab = FindObjectOfType<Game.Enemies.EnemyWaveSpawner>().enemyPrefab;
            var target = Instantiate(prefab, origin + forward * 40, Quaternion.LookRotation(-forward));

            // This stationary fixture disables EnemyController before its normal Start conversion.
            Game.Graphics.GraphicsSettingsManager.PrepareHierarchy(target);
            var controller = target.GetComponent<Game.Enemies.EnemyController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            var acePilot = target.GetComponent<Game.AI.AcePilot>();
            if (acePilot != null)
            {
                acePilot.enabled = false;
            }

            var health = target.GetComponent<Game.Combat.Health>();
            health.maxHealth = 10;
            health.ResetHealth();
            health.ApplyDamage(new DamageInfo(3, DamageSource.PlayerProjectile, pilot.gameObject, target.transform.position, Vector3.up));
            var definition = pilot.weapon.definition;
            var pool = Game.Combat.ProjectilePool.Instance;
            var missile = pool.Get(definition.projectilePrefab);
            missile.Launch(origin + forward * 30 + view.transform.right * 12, Vector3.up, new Game.Combat.ProjectileLaunchData(0.01f, 1, 0.1f, 10, DamageSource.EnemyProjectile, target, GameLayers.EnemyWeaponHitMask), pool);
            SalvagePickup.Create(origin + forward * 35 - view.transform.right * 12, 0, 1, -1, 0, false);
            yield return null;
            Game.Combat.RadarIndex.Synchronize(true);
            var contacts = new List<Game.Combat.RadarContact>();
            Game.Combat.RadarIndex.Query(pilot.transform.position, 150, Game.Combat.RadarKind.All, contacts);
            bool hostile = false;
            bool shot = false;
            bool salvage = false;

            foreach (var radarContact in contacts)
            {
                hostile |= radarContact.gameObject == target;
                shot |= radarContact.gameObject == missile.gameObject;
                salvage |= radarContact.Kind == Game.Combat.RadarKind.Salvage;
            }

            Require(hostile && shot && salvage, "Live octree includes hostile projectile and physical salvage");
            Vector3 desired = (target.transform.position - origin).normalized;
            Require(Vector3.Angle(pilot.weapon.AcquireDirection(origin, Quaternion.AngleAxis(1, Vector3.up) * desired), desired) < 0.01f, "Live weapon acquires nearby hostile from octree");
            yield return new WaitForSecondsRealtime(0.2f);
            Capture("08c-hostile-bracket-radar-crosshair");
            yield return null;
            Destroy(target);
            missile.Despawn();
            pilot.transform.position = saved;
        }

        static Vector3 missionClearPoint(PlayerController pilot) => pilot.transform.position + new Vector3(600, 400, 600);

        IEnumerator Assemble(HangarMenu menu, string identifier, string capture)
        {
            var recipe = AssemblyRecipes.Find(identifier);
            Click("Recipe " + identifier);
            yield return null;
            var state = PilotInventory.State;
            int before = state.credits;
            int creditCost = recipe.Price(state);

            foreach (var part in recipe.Pattern(state))
            {
                if (part.Count == 0)
                {
                    continue;
                }

                Click("Material choice " + part.Material);
                yield return null;
                int left = part.Count;

                foreach (int count in new[] { 10, 5, 1 })
                {
                    if (left < count)
                    {
                        continue;
                    }

                    Click("×" + count);
                    yield return null;

                    while (left >= count)
                    {
                        Click("Assembly slot " + part.Position);
                        left -= count;
                        yield return null;
                    }
                }
            }

            Require(menu.Bench.Matches(state, recipe), "UI exact grid matches " + identifier);
            if (capture != null)
            {
                Capture(capture);
                yield return null;
            }

            Click("ASSEMBLE");
            yield return null;
            Require(state.credits == before - creditCost && !menu.Bench.Matches(state, recipe), "UI craft consumes exact fee and clears " + identifier);
        }

        static void Click(string name)
        {
            foreach (var button in FindObjectsOfType<Button>())
            {
                if (button.name == name)
                {
                    if (!button.interactable)
                    {
                        throw new Exception("Disabled UI control " + name);
                    }

                    button.onClick.Invoke();
                    return;
                }
            }

            throw new Exception("Missing UI control " + name);
        }

        void Require(bool value, string label)
        {
            if (!value)
            {
                throw new Exception(label);
            }

            results.Add("PASS " + label);
            Debug.Log("[CampaignSmoke] PASS " + label);
        }

        static void RequireTransient(bool value, string label)
        {
            if (!value)
            {
                throw new Exception(label);
            }
        }

        static void ClearEnemies(PlayerController pilot)
        {
            foreach (var enemy in FindObjectsOfType<Game.Enemies.EnemyController>())
            {
                enemy.GetComponent<Game.Combat.Health>().ApplyDamage(new DamageInfo(100000, DamageSource.PlayerProjectile, pilot.gameObject, enemy.transform.position, Vector3.up));
            }
        }

        void Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(output, name + ".png"));
        }

        void Finish()
        {
            results.AddRange(errors.ConvertAll(errorMessage => "ERROR " + errorMessage));
            results.Add("Errors: " + errors.Count);
            File.WriteAllLines(Path.Combine(output, "results.txt"), results);
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }
    }
}
#endif
