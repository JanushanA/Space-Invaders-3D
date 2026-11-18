using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using Unity.Networking.Transport;
using Debug = UnityEngine.Debug;
using Game.Net;
using UnityEngine;

// Exercises packet admission, combat authority and snapshot transport using isolated editor fixtures.
public static class NetworkRegressionChecks
{
    public static void Run()
    {
        var failures = new List<string>();
        var sceneObject = new GameObject("NetworkRegressionChecks");
        var runner = sceneObject.AddComponent<NetRunner>();
        var handle = typeof(NetRunner).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
        var trusted = new IPEndPoint(IPAddress.Loopback, 30001);
        var stranger = new IPEndPoint(IPAddress.Loopback, 30002);
        Action<byte[], IPEndPoint> receive = (data, from) => handle.Invoke(runner, new object[] { data, from });
        Action<string, Action> check = (name, test) =>
        {
            try
            {
                test();
                Debug.Log("NETWORK PASS: " + name);
            }
            catch (Exception error)
            {
                failures.Add(name + ": " + error.GetBaseException().Message);
            }
        };
        try
        {
            NetSession.Reset();
            NetSession.Active = true;
            NetSession.IsHost = true;
            NetSession.SetSlot(0, true);
            check("host rejects guest lobby state", () =>
            {
                receive(new byte[] { 83, 3, 8, 1, 1, 0, 0, 0, 0 }, stranger);
                Require(NetSession.SelectedLevel == 1, "guest changed host level");
            });
            check("truncated handshake cannot throw", () => receive(new byte[] { 83, 1 }, stranger));
            check("guest cannot trigger party failure", () =>
            {
                bool failed = false;
                runner.MatchFailed += () => failed = true;
                receive(new byte[] { 83, 13 }, stranger);
                Require(!failed, "guest triggered party failure");
            });
            check("full lobby preserves all slot owners", () =>
            {
                NetSession.Code = "ABCD";
                byte[] hello = { 83, 1, 65, 66, 67, 68, 32, 32, 32, 32 };

                for (int pilotSlot = 0; pilotSlot < 5; pilotSlot++)
                {
                    receive(hello, new IPEndPoint(IPAddress.Loopback, 31000 + pilotSlot));
                }

                Require(NetSession.PlayerCount == 5, "lobby exceeded five pilots");
                var peers = (IPEndPoint[])typeof(NetRunner).GetField("peers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner);
                Require(peers[4].Port == 31003, "full lobby replaced an existing pilot");
                receive(hello, new IPEndPoint(IPAddress.Loopback, 31000));
                Require(NetSession.PlayerCount == 5, "repeated hello duplicated a pilot");
                receive(new byte[] { 83, 10 }, new IPEndPoint(IPAddress.Loopback, 31001));
                Require(NetSession.PlayerCount == 4 && !NetSession.SlotTaken(2), "leave did not free owned slot");
                receive(hello, new IPEndPoint(IPAddress.Loopback, 31004));
                Require(NetSession.PlayerCount == 5 && peers[2].Port == 31004, "freed slot was not reusable");
            });
            check("pose cannot steal another pilot slot", () =>
            {
                NetSession.InMatch = true;
                var peers = (IPEndPoint[])typeof(NetRunner).GetField("peers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner);
                byte[] pose = new byte[28];
                pose[0] = 83;
                pose[1] = 6;
                pose[2] = 2;
                pose[27] = 1;
                receive(pose, peers[1]);
                Require(peers[2].Port == 31004, "pose replaced another endpoint owner");
                NetSession.InMatch = false;
            });
            NetSession.Reset();
            NetSession.Active = true;
            typeof(NetRunner).GetField("hostEnd", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runner, trusted);
            check("client rejects strangers", () =>
            {
                receive(new byte[] { 83, 3, 8, 1, 1, 0, 0, 0, 0 }, stranger);
                Require(NetSession.SelectedLevel == 1, "stranger changed client lobby");
            });
            check("invalid lobby is atomic", () =>
            {
                receive(new byte[] { 83, 3, 8, 5, 1, 0 }, trusted);
                Require(NetSession.SelectedLevel == 1, "partial packet changed level");
            });
            check("duplicate or older starts do not mutate the current match", () =>
            {
                typeof(NetRunner).GetField("matchRound", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runner, 2);
                receive(new byte[] { 83, 5, 8, 1, 1, 0, 0, 0, 0, 2, 0, 0, 0 }, trusted);
                receive(new byte[] { 83, 5, 8, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0 }, trusted);
                Require(NetSession.SelectedLevel == 1 && !NetSession.InMatch, "old start reloaded match state");
            });
            check("valid host lobby updates client", () =>
            {
                receive(new byte[] { 83, 3, 2, 2, 1, 1, 0, 0, 0 }, trusted);
                Require(NetSession.SelectedLevel == 2 && NetSession.PlayerCount == 2 && NetSession.SlotTaken(1), "valid host lobby was ignored");
            });
            check("leave clears stale host endpoint", () =>
            {
                runner.Leave();
                Require(typeof(NetRunner).GetField("hostEnd", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(runner) == null, "old endpoint survived Leave");
            });
            check("inactive runner ignores packets", () =>
            {
                receive(new byte[] { 83, 3, 8, 1, 1, 0, 0, 0, 0 }, trusted);
                Require(NetSession.SelectedLevel == 1, "inactive session accepted lobby");
            });
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sceneObject);
            NetSession.Reset();
        }

        if (failures.Count != 0)
        {
            throw new Exception("Network regressions: " + string.Join("; ", failures));
        }

        Debug.Log("NETWORK REGRESSION CHECKS PASSED");
    }

    // Exercise authoritative damage, skill cooldowns and reward deduplication on real health components.
    public static void RunCombatAuthority()
    {
        bool suppress = Game.Core.GameSession.SuppressSave;
        Game.Core.GameSession.SuppressSave = true;
        var owner = new GameObject("BombOwner");
        var friend = new GameObject("FriendlyPilot");
        var enemy = new GameObject("BombEnemy");
        var runnerGo = new GameObject("AuthorityRunner");
        var combatGo = new GameObject("AuthorityCombat");
        try
        {
            NetSession.Reset();
            friend.layer = LayerMask.NameToLayer(Game.Core.GameLayers.Player);
            var friendly = friend.AddComponent<Game.Combat.Health>();
            enemy.layer = LayerMask.NameToLayer(Game.Core.GameLayers.Enemy);
            var enemyHealth = enemy.AddComponent<Game.Combat.Health>();
            enemyHealth.deactivateOnDeath = false;

            // EditMode AddComponent does not run the runtime Awake that registers each ship
            // in the radar index. Exercise that real initialization, not a scene-scan fallback.
            var awake = typeof(Game.Combat.Health).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake.Invoke(friendly, null);
            awake.Invoke(enemyHealth, null);
            Require(enemy.GetComponent<Game.Combat.RadarContact>() != null, "enemy Awake did not register its radar contact");
            var skills = owner.AddComponent<Game.Player.PlayerSkills>();
            typeof(Game.Player.PlayerSkills).GetMethod("Detonate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(skills, new object[] { 22f });
            Require(friendly.CurrentHealth == friendly.maxHealth, "bomb damaged friendly pilot");
            Require(enemyHealth.CurrentHealth < enemyHealth.maxHealth, "bomb did not damage enemy");
            NetSession.Active = true;
            NetSession.IsHost = true;
            NetSession.InMatch = true;
            var ownerHealth = owner.AddComponent<Game.Combat.Health>();
            ownerHealth.maxHealth = 3f;
            ownerHealth.deactivateOnDeath = false;
            ownerHealth.ResetHealth();
            var authority = owner.AddComponent<RemotePilotAuthority>();
            uint unlockedSkills = (1u << (int)Game.Player.SkillId.Health1) | (1u << (int)Game.Player.SkillId.Health2) |
                (1u << (int)Game.Player.SkillId.Damage1) | (1u << (int)Game.Player.SkillId.Damage2) |
                (1u << (int)Game.Player.SkillId.Repair) | (1u << (int)Game.Player.SkillId.Shield);
            authority.ApplyLoadout(unlockedSkills);
            Require(ownerHealth.maxHealth == 5f && authority.DamageMultiplier == 1.5f, "guest passive loadout was not applied on host");
            ownerHealth.ApplyDamage(new Game.Core.DamageInfo(1f, Game.Core.DamageSource.Script, null, Vector3.zero, Vector3.up));
            Require(authority.TryUse(Game.Player.SkillId.Repair) && ownerHealth.CurrentHealth == 5f, "host repair did not restore guest health");
            Require(!authority.TryUse(Game.Player.SkillId.Repair), "repair cooldown was not enforced by host");
            Require(authority.TryUse(Game.Player.SkillId.Shield), "host shield was rejected");
            ownerHealth.ApplyDamage(new Game.Core.DamageInfo(99f, Game.Core.DamageSource.Script, null, Vector3.zero, Vector3.up));
            Require(ownerHealth.CurrentHealth == 5f, "guest shield did not block host damage");
            NetSession.Reset();
            var runner = runnerGo.AddComponent<NetRunner>();
            var combat = combatGo.AddComponent<NetCombat>();
            NetCombat.Instance = combat;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(NetCombat).GetField("localHealth", flags).SetValue(combat, friendly);
            var trusted = new IPEndPoint(IPAddress.Loopback, 30001);
            typeof(NetRunner).GetField("hostEnd", flags).SetValue(runner, trusted);
            typeof(NetRunner).GetField("matchRound", flags).SetValue(runner, 2);
            NetSession.Active = true;
            NetSession.InMatch = true;
            var handle = typeof(NetRunner).GetMethod("Handle", flags);
            handle.Invoke(runner, new object[] { new byte[] { 83, 9, 0, 0, 0, 0, 0 }, trusted });
            Require(friendly.IsAlive, "unversioned health packet killed current-round pilot");
            bool failed = false;
            runner.MatchFailed += () => failed = true;
            handle.Invoke(runner, new object[] { new byte[] { 83, 13 }, trusted });
            Require(!failed, "unversioned failure ended current round");
            handle.Invoke(runner, new object[] { HealthPacket(2, 2, 10f), trusted });
            handle.Invoke(runner, new object[] { HealthPacket(2, 1, 0f), trusted });
            handle.Invoke(runner, new object[] { HealthPacket(1, 3, 0f), trusted });
            Require(friendly.CurrentHealth == 10f && friendly.IsAlive, "stale health changed current-round pilot");
            var pilot = owner.AddComponent<Game.Player.PlayerController>();
            var progression = owner.AddComponent<Game.Player.PlayerProgression>();
            typeof(Game.Player.PlayerProgression).GetField("xpToNext", flags).SetValue(progression, 40);
            typeof(NetCombat).GetField("localPilot", flags).SetValue(combat, pilot);
            typeof(NetCombat).GetField("<Round>k__BackingField", flags).SetValue(combat, 2);
            int scoreBefore = Game.Core.GameSession.RunScore;
            int xpBefore = progression.Xp;
            byte[] reward = ReliablePacket(2, 77, new byte[] { 83, 19, 20, 0, 0, 0, 1 });
            handle.Invoke(runner, new object[] { reward, trusted });
            handle.Invoke(runner, new object[] { reward, trusted });
            handle.Invoke(runner, new object[] { ReliablePacket(1, 78, new byte[] { 83, 19, 20, 0, 0, 0, 1 }), trusted });
            Require(Game.Core.GameSession.RunScore == scoreBefore + 20 && progression.Xp == xpBefore + 20, "reward duplicate/old-round packet changed progression");
            Game.Core.GameSession.RunScore = scoreBefore;
            Debug.Log("NETWORK COMBAT AUTHORITY PASSED");
        }
        finally
        {
            NetCombat.Instance = null;
            NetSession.Reset();
            UnityEngine.Object.DestroyImmediate(combatGo);
            UnityEngine.Object.DestroyImmediate(runnerGo);
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(friend);
            UnityEngine.Object.DestroyImmediate(enemy);
            Game.Core.GameSession.SuppressSave = suppress;
        }

        RunProjectileInterception();
    }

    // Validate projectile identities before testing cancellation order and reuse of pooled instances.
    public static void RunProjectileInterception()
    {
        var runnerGo = new GameObject("Interception runner");
        var combatGo = new GameObject("Interception combat");
        var poolGo = new GameObject("Interception pool");
        var prefabGo = new GameObject("Interception prefab");
        var runner = runnerGo.AddComponent<NetRunner>();
        var combat = combatGo.AddComponent<NetCombat>();
        var pool = poolGo.AddComponent<Game.Combat.ProjectilePool>();
        var prefab = prefabGo.AddComponent<Game.Combat.Projectile>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var poolInstance = typeof(Game.Combat.ProjectilePool).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        var previousPool = (Game.Combat.ProjectilePool)poolInstance.GetValue(null);
        var speedDefinition = ScriptableObject.CreateInstance<Game.Combat.WeaponDefinition>();
        var trusted = new IPEndPoint(IPAddress.Loopback, 30101);
        var stranger = new IPEndPoint(IPAddress.Loopback, 30102);
        var handle = typeof(NetRunner).GetMethod("Handle", flags);
        Action<Game.Combat.Projectile> prepare = shot =>
        {
            typeof(Game.Combat.Projectile).GetField("inFlight", flags).SetValue(shot, true);
            typeof(Game.Combat.Projectile).GetField("owningPool", flags).SetValue(shot, pool);
        };
        Action<uint, uint, int, IPEndPoint> receive = (identifier, sequence, round, from) =>
        {
            byte[] payload = new byte[6] { 83, 24, 0, 0, 0, 0 };
            Array.Copy(BitConverter.GetBytes(identifier), 0, payload, 2, 4);
            handle.Invoke(runner, new object[] { ReliablePacket(round, sequence, payload), from });
        };
        try
        {
            NetSession.Reset();
            NetSession.Active = true;
            NetSession.InMatch = true;
            NetCombat.Instance = combat;
            typeof(NetRunner).GetField("hostEnd", flags).SetValue(runner, trusted);
            typeof(NetRunner).GetField("matchRound", flags).SetValue(runner, 5);
            typeof(NetCombat).GetField("<Round>k__BackingField", flags).SetValue(combat, 5);
            var valid = typeof(NetRunner).GetMethod("ValidPacket", BindingFlags.Static | BindingFlags.NonPublic);
            var build = typeof(NetRunner).GetMethod("BuildFire", flags);
            byte[] fire = (byte[])build.Invoke(runner, new object[] { 255, Vector3.zero, Vector3.forward, Game.Core.DamageSource.EnemyProjectile });
            Require((bool)valid.Invoke(null, new object[] { fire }), "legacy fire compatibility was removed");
            Array.Resize(ref fire, 36);
            Array.Copy(BitConverter.GetBytes(5), 0, fire, 28, 4);
            Array.Copy(BitConverter.GetBytes(14u), 0, fire, 32, 4);
            Require((bool)valid.Invoke(null, new object[] { fire }), "identified enemy fire packet rejected");
            fire[27] = (byte)Game.Core.DamageSource.PlayerProjectile;
            Require(!(bool)valid.Invoke(null, new object[] { fire }), "identified packet accepts a forged player source");
            fire[27] = (byte)Game.Core.DamageSource.EnemyProjectile;
            Array.Clear(fire, 32, 4);
            Require(!(bool)valid.Invoke(null, new object[] { fire }), "identified fire accepts zero identity");
            Array.Resize(ref fire, 40);
            Array.Copy(BitConverter.GetBytes(50u), 0, fire, 32, 4);

            foreach (float invalidSpeed in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 0f, 0.099f, 1000.1f })
            {
                Array.Copy(BitConverter.GetBytes(invalidSpeed), 0, fire, 36, 4);
                Require(!(bool)valid.Invoke(null, new object[] { fire }), "hostile fire accepts invalid speed " + invalidSpeed);
            }

            poolInstance.SetValue(null, pool);
            speedDefinition.projectilePrefab = prefab;
            speedDefinition.projectileSpeed = 25;
            typeof(NetCombat).GetField("enemyWeaponDef", flags).SetValue(combat, speedDefinition);
            var visuals = (Dictionary<uint, Game.Combat.Projectile>)typeof(NetCombat).GetField("networkProjectiles", flags).GetValue(combat);
            uint speedId = 50;

            foreach (float expectedSpeed in new[] { 0.1f, speedDefinition.projectileSpeed * 0.85f, 1000f })
            {
                Array.Copy(BitConverter.GetBytes(speedId), 0, fire, 32, 4);
                Array.Copy(BitConverter.GetBytes(expectedSpeed), 0, fire, 36, 4);
                Require((bool)valid.Invoke(null, new object[] { fire }), "bounded hostile speed rejected");
                handle.Invoke(runner, new object[] { fire, trusted });
                Require(visuals.TryGetValue(speedId, out var visual) && Vector3.Distance(visual.Velocity, Vector3.forward * expectedSpeed) < 0.0001f, "guest velocity does not match transmitted hostile speed");
                visual.Despawn();
                speedId++;
            }

            var selected = pool.Get(prefab);
            var neighbour = pool.Get(prefab);
            prepare(selected);
            prepare(neighbour);
            Require(combat.RegisterNetworkProjectile(10, selected) && combat.RegisterNetworkProjectile(11, neighbour), "visual projectile identity registration failed");
            receive(10, 1, 5, stranger);
            receive(10, 2, 4, trusted);
            Require(selected.InFlight && neighbour.InFlight, "stranger or stale-round interception removed a visual");
            handle.Invoke(runner, new object[] { new byte[] { 83, 24, 10, 0, 0, 0 }, trusted });
            Require(selected.InFlight, "unreliable interception bypassed reliable envelope");
            NetSession.IsHost = true;
            var peers = (IPEndPoint[])typeof(NetRunner).GetField("peers", flags).GetValue(runner);
            peers[1] = trusted;
            receive(10, 3, 5, trusted);
            Require(selected.InFlight, "guest interception affected host projectiles");
            NetSession.IsHost = false;
            receive(10, 4, 5, trusted);
            Require(!selected.InFlight && neighbour.InFlight, "authoritative interception did not remove exactly the selected ID");
            var reused = pool.Get(prefab);
            prepare(reused);
            Require(reused == selected, "fixture did not actually reuse its pooled projectile");
            Require(combat.RegisterNetworkProjectile(12, reused), "reused projectile did not accept new identity");
            receive(10, 4, 5, trusted);
            receive(10, 5, 5, trusted);
            Require(reused.InFlight && reused.NetworkId == 12 && neighbour.InFlight, "old or duplicate interception killed a reused/nearby projectile");
            receive(13, 6, 5, trusted);
            var delayed = pool.Get(prefab);
            prepare(delayed);
            Require(!combat.RegisterNetworkProjectile(13, delayed), "cancel-before-launch resurrected the projectile");
            delayed.Despawn();

            for (uint identifier2 = 100; identifier2 < 4300; identifier2++)
            {
                combat.CancelNetworkProjectile(identifier2);
            }

            Require(combat.CancelledProjectileCount <= 4096, "interception tombstones are unbounded");
            Require(!combat.RegisterNetworkProjectile(13, reused), "retired old launch resurrected after tombstone trimming");
            Require(reused.InFlight && neighbour.InFlight, "tombstone trimming despawned unrelated live projectiles");
            var nextRoundShot = pool.Get(prefab);
            prepare(nextRoundShot);
            var freshGo = new GameObject("Next round projectile registry");
            try
            {
                var fresh = freshGo.AddComponent<NetCombat>();
                Require(fresh.CancelledProjectileCount == 0 && fresh.RegisterNetworkProjectile(13, nextRoundShot), "next combat retained previous-round tombstones");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(freshGo);
                NetCombat.Instance = combat;
            }

            Debug.Log("NETWORK PROJECTILE INTERCEPTION PASSED: trusted reliable round, exact ID, neighbour preservation, duplicate/reordered cancellation, pool reuse and bounded round state");
        }
        finally
        {
            NetCombat.Instance = null;
            NetSession.Reset();
            poolInstance.SetValue(null, previousPool);
            UnityEngine.Object.DestroyImmediate(speedDefinition);
            UnityEngine.Object.DestroyImmediate(poolGo);
            UnityEngine.Object.DestroyImmediate(prefabGo);
            UnityEngine.Object.DestroyImmediate(combatGo);
            UnityEngine.Object.DestroyImmediate(runnerGo);
        }
    }

    private static byte[] HealthPacket(int round, uint sequence, float current)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)83);
            writer.Write((byte)9);
            writer.Write((byte)0);
            writer.Write(current);
            writer.Write(100f);
            writer.Write(round);
            writer.Write(sequence);
            return stream.ToArray();
        }
    }

    private static byte[] ReliablePacket(int round, uint sequence, byte[] payload)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)83);
            writer.Write((byte)15);
            writer.Write(round);
            writer.Write(sequence);
            writer.Write(payload);
            return stream.ToArray();
        }
    }

    // Old, duplicate and malformed snapshots must not change the current enemy set.
    public static void RunSnapshotOrdering()
    {
        var runnerGo = new GameObject("SnapshotOrderingRunner");
        var combatGo = new GameObject("SnapshotOrderingCombat");
        var runner = runnerGo.AddComponent<NetRunner>();
        var combat = combatGo.AddComponent<NetCombat>();
        NetCombat.Instance = combat;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var trusted = new IPEndPoint(IPAddress.Loopback, 30001);
        typeof(NetRunner).GetField("hostEnd", flags).SetValue(runner, trusted);
        typeof(NetRunner).GetField("matchRound", flags).SetValue(runner, 2);
        var handle = typeof(NetRunner).GetMethod("Handle", flags);
        var ghosts = (Dictionary<int, GameObject>)typeof(NetCombat).GetField("ghosts", flags).GetValue(combat);
        Action<byte[]> receive = bytes => handle.Invoke(runner, new object[] { bytes, trusted });
        try
        {
            NetSession.Reset();
            NetSession.Active = true;
            NetSession.InMatch = true;
            receive(Snapshot(2, 2, false));
            receive(Snapshot(2, 1, true));
            Require(ghosts.Count == 0, "older snapshot resurrected removed enemy");
            receive(Snapshot(1, 3, true));
            Require(ghosts.Count == 0, "previous match snapshot created enemy");
            receive(Snapshot(2, 3, true));
            Require(ghosts.ContainsKey(7), "fresh snapshot failed to create enemy record");
            receive(Snapshot(2, 3, false));
            Require(ghosts.ContainsKey(7), "duplicate sequence changed enemy state");
            byte[] truncated = Snapshot(2, 4, true);
            Array.Resize(ref truncated, truncated.Length - 1);
            receive(truncated);
            Require(ghosts.ContainsKey(7), "truncated snapshot changed enemy state");
            receive(Snapshot(2, 4, false));
            Require(ghosts.Count == 0, "valid retry after malformed snapshot was rejected");
            Debug.Log("NETWORK SNAPSHOT ORDERING PASSED");
        }
        finally
        {
            NetCombat.Instance = null;
            UnityEngine.Object.DestroyImmediate(combatGo);
            UnityEngine.Object.DestroyImmediate(runnerGo);
            NetSession.Reset();
        }
    }

    private static byte[] Snapshot(int round, uint sequence, bool enemy)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)83);
            writer.Write((byte)7);
            writer.Write(round);
            writer.Write(sequence);
            writer.Write((ushort)(enemy ? 1 : 0));
            if (enemy)
            {
                writer.Write(7); // Enemy id.
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(1f);
                writer.Write((byte)0);
                writer.Write(0f);
                writer.Write(3f);
                writer.Write((byte)0);
            }

            return stream.ToArray();
        }
    }

    // Send a large snapshot over the production transport path to verify fragmentation and delivery.
    public static void RunSnapshotTransport()
    {
        var serverGo = new GameObject("SnapshotTransportServer");
        var clientGo = new GameObject("SnapshotTransportClient");
        var server = serverGo.AddComponent<NetRelay>();
        var client = clientGo.AddComponent<NetRelay>();
        var field = typeof(NetRelay).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            var configure = typeof(NetRelay).GetMethod("ConfigureDriver", BindingFlags.Instance | BindingFlags.NonPublic);
            if (configure == null)
            {
                // Characterise the original default driver path before the fragmentation fix.
                field.SetValue(server, NetworkDriver.Create());
                field.SetValue(client, NetworkDriver.Create());
            }
            else
            {
                configure.Invoke(server, new object[] { new NetworkSettings() });
                configure.Invoke(client, new object[] { new NetworkSettings() });
            }

            var serverDriver = (NetworkDriver)field.GetValue(server);
            var clientDriver = (NetworkDriver)field.GetValue(client);
            Require(serverDriver.Bind(NetworkEndPoint.AnyIpv4) == 0 && serverDriver.Listen() == 0, "server bind/listen failed");
            Require(clientDriver.Bind(NetworkEndPoint.AnyIpv4) == 0, "client bind failed");
            var remote = NetworkEndPoint.LoopbackIpv4;
            remote.Port = serverDriver.LocalEndPoint().Port;
            var clientConnection = clientDriver.Connect(remote);
            NetworkConnection serverConnection = default(NetworkConnection);
            var timer = Stopwatch.StartNew();
            bool connected = false;

            while (timer.ElapsedMilliseconds < 5000 && !connected)
            {
                serverDriver.ScheduleUpdate().Complete();
                clientDriver.ScheduleUpdate().Complete();
                if (!serverConnection.IsCreated)
                {
                    serverConnection = serverDriver.Accept();
                }

                DataStreamReader reader;
                NetworkEvent.Type networkEvent;

                while ((networkEvent = clientDriver.PopEventForConnection(clientConnection, out reader)) != NetworkEvent.Type.Empty)
                {
                    if (networkEvent == NetworkEvent.Type.Connect)
                    {
                        connected = true;
                    }
                }

                Thread.Sleep(1);
            }

            Require(connected && serverConnection.IsCreated, "loopback connection failed");
            byte[] packet = new byte[4096];

            for (int byteIndex = 0; byteIndex < packet.Length; byteIndex++)
            {
                packet[byteIndex] = (byte)(byteIndex % 251);
            }

            var send = typeof(NetRelay).GetMethod("SendOn", BindingFlags.Instance | BindingFlags.NonPublic);
            send.Invoke(server, new object[] { serverConnection, packet });
            bool received = false;
            timer.Restart();

            while (timer.ElapsedMilliseconds < 5000 && !received)
            {
                serverDriver.ScheduleUpdate().Complete();
                clientDriver.ScheduleUpdate().Complete();
                DataStreamReader reader;
                NetworkEvent.Type networkEvent2;

                while ((networkEvent2 = clientDriver.PopEventForConnection(clientConnection, out reader)) != NetworkEvent.Type.Empty)
                {
                    if (networkEvent2 != NetworkEvent.Type.Data)
                    {
                        continue;
                    }

                    Require(reader.Length == 4096, "fragmented packet length changed");

                    for (int receivedByteIndex = 0; receivedByteIndex < packet.Length; receivedByteIndex++)
                    {
                        Require(reader.ReadByte() == packet[receivedByteIndex], "fragment content changed");
                    }

                    received = true;
                }

                Thread.Sleep(1);
            }

            Require(received, "4096-byte packet was not delivered by NetRelay.SendOn");
            Debug.Log("NETWORK SNAPSHOT TRANSPORT PASSED: 4096 bytes over real UTP loopback");
        }
        finally
        {
            // Editor fixtures must dispose native resources explicitly; edit-mode destruction does not guarantee OnDestroy.
            client.Stop();
            server.Stop();
            UnityEngine.Object.DestroyImmediate(clientGo);
            UnityEngine.Object.DestroyImmediate(serverGo);
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
