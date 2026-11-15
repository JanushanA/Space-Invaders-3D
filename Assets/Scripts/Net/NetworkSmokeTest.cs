#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Enemies;
using Game.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Net
{
    // Opt-in host/client integration checks, coordinated through marker files in a shared directory.
    // Command-line flags enable the probe; ordinary launches never create it or disable saves.
    public sealed class NetworkSmokeTest : MonoBehaviour
    {
        private bool host;
        private string directory;
        private string role;
        private float deadline;
        private bool finished;
        private string step = "startup";

        // Reflection exercises existing private gameplay hooks; those field/method names are test contracts.
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool hostMode = Array.IndexOf(args, "-integrationNetworkHost") >= 0;
            bool clientMode = Array.IndexOf(args, "-integrationNetworkClient") >= 0;
            if (!hostMode && !clientMode)
            {
                return;
            }

            GameSession.SuppressSave = true;
            var sceneObject = new GameObject("NetworkSmokeTest");
            DontDestroyOnLoad(sceneObject);
            var smoke = sceneObject.AddComponent<NetworkSmokeTest>();
            smoke.host = hostMode;
            smoke.role = hostMode ? "host" : "client";
            int index = Array.IndexOf(args, "-integrationNetworkDir");
            smoke.directory = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) :
                Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "network-smoke"));
            Directory.CreateDirectory(smoke.directory);
            smoke.deadline = Time.realtimeSinceStartup + 60f;
            Application.runInBackground = true;
            smoke.StartCoroutine(smoke.Run());
        }

        private void Update()
        {
            GameSession.SuppressSave = true;
            if (!finished && Time.realtimeSinceStartup > deadline)
            {
                Finish(false, "timeout at " + step);
            }
        }

        private IEnumerator Run()
        {
            yield return null; // Let the menu initialise before creating the session.
            var runner = NetRunner.Ensure();
            if (host)
            {
                Mark("creating lobby");
                runner.CreateLobby();

                while (NetSession.Code.Length == 0)
                {
                    yield return null;
                }

                File.WriteAllText(Path.Combine(directory, "lobby-code.txt"), NetSession.Code);
                Mark("waiting for client");

                while (NetSession.PlayerCount != 2)
                {
                    yield return null;
                }

                runner.HostStart();
            }
            else
            {
                Mark("waiting for code");
                string codePath = Path.Combine(directory, "lobby-code.txt");

                while (!File.Exists(codePath))
                {
                    yield return null;
                }

                string code = File.ReadAllText(codePath).Trim();

                while (code.Length == 0)
                {
                    yield return null;
                    code = File.ReadAllText(codePath).Trim();
                }

                runner.JoinLobby(code);
            }

            NetCombat previous = null;

            for (int round = 1; round <= 2; round++)
            {
                Mark("waiting for combat round " + round);

                while (!NetSession.InMatch || NetCombat.Instance == null || NetCombat.Instance == previous ||
                    SceneManager.GetActiveScene().name != GameSession.CombatScene)
                {
                    yield return null;
                }

                NetCombat combat = NetCombat.Instance;
                previous = combat;
                PlayerController pilot = FindObjectOfType<PlayerController>();
                if (pilot == null || pilot.weapon == null)
                {
                    Finish(false, "missing local pilot/weapon");
                    yield break;
                }

                pilot.enabled = false; // Script a deterministic pose through the actual network sender.
                PlayerRoster.Add(pilot.transform);
                if (!host)
                {
                    SkillTree tree = pilot.GetComponent<SkillTree>();
                    var unlocked = (HashSet<SkillId>)typeof(SkillTree).GetField("unlocked", Fields).GetValue(tree);

                    foreach (SkillId skill in new[] { SkillId.Health1, SkillId.Damage1, SkillId.Repair, SkillId.Shield, SkillId.Bomb })
                    {
                        unlocked.Add(skill);
                    }

                    pilot.GetComponent<PlayerSkills>().ApplyTreeBonuses();
                }

                Health local = pilot.GetComponent<Health>();
                local.invulnerable = true;
                bool sawRemoteShot = false;
                bool sawPose = false;
                bool sawEnemies = false;
                float until = Time.realtimeSinceStartup + 12f;

                while (!(sawRemoteShot && sawPose && sawEnemies))
                {
                    if (Time.realtimeSinceStartup > until)
                    {
                        Finish(false, "round " + round + " pose=" + sawPose + " remoteShot=" + sawRemoteShot + " enemies=" + sawEnemies);
                        yield break;
                    }

                    pilot.transform.position = new Vector3(host ? -30f : 30f, 20f, -6f);
                    pilot.weapon.TryFire();
                    var avatars = (GameObject[])typeof(NetCombat).GetField("avatars", Fields).GetValue(combat);
                    int remoteSlot = host ? 1 : 0;
                    GameObject avatar = avatars[remoteSlot];
                    if (avatar != null)
                    {
                        avatar.GetComponent<Health>().invulnerable = true;
                        sawPose |= Mathf.Abs(avatar.transform.position.x - (host ? 30f : -30f)) < 0.5f;

                        foreach (Projectile projectile in FindObjectsOfType<Projectile>())
                        {
                            var owner = (GameObject)typeof(Projectile).GetField("instigator", Fields).GetValue(projectile);
                            bool flying = (bool)typeof(Projectile).GetField("inFlight", Fields).GetValue(projectile);
                            if (flying && owner == avatar)
                            {
                                float damage = (float)typeof(Projectile).GetField("damage", Fields).GetValue(projectile);
                                sawRemoteShot = !host || damage > pilot.weapon.definition.damagePerProjectile;
                            }
                        }
                    }

                    var enemies = (Dictionary<int, GameObject>)typeof(NetCombat).GetField(host ? "hostEnemies" : "ghosts", Fields).GetValue(combat);
                    sawEnemies |= enemies.Count > 0;
                    yield return null;
                }

                Mark("round " + round + " movement/fire/enemy sync passed via " + (NetSession.ViaRelay ? "Relay" : "LAN"));
                File.WriteAllText(Path.Combine(directory, role + "-round" + round + ".txt"), "PASS");

                while (!File.Exists(Path.Combine(directory, (host ? "client" : "host") + "-round" + round + ".txt")))
                {
                    pilot.weapon.TryFire();
                    yield return null;
                }

                if (round == 1)
                {
                    yield return CheckProjectileInterception(runner, combat);
                    if (finished)
                    {
                        yield break;
                    }

                    yield return CheckGuestAuthority(runner, combat, pilot);
                    if (finished)
                    {
                        yield break;
                    }

                    if (host)
                    {
                        int oldRound = runner.MatchRound;

                        while (runner.MatchRound == oldRound)
                        {
                            runner.HostStart();
                            yield return null;
                        }
                    }
                }
                else if (host)
                {
                    runner.ReturnEveryoneToLobby();
                }
            }

            Mark("waiting for lobby return");

            while (NetSession.InMatch || SceneManager.GetActiveScene().name != GameSession.MenuScene)
            {
                yield return null;
            }

            if (!NetSession.Active)
            {
                Finish(false, "session lost during lobby return");
                yield break;
            }

            if (host)
            {
                while (!File.Exists(Path.Combine(directory, "client-invalid-join.txt")) || NetSession.PlayerCount != 1)
                {
                    yield return null;
                }

                runner.Leave();
                Finish(!NetSession.Active, "guest abilities/rewards/result, two rounds, lobby return, invalid join and disconnect");
            }
            else
            {
                runner.Leave();
                File.WriteAllText(Path.Combine(directory, "client-left.txt"), "PASS");
                Mark("checking invalid join");
                runner.JoinLobby("ZZZZ");

                while (NetSession.Active)
                {
                    yield return null;
                }

                if (NetSession.Status != "NO LOBBY FOUND" && NetSession.Status != "JOIN REJECTED")
                {
                    Finish(false, "invalid join ended unexpectedly: " + NetSession.Status);
                    yield break;
                }

                File.WriteAllText(Path.Combine(directory, "client-invalid-join.txt"), "PASS");
                Finish(true, "guest abilities/rewards/result, two rounds, lobby return and invalid join");
            }
        }

        private IEnumerator CheckProjectileInterception(NetRunner runner, NetCombat combat)
        {
            Mark("checking exact hostile projectile interception");
            string idsPath = Path.Combine(directory, "interception-ids.txt");
            string seenPath = Path.Combine(directory, "interception-seen.txt");
            string donePath = Path.Combine(directory, "interception-guest-pass.txt");
            if (host)
            {
                var definition = (WeaponDefinition)typeof(NetCombat).GetField("enemyWeaponDef", Fields).GetValue(combat);
                if (definition == null || definition.projectilePrefab == null)
                {
                    Finish(false, "interception enemy projectile definition missing");
                    yield break;
                }

                var emitter = new GameObject("Network interception fixture");
                emitter.transform.position = new Vector3(0, 5000, 0);
                var pool = ProjectilePool.Instance;
                var first = pool.Get(definition.projectilePrefab);
                var second = pool.Get(definition.projectilePrefab);
                var data = new ProjectileLaunchData(0.1f, 0, definition.projectileRadius, 20, DamageSource.EnemyProjectile, emitter, GameLayers.EnemyWeaponHitMask);
                Vector3 firstShotPosition = emitter.transform.position + Vector3.forward * 5;
                Vector3 secondShotPosition = emitter.transform.position + Vector3.forward * 20;
                first.Launch(firstShotPosition, Vector3.right, data, pool);
                runner.NoteFire(firstShotPosition, Vector3.right, data.Source, first);
                second.Launch(secondShotPosition, Vector3.right, data, pool);
                runner.NoteFire(secondShotPosition, Vector3.right, data.Source, second);
                uint cancelledId = first.NetworkId;
                uint keptId = second.NetworkId;
                if (cancelledId == 0 || keptId == 0 || cancelledId == keptId)
                {
                    Finish(false, "host enemy projectiles lack distinct IDs");
                    yield break;
                }

                File.WriteAllText(idsPath + ".tmp", cancelledId + "," + keptId);
                File.Move(idsPath + ".tmp", idsPath);

                while (!File.Exists(seenPath))
                {
                    yield return null;
                }

                RadarIndex.Synchronize(true);
                float nextPulse = 0;
                var contacts = new List<RadarContact>();
                SkillSupportEffects.Intercept(emitter, 1u << (int)SkillId.RapidFire, ref nextPulse, contacts);
                if (first.InFlight || !second.InFlight)
                {
                    Finish(false, "host point defence did not select the in-range missile exclusively");
                    yield break;
                }

                while (!File.Exists(donePath))
                {
                    yield return null;
                }

                second.Despawn();
                Destroy(emitter);
            }
            else
            {
                while (!File.Exists(idsPath))
                {
                    yield return null;
                }

                string[] projectileIdTexts = File.ReadAllText(idsPath).Split(',');
                uint cancelledId = uint.Parse(projectileIdTexts[0]);
                uint keptId = uint.Parse(projectileIdTexts[1]);
                var visuals = (Dictionary<uint, Projectile>)typeof(NetCombat).GetField("networkProjectiles", Fields).GetValue(combat);
                Projectile first = null;
                Projectile second = null;

                while (!visuals.TryGetValue(cancelledId, out first) || !visuals.TryGetValue(keptId, out second))
                {
                    yield return null;
                }

                if (!first.InFlight || !second.InFlight)
                {
                    Finish(false, "guest enemy visual launch missing");
                    yield break;
                }

                if (Vector3.Distance(first.Velocity, Vector3.right * 0.1f) > 0.0001f || Vector3.Distance(second.Velocity, Vector3.right * 0.1f) > 0.0001f)
                {
                    Finish(false, "guest hostile projectile velocity differs from host's actual .1 m/s launch");
                    yield break;
                }

                File.WriteAllText(seenPath, "PASS");

                while (first != null && first.InFlight && first.NetworkId == cancelledId)
                {
                    yield return null;
                }

                if (second == null || !second.InFlight || second.NetworkId != keptId)
                {
                    Finish(false, "interception removed a different nearby guest missile");
                    yield break;
                }

                if (visuals.ContainsKey(cancelledId))
                {
                    Finish(false, "intercepted guest missile retained its ID mapping");
                    yield break;
                }

                var cancelled = (SortedSet<uint>)typeof(NetCombat).GetField("cancelledProjectiles", Fields).GetValue(combat);
                if (!cancelled.Contains(cancelledId))
                {
                    Finish(false, "guest missile expired without authoritative interception receipt");
                    yield break;
                }

                File.WriteAllText(donePath, "PASS");
            }

            Mark("exact hostile projectile interception replicated; nearby missile remained");
        }

        private IEnumerator CheckGuestAuthority(NetRunner runner, NetCombat combat, PlayerController pilot)
        {
            Mark("checking guest authority");
            if (host)
            {
                // Freeze unrelated hazards after real movement/fire/snapshot checks.
                EnemyWaveSpawner spawner = FindObjectOfType<EnemyWaveSpawner>();
                spawner.enabled = false;

                foreach (EnemyController enemy in FindObjectsOfType<EnemyController>())
                {
                    enemy.enabled = false;
                }

                foreach (Weapon weapon in FindObjectsOfType<Weapon>())
                {
                    if (weapon.damageSource == DamageSource.EnemyProjectile)
                    {
                        weapon.enabled = false;
                    }
                }

                var avatars = (GameObject[])typeof(NetCombat).GetField("avatars", Fields).GetValue(combat);
                Health guest = avatars[1].GetComponent<Health>();

                while (!combat.HasLoadout(1) || guest.maxHealth < 4f)
                {
                    yield return null;
                }

                guest.invulnerable = false;
                guest.regenPerSecond = 0f;
                guest.ApplyDamage(new DamageInfo(1f, DamageSource.Script, gameObject, guest.transform.position, Vector3.up));
                File.WriteAllText(Path.Combine(directory, "guest-damaged.txt"), "PASS");

                while (!File.Exists(Path.Combine(directory, "guest-repaired.txt")))
                {
                    yield return null;
                }

                if (guest.CurrentHealth != guest.maxHealth)
                {
                    Finish(false, "guest repair not authoritative");
                    yield break;
                }

                guest.ApplyDamage(new DamageInfo(1f, DamageSource.Script, gameObject, guest.transform.position, Vector3.up));
                File.WriteAllText(Path.Combine(directory, "guest-damaged-again.txt"), "PASS");

                while (!File.Exists(Path.Combine(directory, "guest-cooldown.txt")))
                {
                    yield return null;
                }

                if (guest.CurrentHealth != guest.maxHealth - 1f)
                {
                    Finish(false, "guest bypassed host repair cooldown");
                    yield break;
                }

                while (!guest.invulnerable)
                {
                    yield return null;
                }

                guest.ApplyDamage(new DamageInfo(99f, DamageSource.EnemyProjectile, gameObject, guest.transform.position, Vector3.up));
                if (!guest.IsAlive || guest.CurrentHealth != guest.maxHealth - 1f)
                {
                    Finish(false, "guest shield failed on host");
                    yield break;
                }

                GameObject target = Instantiate(spawner.enemyPrefab, guest.transform.position + Vector3.forward * 3f, Quaternion.identity);
                target.name = "NetworkBombRewardTarget";
                Health targetHealth = target.GetComponent<Health>();
                targetHealth.maxHealth = 3f;
                targetHealth.ResetHealth();
                Weapon targetWeapon = target.GetComponentInChildren<Weapon>();
                if (targetWeapon != null)
                {
                    targetWeapon.enabled = false;
                }

                combat.RegisterEnemy(target);
                File.WriteAllText(Path.Combine(directory, "guest-bomb-ready.txt"), "PASS");

                while (targetHealth.IsAlive)
                {
                    yield return null;
                }

                if (guest.CurrentHealth != guest.maxHealth - 1f)
                {
                    Finish(false, "bomb hurt its pilot");
                    yield break;
                }

                Destroy(target);

                while (!File.Exists(Path.Combine(directory, "guest-reward.txt")))
                {
                    yield return null;
                }

                runner.AwardSalvage(guest.gameObject, 3, 3, 5, 400, true);

                while (!File.Exists(Path.Combine(directory, "guest-salvage.txt")))
                {
                    yield return null;
                }

                if (!Game.Progression.PilotInventory.State.weapons.Contains(5))
                {
                    Finish(false, "host party legendary missing");
                    yield break;
                }

                runner.CompletePartyLevel(1, 3);

                while (!File.Exists(Path.Combine(directory, "guest-result.txt")))
                {
                    yield return null;
                }

                Mark("guest loadout, damage, repair cooldown, shield, bomb, reward and sector result passed");
            }
            else
            {
                Health health = pilot.GetComponent<Health>();
                PlayerSkills skills = pilot.GetComponent<PlayerSkills>();
                var useSkillMethod = typeof(PlayerSkills).GetMethod("UseSkill", Fields);

                while (!File.Exists(Path.Combine(directory, "guest-damaged.txt")) || health.CurrentHealth == health.maxHealth)
                {
                    yield return null;
                }

                if (!(bool)useSkillMethod.Invoke(skills, new object[] { SkillId.Repair }))
                {
                    Finish(false, "client repair input rejected");
                    yield break;
                }

                while (health.CurrentHealth != health.maxHealth)
                {
                    yield return null;
                }

                File.WriteAllText(Path.Combine(directory, "guest-repaired.txt"), "PASS");

                while (!File.Exists(Path.Combine(directory, "guest-damaged-again.txt")) || health.CurrentHealth == health.maxHealth)
                {
                    yield return null;
                }

                useSkillMethod.Invoke(skills, new object[] { SkillId.Repair });
                float until = Time.realtimeSinceStartup + 0.6f;

                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }

                if (health.CurrentHealth != health.maxHealth - 1f)
                {
                    Finish(false, "duplicate repair bypassed host cooldown");
                    yield break;
                }

                File.WriteAllText(Path.Combine(directory, "guest-cooldown.txt"), "PASS");
                if (!(bool)useSkillMethod.Invoke(skills, new object[] { SkillId.Shield }))
                {
                    Finish(false, "client shield input rejected");
                    yield break;
                }

                while (!File.Exists(Path.Combine(directory, "guest-bomb-ready.txt")))
                {
                    yield return null;
                }

                PlayerProgression progression = pilot.GetComponent<PlayerProgression>();
                int score = GameSession.RunScore;
                int experiencePoints = progression.Xp;
                int level = progression.Level;
                if (!(bool)useSkillMethod.Invoke(skills, new object[] { SkillId.Bomb }))
                {
                    Finish(false, "client bomb input rejected");
                    yield break;
                }

                while (GameSession.RunScore <= score || (progression.Xp <= experiencePoints && progression.Level <= level))
                {
                    yield return null;
                }

                File.WriteAllText(Path.Combine(directory, "guest-reward.txt"), "PASS");

                while (!Game.Progression.PilotInventory.State.weapons.Contains(5) || Game.Progression.PilotInventory.State.materials[3] < 3)
                {
                    yield return null;
                }

                File.WriteAllText(Path.Combine(directory, "guest-salvage.txt"), "PASS");
                Mark("guest received host-authoritative materials and legendary weapon");

                while ((int)typeof(NetRunner).GetField("completedRound", Fields).GetValue(runner) != runner.MatchRound)
                {
                    yield return null;
                }

                File.WriteAllText(Path.Combine(directory, "guest-result.txt"), "PASS");
                Mark("guest repair, cooldown, shield, bomb XP/score and sector result received");
            }
        }

        private void Mark(string value)
        {
            step = value;
            Debug.Log("NETWORK SMOKE " + role + ": " + step);
            File.AppendAllText(Path.Combine(directory, role + "-steps.txt"), step + Environment.NewLine);
        }

        private void Finish(bool passed, string message)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            string result = (passed ? "PASS: " : "FAIL: ") + message;
            File.WriteAllText(Path.Combine(directory, role + "-result.txt"), result);
            Debug.Log("NETWORK SMOKE " + role + " " + result);
            if (NetRunner.Instance != null)
            {
                NetRunner.Instance.Leave();
            }

            Application.Quit(passed ? 0 : 1);
        }
    }
}
#endif
