using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Game.Combat;
using Game.Core;
using Game.Net;
using Game.World;
using UnityEditor;
using UnityEngine;

// Checks wire coordinates across independently rebased peers using actual loopback UDP messages.
public static class OriginNetworkChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Group Game/Check origin network coordinates")]
    public static void Run()
    {
        var runnerObject = new GameObject("OriginWireRunner");
        var combatObject = new GameObject("OriginWireCombat");
        var avatar = new GameObject("OriginWirePilot");
        var enemy = new GameObject("OriginWireEnemy");
        var definition = ScriptableObject.CreateInstance<WeaponDefinition>();
        Vector3 originalOrigin = FloatingOrigin.OriginOffset;
        var previousRunner = NetRunner.Instance;
        var previousCombat = NetCombat.Instance;
        UdpClient sender = null;
        UdpClient receiver = null;
        try
        {
            NetSession.Reset();
            var runner = runnerObject.AddComponent<NetRunner>();
            var combat = combatObject.AddComponent<NetCombat>();
            NetRunner.Instance = runner;
            NetCombat.Instance = combat;
            var health = avatar.AddComponent<Health>();
            health.ResetHealth();
            var authority = avatar.AddComponent<RemotePilotAuthority>();
            authority.ApplyLoadout(0);
            ((GameObject[])Get(combat, "avatars"))[1] = avatar;
            Set(combat, "playerWeaponDef", definition);
            sender = new UdpClient(AddressFamily.InterNetwork);
            receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            receiver.Client.ReceiveTimeout = 1500;
            Set(runner, "gameSock", sender);
            var peers = (IPEndPoint[])Get(runner, "peers");
            var source = new IPEndPoint(IPAddress.Loopback, 30201);
            peers[1] = source;
            peers[2] = (IPEndPoint)receiver.Client.LocalEndPoint;
            NetSession.Active = true;
            NetSession.InMatch = true;
            NetSession.LocalSlot = 0;
            NetSession.SetSlot(1, true);
            NetSession.SetSlot(2, true);

            // The same world point is represented in three independently rebased frames.
            SetOrigin(new Vector3(3000f, 40f, -2000f));
            byte[] pose = (byte[])Call(runner, "BuildPose", 1, new Vector3(10f, 3f, 14f), 25f, -10f, 6f, true);
            byte[] fire = (byte[])Call(runner, "BuildFire", 1, new Vector3(11f, 4f, 17f), new Vector3(0.6f, 0f, 0.8f), DamageSource.PlayerProjectile);
            Require(Close(ReadPosition(pose), new Vector3(3010f, 43f, -1986f)), "Sender pose was not encoded in global coordinates");
            Require(Close(ReadPosition(fire), new Vector3(3011f, 44f, -1983f)), "Sender fire was not encoded in global coordinates");
            SetOrigin(new Vector3(-2000f, 20f, 500f));
            NetSession.IsHost = true;
            Handle(runner, "HandlePose", pose, source);
            Require(Close(avatar.transform.position, new Vector3(5010f, 23f, -2486f)), "Host applied sender-local pose to its differently rebased world");
            byte[] relayedPose = Receive(receiver);
            Require(EqualBytes(relayedPose, pose), "Host relay double-converted the pose origin or changed its orientation");
            Handle(runner, "HandleFire", fire, source);
            byte[] relayedFire = Receive(receiver);
            Require(EqualBytes(relayedFire, fire), "Host relay double-converted fire origin/direction or rejected valid nearby fire");
            SetOrigin(new Vector3(8000f, -90f, 4000f));
            NetSession.IsHost = false;
            Handle(runner, "HandlePose", relayedPose, source);
            Require(Close(avatar.transform.position, new Vector3(-4990f, 133f, -5986f)), "Receiving peer did not decode into its own frame");
            Require(Quaternion.Angle(avatar.transform.rotation, Quaternion.Euler(0f, 25f, 0f) * Quaternion.Euler(0f, 0f, 6f)) < 0.01f,
                "Rebasing changed pilot orientation");
            CheckBossCodec();
            CheckSnapshot(runner, combat, enemy, receiver, peers);
            Debug.Log("ORIGIN NETWORK CHECKS: PASS independent client/host frames, actual UDP relay, shot admission, direction, boss beam and snapshot codecs");
        }
        finally
        {
            SetOrigin(originalOrigin);
            NetCombat.Instance = null;
            if (sender != null)
            {
                sender.Close();
            }

            if (receiver != null)
            {
                receiver.Close();
            }

            // Detach fixture avatars/enemies before NetCombat's normal scene cleanup.
            var combat = combatObject.GetComponent<NetCombat>();
            if (combat != null)
            {
                Array.Clear((GameObject[])Get(combat, "avatars"), 0, NetSession.MaxPlayers);
                ((Dictionary<int, GameObject>)Get(combat, "ghosts")).Clear();
                ((Dictionary<int, GameObject>)Get(combat, "hostEnemies")).Clear();
            }

            UnityEngine.Object.DestroyImmediate(combatObject);
            UnityEngine.Object.DestroyImmediate(runnerObject);
            UnityEngine.Object.DestroyImmediate(avatar);
            UnityEngine.Object.DestroyImmediate(enemy);
            UnityEngine.Object.DestroyImmediate(definition);
            NetRunner.Instance = previousRunner;
            NetCombat.Instance = previousCombat;
            NetSession.Reset();
        }
    }

    // Boss warning positions must change coordinate frames without rotating their beam directions.
    private static void CheckBossCodec()
    {
        var state = new BossPresentationState
        {
            Phase = 2,
            Stage = 2,
            BeamVisible = true,
            BeamFiring = true,
            BeamOrigin = new Vector3(2f, 3f, 4f),
            BeamDirection = new Vector3(0.6f, 0f, 0.8f),
            HullColor = Color.cyan
        };
        SetOrigin(new Vector3(6000f, 20f, -2000f));
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            typeof(NetCombat).GetMethod("WriteBoss", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { writer, state });
            SetOrigin(new Vector3(-3000f, 40f, 8000f));
            stream.Position = 0;
            using (var reader = new BinaryReader(stream))
            {
                var decoded = (BossPresentationState)typeof(NetCombat).GetMethod("ReadBoss", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { reader });
                Require(Close(decoded.BeamOrigin, new Vector3(9002f, -17f, -9996f)), "Boss beam origin used the sender's local frame");
                Require(decoded.BeamDirection == state.BeamDirection && decoded.BeamVisible && decoded.BeamFiring && decoded.Stage == 2,
                    "Boss codec translated a direction or changed attack state");
            }
        }
    }

    // Check enemy snapshot coordinates through the same host transport used during play.
    private static void CheckSnapshot(NetRunner runner, NetCombat combat, GameObject enemy, UdpClient receiver, IPEndPoint[] peers)
    {
        SetOrigin(new Vector3(6000f, 20f, -2000f));
        NetSession.IsHost = true;
        peers[1] = null;
        enemy.AddComponent<Health>().ResetHealth();
        enemy.transform.position = new Vector3(12f, 8f, 30f);
        ((Dictionary<int, GameObject>)Get(combat, "hostEnemies"))[7] = enemy;
        Call(combat, "SendEnemySnap");
        byte[] packet = Receive(receiver);
        using (var stream = new MemoryStream(packet))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = 12; // magic, type, round, sequence, record count.
            Require(reader.ReadInt32() == 7 && Close(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()), new Vector3(6012f, 28f, -1970f)),
                "Host snapshot did not encode enemy world coordinates");
            ((Dictionary<int, GameObject>)Get(combat, "ghosts"))[7] = enemy;
            SetOrigin(new Vector3(-3000f, 40f, 8000f));
            NetSession.IsHost = false;
            stream.Position = 10;
            combat.ApplySnapshot(reader);
            var targets = (Dictionary<int, Vector3>)Get(combat, "ghostTargetPos");
            Require(Close(targets[7], new Vector3(9012f, -12f, -9970f)), "Snapshot interpolation target did not use receiver origin");
            Call(combat, "ShiftOrigin", new Vector3(-2100f, 40f, 0f));
            Require(Close(targets[7], new Vector3(6912f, 28f, -9970f)), "Rebasing left stale ghost interpolation coordinates");
        }
    }

    private static void Handle(NetRunner runner, string method, byte[] packet, IPEndPoint source)
    {
        using (var stream = new MemoryStream(packet))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = 2;
            Call(runner, method, reader, source);
        }
    }

    private static byte[] Receive(UdpClient socket)
    {
        IPEndPoint remote = null;
        return socket.Receive(ref remote);
    }

    private static Vector3 ReadPosition(byte[] packet) => new Vector3(BitConverter.ToSingle(packet, 3), BitConverter.ToSingle(packet, 7), BitConverter.ToSingle(packet, 11));

    private static bool EqualBytes(byte[] first, byte[] second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (int byteIndex = 0; byteIndex < first.Length; byteIndex++)
        {
            if (first[byteIndex] != second[byteIndex])
            {
                return false;
            }
        }

        return true;
    }

    private static void SetOrigin(Vector3 origin) => typeof(FloatingOrigin).GetProperty("OriginOffset").GetSetMethod(true).Invoke(null, new object[] { origin });

    private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);

    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);

    private static bool Close(Vector3 actualPosition, Vector3 expectedPosition) => Vector3.Distance(actualPosition, expectedPosition) < 0.01f;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
