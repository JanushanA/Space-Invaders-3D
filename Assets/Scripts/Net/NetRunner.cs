using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Game.Combat;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Game.Net
{
    // Maintains the lobby connection across scenes and routes the shared LAN/Relay packet protocol.
    // The host also plays locally while owning enemy simulation and guest gameplay decisions.
    public partial class NetRunner : MonoBehaviour
    {
        // Every packet begins with this marker and a one-byte message type.
        // Writers, validators, and readers must agree on every following field and its byte order.
        private const byte Magic = 83;
        private const byte MsgFind = 20;
        private const byte MsgHere = 21;
        private const byte MsgHello = 1;
        private const byte MsgWelcome = 2;
        private const byte MsgLobby = 3;
        private const byte MsgSetLevel = 4;
        private const byte MsgStart = 5;
        private const byte MsgPose = 6;
        private const byte MsgSnap = 7;
        private const byte MsgFire = 8;
        private const byte MsgHealth = 9;
        private const byte MsgLeave = 10;
        private const byte MsgReject = 11;
        private const byte MsgLobbyBack = 12;
        private const byte MsgFail = 13;
        private const byte MsgHeartbeat = 14;
        private static readonly string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public static bool VisualOnlyFire;
        public static NetRunner Instance;
        public event Action LobbyChanged;
        public event Action JoinFailed;
        public event Action MatchFailed;
        private UdpClient gameSock;
        private UdpClient beaconSock;
        private Thread gameThread;
        private Thread beaconThread;
        private volatile bool running;
        private readonly Queue<Datagram> inbox = new Queue<Datagram>();
        private readonly IPEndPoint[] peers = new IPEndPoint[NetSession.MaxPlayers];
        private readonly float[] lastHeard = new float[NetSession.MaxPlayers];
        private IPEndPoint hostEnd;
        private float nextLobbySend;
        private float nextFindSend;
        private float joinUntil;
        private bool joining;
        private bool useRelay;
        private NetRelay relay;
        private bool relayHelloSent;
        private volatile int generation;
        private int matchRound;
        private uint nextSnapshotSequence;
        private uint lastSnapshotSequence;
        private float nextHeartbeat;
        private float hostLastHeard;

        // Stores a received packet until the Unity main thread can safely process it.
        private struct Datagram
        {
            public byte[] Data;
            public IPEndPoint From;
            public int Generation;
        }

        public static NetRunner Ensure()
        {
            // Creates one persistent network runner so lobby connections survive menu and combat scene changes.
            if (Instance != null)
            {
                return Instance;
            }

            GameObject sceneObject = new GameObject("NetRunner");
            DontDestroyOnLoad(sceneObject);
            Instance = sceneObject.AddComponent<NetRunner>();
            sceneObject.AddComponent<NetRelay>();
            return Instance;
        }

        public int MatchRound
        {
            get
            {
                return matchRound;
            }
        }

        public static bool HasInstance
        {
            get
            {
                return Instance != null && NetSession.Active;
            }
        }

        private void Update()
        {
            // Pumps Relay, handles queued packets, then runs host or client timing for the current session role.
            if (relay != null)
            {
                relay.Tick();
            }

            DrainInbox();
            if (!NetSession.Active)
            {
                return;
            }

            TickReliable();
            if (joining && Time.unscaledTime > joinUntil)
            {
                // Stops a join attempt after its timeout so the lobby UI can report a clear failure.
                FailJoin("NO LOBBY FOUND");
                return;
            }

            if (useRelay && !NetSession.IsHost && relay != null && relay.ClientReady && !relayHelloSent && hostEnd != null)
            {
                // Sends the normal custom hello packet only after Relay transport confirms the connection is ready.
                relayHelloSent = true;
                Send(null, BuildHello(), hostEnd);
            }

            if (NetSession.IsHost)
            {
                TickHost();
            }
            else
            {
                TickClient();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            Shutdown();
        }

        public async void CreateLobby()
        {
            // Resets any old connection, claims host slot zero, then prefers an internet Relay lobby.
            Shutdown();
            NetSession.Reset();
            NetSession.Active = true;
            NetSession.IsHost = true;
            NetSession.HoldLobby = true;
            NetSession.LocalSlot = 0;
            NetSession.PlayerCount = 1;
            NetSession.SelectedLevel = 1;
            NetSession.SetSlot(0, true);
            NetSession.Status = "CONNECTING RELAY";
            NotifyLobby();
            int operation = generation;
            bool relayOk = await StartRelayHost(operation);
            if (this == null || operation != generation)
            {
                return;
            }

            if (!NetSession.Active || !NetSession.IsHost)
            {
                return;
            }

            if (relayOk)
            {
                // Uses Unity Relay's join code when available so friends can connect outside the local network.
                NetSession.ViaRelay = true;
                NetSession.Code = relay.JoinCode;
                NetSession.Status = "WAITING FOR PILOTS";
                NotifyLobby();
                return;
            }

            NetSession.ViaRelay = false;
            NetSession.Code = MakeCode();

            // Falls back to a generated LAN code when Relay setup is unavailable.
            NetSession.Status = "LAN LOBBY    RELAY OFFLINE";
            try
            {
                BindHost();
            }
            catch (SocketException socketException)
            {
                FailJoin("LAN HOST FAILED: " + socketException.SocketErrorCode);
                return;
            }

            NotifyLobby();
        }

        public async void JoinLobby(string code)
        {
            // Normalises the typed code, then attempts Relay first before switching to local LAN discovery.
            Shutdown();
            NetSession.Reset();
            NetSession.Active = true;
            NetSession.IsHost = false;
            NetSession.HoldLobby = true;
            NetSession.Code = NormaliseCode(code);
            NetSession.Status = "SEARCHING RELAY";
            joining = true;
            joinUntil = Time.unscaledTime + 12f;
            NotifyLobby();
            int operation = generation;
            bool relayOk = await StartRelayClient(NetSession.Code, operation);
            if (this == null || operation != generation)
            {
                return;
            }

            if (!NetSession.Active || NetSession.IsHost)
            {
                return;
            }

            if (relayOk)
            {
                // Uses a fixed fake host endpoint because the existing protocol identifies Relay peers this way.
                NetSession.ViaRelay = true;
                hostEnd = new IPEndPoint(IPAddress.Parse("127.0.0.2"), 21000);
                NetSession.Status = "CONNECTING";
                NotifyLobby();
                return;
            }

            NetSession.ViaRelay = false;
            NetSession.Status = "SEARCHING LAN";
            joinUntil = Time.unscaledTime + 5f;
            try
            {
                BindClient();
            }
            catch (SocketException socketException)
            {
                FailJoin("LAN JOIN FAILED: " + socketException.SocketErrorCode);
                return;
            }

            NotifyLobby();
        }

        public void Leave()
        {
            // Tells known peers this player is leaving before clearing local sockets and session state.
            if (NetSession.Active && (gameSock != null || useRelay))
            {
                SendToKnown(BuildLeave());
            }

            Shutdown();
            NetSession.Reset();
            NotifyLobby();
        }

        public void HostSetLevel(int level)
        {
            // Lets only the host choose an already unlocked sector and broadcasts the updated lobby state.
            if (!NetSession.Active || !NetSession.IsHost || NetSession.InMatch)
            {
                return;
            }

            if (level < 1 || level > GameSession.LevelCount || !GameSession.IsUnlocked(level))
            {
                return;
            }

            NetSession.SelectedLevel = level;
            Broadcast(BuildLobby());
            NotifyLobby();
        }

        public void HostStart()
        {
            // Starts a match with one to five players, including a host-only Relay session.
            if (!NetSession.Active || !NetSession.IsHost || (!useRelay && gameSock == null))
            {
                return;
            }

            if (NetSession.PlayerCount < 1)
            {
                NetSession.Status = "NEED A HOST";
                NotifyLobby();
                return;
            }

            if (NetSession.PlayerCount > NetSession.MaxPlayers)
            {
                return;
            }

            if (pendingMessages.Count > 0)
            {
                NetSession.Status = "SYNCING PILOTS";
                NotifyLobby();
                return;
            }

            matchRound++;
            LoadMatch();
            BroadcastReliable(BuildStart());
        }

        public void ReturnEveryoneToLobby()
        {
            // Deliver earned rewards/results before unloading the guest progression components.
            if (!NetSession.Active || !NetSession.IsHost || !NetSession.InMatch)
            {
                return;
            }

            if (pendingMessages.Count > 0)
            {
                returnToLobbyWhenSynced = true;
                return;
            }

            if (NetSession.IsHost)
            {
                BroadcastReliable(BuildLobbyBack());
            }

            EnterLobbyScene();
        }

        public void BroadcastFail()
        {
            if (!NetSession.Active || !NetSession.IsHost || !NetSession.InMatch)
            {
                return;
            }

            if (NetSession.IsHost)
            {
                BroadcastReliable(BuildFail());
            }

            if (MatchFailed != null)
            {
                MatchFailed();
            }
        }

        public void NoteFire(Vector3 origin, Vector3 direction, DamageSource source, Projectile projectile = null)
        {
            // Converts a local weapon shot into a lightweight origin-and-direction packet for other players.
            if (!NetSession.Active || !NetSession.InMatch || VisualOnlyFire)
            {
                return;
            }

            int fireSlot = NetSession.LocalSlot;
            if (source == DamageSource.EnemyProjectile)
            {
                if (!NetSession.IsHost)
                {
                    return;
                }

                // Uses a special slot value for host-owned enemy projectiles because no player fired them.
                fireSlot = 255;
            }

            byte[] packet = BuildFire(fireSlot, origin, direction, source);
            if (source == DamageSource.EnemyProjectile && projectile != null)
            {
                float hostileSpeed = projectile.Velocity.magnitude;
                if (!ValidHostileProjectileSpeed(hostileSpeed))
                {
                    return;
                }

                uint projectileId = ++nextEnemyProjectileId;
                if (projectileId == 0)
                {
                    projectileId = ++nextEnemyProjectileId;
                }

                projectile.NetworkId = projectileId;
                Array.Resize(ref packet, 40);
                Array.Copy(BitConverter.GetBytes(matchRound), 0, packet, 28, 4);
                Array.Copy(BitConverter.GetBytes(projectileId), 0, packet, 32, 4);
                Array.Copy(BitConverter.GetBytes(hostileSpeed), 0, packet, 36, 4);
            }

            if (NetSession.IsHost)
            {
                Broadcast(packet);
            }
            else if (hostEnd != null)
            {
                Send(gameSock, packet, hostEnd);
            }
        }

        public void NoteHealth(int slot, float current)
        {
            if (!NetSession.IsHost || !NetSession.InMatch)
            {
                return;
            }

            SendToSlot(slot, BuildHealth(slot, current));
        }

        public void SendLocalPose(Vector3 position, float yawDegrees, float pitch, float bank, bool alive)
        {
            // Sends movement and visual aim values so remote avatars mirror the local ship correctly.
            if (!NetSession.Active || !NetSession.InMatch)
            {
                return;
            }

            byte[] packet = BuildPose(NetSession.LocalSlot, position, yawDegrees, pitch, bank, alive);
            if (NetSession.IsHost)
            {
                Broadcast(packet);
            }
            else if (hostEnd != null)
            {
                Send(gameSock, packet, hostEnd);
            }
        }

        public void SendSnapshot(byte[] payload)
        {
            if (!NetSession.IsHost)
            {
                return;
            }

            Broadcast(payload);
        }

        public static byte[] BeginSnapshot(BinaryWriter writer)
        {
            return null;
        }

        public MemoryStream OpenSnapStream()
        {
            // Starts an enemy snapshot with the common packet header before NetCombat writes its records.
            MemoryStream packetStream = new MemoryStream(2048);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgSnap);
            packetWriter.Write(matchRound);
            packetWriter.Write(++nextSnapshotSequence);
            return packetStream;
        }

        private void TickHost()
        {
            // Regularly refreshes lobby state for clients and removes peers that stopped sending packets.
            if (Time.unscaledTime >= nextLobbySend)
            {
                nextLobbySend = Time.unscaledTime + 0.35f;
                Broadcast(BuildLobby());
            }

            DropStalePeers();
        }

        private void TickClient()
        {
            if (!joining && hostEnd != null)
            {
                if (Time.unscaledTime - hostLastHeard > 12f)
                {
                    LoseHost();
                    return;
                }

                if (Time.unscaledTime >= nextHeartbeat)
                {
                    nextHeartbeat = Time.unscaledTime + 1f;
                    Send(gameSock, new byte[] { Magic, MsgHeartbeat }, hostEnd);
                }
            }

            // Sends LAN discovery only after a LAN socket has been bound.
            if (useRelay || gameSock == null)
            {
                return;
            }

            if (joining && Time.unscaledTime >= nextFindSend)
            {
                nextFindSend = Time.unscaledTime + 0.4f;
                SendFind();
            }
        }

        private async System.Threading.Tasks.Task<bool> StartRelayHost(int operation)
        {
            GetRelay();
            bool hostStarted = await relay.StartHost();
            if (this == null || operation != generation)
            {
                return false;
            }

            useRelay = hostStarted;
            running = hostStarted;
            return hostStarted;
        }

        private async System.Threading.Tasks.Task<bool> StartRelayClient(string code, int operation)
        {
            GetRelay();
            bool clientStarted = await relay.StartClient(code);
            if (this == null || operation != generation)
            {
                return false;
            }

            useRelay = clientStarted;
            running = clientStarted;
            relayHelloSent = false;
            return clientStarted;
        }

        private void GetRelay()
        {
            // Finds or adds the Relay transport bridge, then subscribes packet and disconnect callbacks once.
            if (relay == null)
            {
                relay = GetComponent<NetRelay>();
            }

            if (relay == null)
            {
                relay = gameObject.AddComponent<NetRelay>();
            }

            relay.Received -= OnRelayData;
            relay.Received += OnRelayData;
            relay.PeerDropped -= OnRelayDrop;
            relay.PeerDropped += OnRelayDrop;
        }

        private void OnRelayDrop(IPEndPoint from)
        {
            HandleLeave(from);
        }

        private void OnRelayData(byte[] data, IPEndPoint from)
        {
            // Queues Relay data under a lock because transport callbacks can occur outside Unity's main thread.
            Datagram datagram = new Datagram();
            datagram.Data = data;
            datagram.From = from;
            datagram.Generation = generation;
            lock (inbox)
            {
                if (inbox.Count < 256)
                {
                    inbox.Enqueue(datagram);
                }
            }
        }

        private void TouchPeer(IPEndPoint from)
        {
            // Updates a host-side last-heard time whenever a known client sends a packet.
            if (!NetSession.IsHost || from == null)
            {
                return;
            }

            int slot = SlotForEnd(from);
            if (slot > 0)
            {
                lastHeard[slot] = Time.unscaledTime;
            }
        }

        private void DropStalePeers()
        {
            // Frees slots whose clients have been silent for twelve seconds during an active match.
            int pilotSlot;

            for (pilotSlot = 1; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                if (!NetSession.SlotTaken(pilotSlot))
                {
                    continue;
                }

                if (Time.unscaledTime - lastHeard[pilotSlot] < 12f)
                {
                    continue;
                }

                FreeSlot(pilotSlot);
            }
        }

        private void DrainInbox()
        {
            // Removes one queued packet at a time under a lock, then handles it safely on Unity's main thread.
            while (true)
            {
                Datagram datagram;
                lock (inbox)
                {
                    if (inbox.Count == 0)
                    {
                        return;
                    }

                    datagram = inbox.Dequeue();
                }
                if (datagram.Generation == generation)
                {
                    Handle(datagram.Data, datagram.From);
                }
            }
        }

        private void Handle(byte[] data, IPEndPoint from)
        {
            // Validates the packet header, then dispatches each message type to its matching reader.
            if (!NetSession.Active || from == null || !ValidPacket(data) || !AllowedSender(data, from))
            {
                return;
            }

            TouchPeer(from);
            if (!NetSession.IsHost && EndsEqual(hostEnd, from))
            {
                hostLastHeard = Time.unscaledTime;
            }

            byte type = data[1];
            if (IsGameplayType(type))
            {
                HandleGameplayPacket(data, from);
                return;
            }

            BinaryReader packetReader = new BinaryReader(new MemoryStream(data));
            packetReader.ReadByte();
            packetReader.ReadByte();
            if (type == MsgFind)
            {
                HandleFind(packetReader, from);
                return;
            }

            if (type == MsgHere)
            {
                HandleHere(packetReader, from);
                return;
            }

            if (type == MsgHello)
            {
                HandleHello(packetReader, from);
                return;
            }

            if (type == MsgWelcome)
            {
                HandleWelcome(packetReader);
                return;
            }

            if (type == MsgLobby)
            {
                HandleLobby(packetReader);
                return;
            }

            if (type == MsgSetLevel)
            {
                return;
            }

            if (type == MsgStart)
            {
                HandleStart(packetReader);
                return;
            }

            if (type == MsgPose)
            {
                HandlePose(packetReader, from);
                return;
            }

            if (type == MsgSnap)
            {
                // Ignore old rounds and out-of-order snapshots instead of moving ghosts backward.
                int round = packetReader.ReadInt32();
                uint sequence = packetReader.ReadUInt32();
                if (round == matchRound && unchecked((int)(sequence - lastSnapshotSequence)) > 0 && NetCombat.Instance != null)
                {
                    NetCombat.Instance.ApplySnapshot(packetReader);
                    lastSnapshotSequence = sequence;
                }

                return;
            }

            if (type == MsgFire)
            {
                HandleFire(packetReader, from);
                return;
            }

            if (type == MsgHealth)
            {
                HandleHealth(packetReader);
                return;
            }

            if (type == MsgLeave)
            {
                HandleLeave(from);
                return;
            }

            if (type == MsgReject)
            {
                // Ends the join attempt immediately when the host rejects a full, bad-code, or in-match request.
                FailJoin("JOIN REJECTED");
                return;
            }

            if (type == MsgLobbyBack)
            {
                if (NetSession.InMatch)
                {
                    EnterLobbyScene();
                }

                return;
            }

            if (type == MsgFail)
            {
                if (MatchFailed != null)
                {
                    MatchFailed();
                }
            }
        }

        private bool AllowedSender(byte[] data, IPEndPoint from)
        {
            // Packet shape alone is insufficient: check the connection role and its assigned pilot slot.
            byte type = data[1];
            if (IsGameplayType(type))
            {
                return AllowedGameplaySender(data, from);
            }

            if ((type == MsgFail || type == MsgLobbyBack || type == MsgStart) && !insideReliable)
            {
                return false;
            }

            if (NetSession.IsHost)
            {
                if (type == MsgFind || type == MsgHello)
                {
                    return true;
                }

                int slot = SlotForEnd(from);
                if (slot <= 0)
                {
                    return false;
                }

                if (type == MsgLeave || type == MsgHeartbeat)
                {
                    return true;
                }

                if (!NetSession.InMatch)
                {
                    return false;
                }

                if (type == MsgPose)
                {
                    // A guest can describe only its own pilot, not another party member.
                    return data[2] == slot;
                }

                return type == MsgFire && data[2] == slot && data[27] == (byte)DamageSource.PlayerProjectile;
            }

            if (type == MsgHere)
            {
                return joining && !useRelay;
            }

            if (!EndsEqual(hostEnd, from))
            {
                return false;
            }

            if (type == MsgWelcome || type == MsgReject)
            {
                return joining;
            }

            if (type == MsgLeave)
            {
                return true;
            }

            if (type == MsgLobby)
            {
                return true;
            }

            if (joining)
            {
                return false;
            }

            if (type == MsgStart)
            {
                return true;
            }

            return NetSession.InMatch && (type == MsgPose || type == MsgSnap || type == MsgFire ||
                type == MsgHealth || type == MsgLobbyBack || type == MsgFail);
        }

        private static bool ValidHostileProjectileSpeed(float speed) => !float.IsNaN(speed) && !float.IsInfinity(speed) && speed >= 0.1f && speed <= 1000f;

        // Validate the whole packet before any handler mutates lobby or combat state.
        private static bool ValidPacket(byte[] data)
        {
            if (data == null || data.Length < 2 || data.Length > 16384 || data[0] != Magic)
            {
                return false;
            }

            byte type = data[1];
            int length = data.Length;
            if (IsGameplayType(type))
            {
                return ValidGameplayPacket(data);
            }

            if (type == MsgFind || type == MsgHello)
            {
                return length == 10;
            }

            if (type == MsgHere)
            {
                return length == 12;
            }

            if (type == MsgLeave || type == MsgReject || type == MsgLobbyBack || type == MsgFail || type == MsgHeartbeat)
            {
                return length == 2;
            }

            if (type == MsgWelcome)
            {
                return length == 5 && data[2] == 1 && data[3] > 0 && data[3] < NetSession.MaxPlayers && ValidLevel(data[4]);
            }

            if (type == MsgLobby || type == MsgStart)
            {
                if (length != (type == MsgStart ? 13 : 9) || !ValidLevel(data[2]) || data[4] != 1)
                {
                    return false;
                }

                int count = 0;

                for (int slotFlagOffset = 4; slotFlagOffset < 9; slotFlagOffset++)
                {
                    if (data[slotFlagOffset] > 1)
                    {
                        return false;
                    }

                    count += data[slotFlagOffset];
                }

                return count == data[3] && count >= 1 && count <= NetSession.MaxPlayers;
            }

            if (type == MsgHealth)
            {
                // Header, slot, current/max health floats, round number, and sequence number.
                return length == 19 && data[2] < NetSession.MaxPlayers && Finite(data, 3) && Finite(data, 7) &&
                BitConverter.ToSingle(data, 3) >= 0f && BitConverter.ToSingle(data, 7) > 0f && BitConverter.ToSingle(data, 7) <= 1000f;
            }

            if (type == MsgPose || type == MsgFire)
            {
                if (length != 28 && !(type == MsgFire && (length == 36 || length == 40)))
                {
                    return false;
                }

                for (int floatByteOffset = 3; floatByteOffset < 27; floatByteOffset += 4)
                {
                    if (!Finite(data, floatByteOffset))
                    {
                        return false;
                    }
                }

                if (type == MsgPose)
                {
                    return data[2] < NetSession.MaxPlayers && data[27] <= 1;
                }

                if (length >= 36)
                {
                    // Enemy fire extends the 28-byte base with round/ID and an optional speed float.
                    return data[2] == 255 && data[27] == (byte)DamageSource.EnemyProjectile &&
                    BitConverter.ToInt32(data, 28) >= 0 && BitConverter.ToUInt32(data, 32) != 0 &&
                    (length == 36 || ValidHostileProjectileSpeed(BitConverter.ToSingle(data, 36)));
                }

                return (data[2] < NetSession.MaxPlayers && data[27] == (byte)DamageSource.PlayerProjectile) ||
                    (data[2] == 255 && data[27] == (byte)DamageSource.EnemyProjectile);
            }

            if (type == MsgSnap)
            {
                if (length < 12)
                {
                    return false;
                }

                // Snapshot header: magic/type, round, sequence, then a two-byte enemy count.
                int count = BitConverter.ToUInt16(data, 10);
                int offset = 12;
                var identifiers = new HashSet<int>();

                for (int record = 0; record < count; record++)
                {
                    // Each 34-byte enemy record stores ID, pose, scale, life/health, and a boss marker.
                    int enemyRecordOffset = offset;
                    if (length - enemyRecordOffset < 34)
                    {
                        return false;
                    }

                    int identifier = BitConverter.ToInt32(data, enemyRecordOffset);
                    if (identifier <= 0 || !identifiers.Add(identifier) || data[enemyRecordOffset + 24] > 1 || data[enemyRecordOffset + 33] > 1)
                    {
                        return false;
                    }

                    for (int enemyFloatOffset = 4; enemyFloatOffset <= 20; enemyFloatOffset += 4)
                    {
                        if (!Finite(data, enemyRecordOffset + enemyFloatOffset))
                        {
                            return false;
                        }
                    }

                    if (!Finite(data, enemyRecordOffset + 25) || !Finite(data, enemyRecordOffset + 29) || BitConverter.ToSingle(data, enemyRecordOffset + 20) <= 0f ||
                        BitConverter.ToSingle(data, enemyRecordOffset + 25) < 0f || BitConverter.ToSingle(data, enemyRecordOffset + 29) <= 0f)
                    {
                        return false;
                    }

                    offset += 34;
                    if (data[enemyRecordOffset + 33] == 1)
                    {
                        // Boss presentation adds phase, stage, flags, beam vectors, and color: 43 bytes.
                        if (length - offset < 43 || data[offset] > 11 || data[offset + 1] < 1 || data[offset + 1] > 2 || data[offset + 2] > 31)
                        {
                            return false;
                        }

                        for (int bossFloatOffset = 3; bossFloatOffset < 43; bossFloatOffset += 4)
                        {
                            if (!Finite(data, offset + bossFloatOffset))
                            {
                                return false;
                            }
                        }

                        offset += 43;
                    }
                }

                if (offset != length)
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool ValidLevel(int level)
        {
            return level >= 1 && level <= GameSession.LevelCount;
        }

        private static bool Finite(byte[] data, int offset)
        {
            float value = BitConverter.ToSingle(data, offset);
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void FailJoin(string status)
        {
            Shutdown();
            NetSession.Reset();
            NetSession.Status = status;
            NotifyLobby();
            if (JoinFailed != null)
            {
                JoinFailed();
            }
        }

        private void LoseHost()
        {
            bool wasInMatch = NetSession.InMatch;
            Shutdown();
            NetSession.Reset();
            NetSession.Status = "HOST DISCONNECTED";
            NotifyLobby();
            if (JoinFailed != null)
            {
                JoinFailed();
            }

            if (wasInMatch)
            {
                Time.timeScale = 1f;
                GameSession.ShowMpLobby = false;
                GameSession.EnteredFromMenu = true;
                SceneManager.LoadSceneAsync(GameSession.MenuScene);
            }
        }

        private void HandleFind(BinaryReader packetReader, IPEndPoint from)
        {
            // Responds only when this host is in a lobby and the LAN discovery code matches exactly.
            if (!NetSession.Active || !NetSession.IsHost || NetSession.InMatch)
            {
                return;
            }

            string code = ReadCode(packetReader);
            if (code != NetSession.Code)
            {
                return;
            }

            Send(beaconSock, BuildHere(), from);
        }

        private void HandleHere(BinaryReader packetReader, IPEndPoint from)
        {
            // Records the LAN host address supplied by discovery, then sends the normal lobby hello packet.
            if (NetSession.IsHost || !joining)
            {
                return;
            }

            string code = ReadCode(packetReader);
            if (code != NetSession.Code)
            {
                return;
            }

            ushort port = packetReader.ReadUInt16();
            if (port == 0 || useRelay || (hostEnd != null && !hostEnd.Address.Equals(from.Address)))
            {
                return;
            }

            hostEnd = new IPEndPoint(from.Address, port);
            Send(gameSock, BuildHello(), hostEnd);
        }

        private void HandleHello(BinaryReader packetReader, IPEndPoint from)
        {
            // Validates a joining client, reuses an existing slot when possible, or assigns the first free slot.
            if (!NetSession.IsHost)
            {
                return;
            }

            string code = ReadCode(packetReader);
            if (code != NetSession.Code)
            {
                Send(gameSock, BuildReject(), from);
                return;
            }

            if (NetSession.InMatch)
            {
                Send(gameSock, BuildReject(), from);
                return;
            }

            int existing = SlotForEnd(from);
            if (existing >= 0)
            {
                // Re-sends welcome for a repeated hello so reconnecting packets do not create duplicate players.
                Send(gameSock, BuildWelcome(existing), from);
                return;
            }

            int slot = FirstFreeSlot();
            if (slot < 0)
            {
                Send(gameSock, BuildReject(), from);
                return;
            }

            peers[slot] = from;

            // Records endpoint ownership before broadcasting so following pose packets can be matched to a slot.
            lastHeard[slot] = Time.unscaledTime;
            NetSession.SetSlot(slot, true);
            Recount();
            Send(gameSock, BuildWelcome(slot), from);
            Broadcast(BuildLobby());
            NotifyLobby();
        }

        private void HandleWelcome(BinaryReader packetReader)
        {
            // Reads host approval, assigned slot and selected level to finish a client lobby join.
            int acceptedFlag = packetReader.ReadByte();
            if (acceptedFlag == 0)
            {
                FailJoin("JOIN REJECTED");
                return;
            }

            hostLastHeard = Time.unscaledTime;
            NetSession.LocalSlot = packetReader.ReadByte();
            NetSession.SelectedLevel = packetReader.ReadByte();
            joining = false;
            NetSession.Status = "IN LOBBY";
            NotifyLobby();
        }

        private void HandleLobby(BinaryReader packetReader)
        {
            // Rebuilds client lobby occupancy from the host's complete authoritative lobby packet.
            int level = packetReader.ReadByte();
            if (!NetSession.InMatch)
            {
                NetSession.SelectedLevel = level;
            }

            NetSession.PlayerCount = packetReader.ReadByte();
            NetSession.ClearSlots();
            int pilotSlot;

            for (pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                bool taken = packetReader.ReadByte() == 1;
                NetSession.SetSlot(pilotSlot, taken);
                if (!taken && NetCombat.Instance != null)
                {
                    NetCombat.Instance.RemovePilot(pilotSlot);
                }
            }

            NotifyLobby();
        }

        private void HandleStart(BinaryReader packetReader)
        {
            // Copies the host's selected level and player slots before loading the same combat scene locally.
            if (NetSession.IsHost)
            {
                return;
            }

            long payload = packetReader.BaseStream.Position;
            packetReader.BaseStream.Position = packetReader.BaseStream.Length - 4;
            int round = packetReader.ReadInt32();
            if (round <= matchRound)
            {
                return;
            }

            matchRound = round;
            packetReader.BaseStream.Position = payload;
            NetSession.SelectedLevel = packetReader.ReadByte();
            NetSession.PlayerCount = packetReader.ReadByte();
            NetSession.ClearSlots();
            int pilotSlot;

            for (pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                bool taken = packetReader.ReadByte() == 1;
                NetSession.SetSlot(pilotSlot, taken);
            }

            LoadMatch();
        }

        private void HandlePose(BinaryReader packetReader, IPEndPoint from)
        {
            // Reads one pilot pose, updates its avatar, then forwards it to every other client when hosting.
            int slot = packetReader.ReadByte();
            Vector3 position = Game.World.FloatingOrigin.ToLocalPosition(ReadVec3(packetReader));
            float yawDegrees = packetReader.ReadSingle();
            float pitch = packetReader.ReadSingle();
            float bank = packetReader.ReadSingle();
            bool alive = packetReader.ReadByte() == 1;
            if (NetCombat.Instance != null)
            {
                NetCombat.Instance.ApplyPose(slot, position, yawDegrees, pitch, bank, alive);
                if (NetSession.IsHost)
                {
                    alive = NetCombat.Instance.PilotAlive(slot);
                }
            }

            if (NetSession.IsHost)
            {
                BroadcastExcept(BuildPose(slot, position, yawDegrees, pitch, bank, alive), from);
            }
        }

        private void HandleFire(BinaryReader packetReader, IPEndPoint from)
        {
            // Recreates a remote shot and lets the host relay it to every peer except the original sender.
            int slot = packetReader.ReadByte();
            Vector3 origin = Game.World.FloatingOrigin.ToLocalPosition(ReadVec3(packetReader));
            Vector3 direction = ReadVec3(packetReader);
            DamageSource source = (DamageSource)packetReader.ReadByte();
            uint projectileId = 0;
            float? hostileSpeed = null;
            if (packetReader.BaseStream.Length - packetReader.BaseStream.Position >= 8)
            {
                int round = packetReader.ReadInt32();
                projectileId = packetReader.ReadUInt32();
                if (round != matchRound)
                {
                    return;
                }

                if (packetReader.BaseStream.Length - packetReader.BaseStream.Position == 4)
                {
                    hostileSpeed = packetReader.ReadSingle();
                }
            }

            if (NetSession.IsHost && (NetCombat.Instance == null || !NetCombat.Instance.AdmitShot(slot, origin, direction)))
            {
                return;
            }

            if (NetCombat.Instance != null)
            {
                if (projectileId != 0)
                {
                    NetCombat.Instance.SpawnIdentifiedEnemyFire(origin, direction, projectileId, hostileSpeed);
                }
                else
                {
                    NetCombat.Instance.SpawnNetFire(slot, origin, direction, source);
                }
            }

            if (NetSession.IsHost)
            {
                BroadcastExcept(BuildFire(slot, origin, direction, source), from);
            }
        }

        private void HandleHealth(BinaryReader packetReader)
        {
            // Applies a host health update only when this packet belongs to the local player slot.
            int slot = packetReader.ReadByte();
            float current = packetReader.ReadSingle();
            float maximum = packetReader.ReadSingle();
            int round = packetReader.ReadInt32();
            uint sequence = packetReader.ReadUInt32();

            // Health snapshots replace state; delayed packets must not undo a newer damage/heal result.
            if (round != matchRound || unchecked((int)(sequence - receivedHealthSequence)) <= 0)
            {
                return;
            }

            if (slot != NetSession.LocalSlot)
            {
                return;
            }

            if (NetCombat.Instance != null)
            {
                NetCombat.Instance.ApplyRemoteHealth(current, maximum);
                receivedHealthSequence = sequence;
            }
        }

        private void HandleLeave(IPEndPoint from)
        {
            if (!NetSession.IsHost)
            {
                if (EndsEqual(hostEnd, from))
                {
                    LoseHost();
                }

                return;
            }

            int slot = SlotForEnd(from);
            if (slot > 0)
            {
                FreeSlot(slot);
            }
        }

        private void FreeSlot(int slot)
        {
            // Clears endpoint and occupancy, then broadcasts the resulting party size to all remaining peers.
            peers[slot] = null;
            NetSession.SetSlot(slot, false);
            if (NetCombat.Instance != null)
            {
                NetCombat.Instance.RemovePilot(slot);
            }

            Recount();
            Broadcast(BuildLobby());
            NotifyLobby();
        }

        private int FirstFreeSlot()
        {
            // Searches guest slots only because the host permanently owns slot zero.
            int pilotSlot;

            for (pilotSlot = 1; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                if (!NetSession.SlotTaken(pilotSlot))
                {
                    return pilotSlot;
                }
            }

            return -1;
        }

        private int SlotForEnd(IPEndPoint from)
        {
            // Matches a packet endpoint to the slot recorded during the successful hello handshake.
            int pilotSlot;

            for (pilotSlot = 1; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                if (peers[pilotSlot] != null && EndsEqual(peers[pilotSlot], from))
                {
                    return pilotSlot;
                }
            }

            return -1;
        }

        private void Recount()
        {
            // Recalculates player count from occupied flags so joins and leaves cannot drift the lobby total.
            int connectedPilotCount = 0;
            int pilotSlot;

            for (pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                if (NetSession.SlotTaken(pilotSlot))
                {
                    connectedPilotCount++;
                }
            }

            NetSession.PlayerCount = connectedPilotCount;
        }

        private void LoadMatch()
        {
            // Stores selected lobby level in shared game state, marks match mode, and loads combat asynchronously.
            nextSnapshotSequence = lastSnapshotSequence = 0;
            ResetGameplayNetwork();
            Time.timeScale = 1f;
            NetSession.InMatch = true;
            NetSession.HoldLobby = true;
            GameSession.SelectedLevel = NetSession.SelectedLevel;
            GameSession.EnteredFromMenu = true;
            MarkPeersHeard();
            SceneManager.LoadSceneAsync(GameSession.CombatScene);
        }

        private void EnterLobbyScene()
        {
            // Clears match mode while preserving the lobby connection, then returns to the multiplayer menu screen.
            NetSession.InMatch = false;
            NetSession.HoldLobby = true;
            GameSession.ShowMpLobby = true;
            GameSession.EnteredFromMenu = true;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(GameSession.MenuScene);
        }

        private void MarkPeersHeard()
        {
            // Gives every peer a fresh timeout timestamp when a new match begins.
            float currentTime = Time.unscaledTime;
            int peerSlot;

            for (peerSlot = 0; peerSlot < lastHeard.Length; peerSlot++)
            {
                lastHeard[peerSlot] = currentTime;
            }
        }

        private void BindHost()
        {
            // Opens separate game and discovery sockets, then runs their blocking receive loops on background threads.
            running = true;
            gameSock = NewSock(NetSession.GamePort);
            beaconSock = NewSock(NetSession.BeaconPort);
            beaconSock.EnableBroadcast = true;
            UdpClient game = gameSock;
            int operation = generation;
            gameThread = new Thread(() => RecvLoop(game, operation));
            gameThread.IsBackground = true;
            gameThread.Start();
            UdpClient beacon = beaconSock;
            beaconThread = new Thread(() => RecvLoop(beacon, operation));
            beaconThread.IsBackground = true;
            beaconThread.Start();
        }

        private void BindClient()
        {
            // Opens an automatic local port and shares it for both game packets and LAN discovery replies.
            running = true;
            gameSock = NewSock(0);
            gameSock.EnableBroadcast = true;
            beaconSock = gameSock;
            UdpClient game = gameSock;
            int operation = generation;
            gameThread = new Thread(() => RecvLoop(game, operation));
            gameThread.IsBackground = true;
            gameThread.Start();
        }

        private static UdpClient NewSock(int port)
        {
            // Sets a short receive timeout so a background loop can regularly check whether shutdown was requested.
            UdpClient sock = new UdpClient(port);
            sock.Client.ReceiveTimeout = 500;
            return sock;
        }

        private void RecvLoop(UdpClient sock, int operation)
        {
            // A generation change retires old socket workers after shutdown or reconnection.
            // Queue bytes only here; Unity scene objects are handled later on the main thread.
            while (running && operation == generation && sock != null)
            {
                try
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = sock.Receive(ref from);
                    lock (inbox)
                    {
                        if (operation == generation && inbox.Count < 256)
                        {
                            inbox.Enqueue(new Datagram { Data = data, From = from, Generation = operation });
                        }
                    }
                }
                catch (SocketException)
                {
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void SendFind()
        {
            // Broadcasts the LAN code and also sends locally so another build on the same computer can be found.
            byte[] packet = BuildFind();
            try
            {
                gameSock.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, NetSession.BeaconPort));
                gameSock.Send(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, NetSession.BeaconPort));
            }
            catch (SocketException)
            {
            }
        }

        private void Broadcast(byte[] packet)
        {
            // Routes through Relay when active, otherwise sends the same packet to every known LAN guest.
            if (useRelay && relay != null)
            {
                relay.Broadcast(packet);
                return;
            }

            int pilotSlot;

            for (pilotSlot = 1; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                if (peers[pilotSlot] != null)
                {
                    Send(gameSock, packet, peers[pilotSlot]);
                }
            }
        }

        private void BroadcastExcept(byte[] packet, IPEndPoint skip)
        {
            // Avoids echoing a client packet straight back to its sender while still updating other peers.
            if (useRelay && relay != null)
            {
                relay.BroadcastExcept(packet, skip);
                return;
            }

            int pilotSlot;

            for (pilotSlot = 1; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                if (peers[pilotSlot] != null && !EndsEqual(peers[pilotSlot], skip))
                {
                    Send(gameSock, packet, peers[pilotSlot]);
                }
            }
        }

        private void SendToKnown(byte[] packet)
        {
            // Lets either role send a leave or similar packet without the caller needing to know host/client routing.
            if (NetSession.IsHost)
            {
                Broadcast(packet);
            }
            else if (hostEnd != null)
            {
                Send(gameSock, packet, hostEnd);
            }
        }

        private void SendToSlot(int slot, byte[] packet)
        {
            // Sends host-only private messages, such as health updates, to one validated guest slot.
            if (slot <= 0 || slot >= NetSession.MaxPlayers)
            {
                return;
            }

            if (peers[slot] != null)
            {
                Send(gameSock, packet, peers[slot]);
            }
        }

        private static void Send(UdpClient sock, byte[] packet, IPEndPoint recipient)
        {
            // Switches to Relay transport when needed; otherwise performs a normal LAN UDP send.
            if (Instance != null && Instance.useRelay && Instance.relay != null)
            {
                Instance.relay.SendToEnd(recipient, packet);
                return;
            }

            if (sock == null || packet == null || recipient == null)
            {
                return;
            }

            try
            {
                sock.Send(packet, packet.Length, recipient);
            }
            catch (SocketException)
            {
            }
        }

        private void Shutdown()
        {
            // Stops threads, detaches Relay callbacks, closes sockets and clears peer endpoints for a clean restart.
            generation++;
            running = false;
            joining = false;
            hostEnd = null;
            matchRound = 0;
            nextSnapshotSequence = lastSnapshotSequence = 0;
            ResetGameplayNetwork();
            nextFindSend = nextLobbySend = nextHeartbeat = 0f;
            lock (inbox)
                inbox.Clear();
            useRelay = false;
            relayHelloSent = false;
            if (relay != null)
            {
                relay.Received -= OnRelayData;
                relay.PeerDropped -= OnRelayDrop;
                relay.Stop();
            }

            UdpClient game = gameSock;
            UdpClient beacon = beaconSock;
            gameSock = null;
            beaconSock = null;
            if (game != null)
            {
                game.Close();
            }

            if (beacon != null && beacon != game)
            {
                beacon.Close();
            }

            int peerSlot;

            for (peerSlot = 0; peerSlot < peers.Length; peerSlot++)
            {
                peers[peerSlot] = null;
            }
        }

        private void NotifyLobby()
        {
            // Raises one event so menu UI can repaint after any join, leave, level, or status change.
            if (LobbyChanged != null)
            {
                LobbyChanged();
            }
        }

        private static bool EndsEqual(IPEndPoint firstEndpoint, IPEndPoint secondEndpoint)
        {
            // Requires both address and port to match because one IP address can host multiple connections.
            if (firstEndpoint == null || secondEndpoint == null)
            {
                return false;
            }

            return firstEndpoint.Address.Equals(secondEndpoint.Address) && firstEndpoint.Port == secondEndpoint.Port;
        }

        private static string MakeCode()
        {
            // Generates a short readable LAN code from an alphabet that avoids confusing characters.
            char[] chars = new char[4];
            int characterIndex;

            for (characterIndex = 0; characterIndex < 4; characterIndex++)
            {
                chars[characterIndex] = Alphabet[UnityEngine.Random.Range(0, Alphabet.Length)];
            }

            return new string(chars);
        }

        private static string NormaliseCode(string code)
        {
            // Removes spaces and punctuation and converts to uppercase so players can type codes flexibly.
            if (code == null)
            {
                return "";
            }

            char[] chars = new char[code.Length];
            int validCharacterCount = 0;
            int characterIndex;

            for (characterIndex = 0; characterIndex < code.Length; characterIndex++)
            {
                char codeCharacter = char.ToUpperInvariant(code[characterIndex]);
                if ((codeCharacter >= 'A' && codeCharacter <= 'Z') || (codeCharacter >= '0' && codeCharacter <= '9'))
                {
                    chars[validCharacterCount] = codeCharacter;
                    validCharacterCount++;
                }
            }

            return new string(chars, 0, validCharacterCount);
        }

        private static string ReadCode(BinaryReader packetReader)
        {
            // Reads the fixed eight-byte code field and trims unused trailing spaces or null bytes.
            char[] chars = new char[8];
            int last = 0;
            int characterIndex;

            for (characterIndex = 0; characterIndex < 8; characterIndex++)
            {
                chars[characterIndex] = (char)packetReader.ReadByte();
                if (chars[characterIndex] != ' ' && chars[characterIndex] != '\0')
                {
                    last = characterIndex + 1;
                }
            }

            return new string(chars, 0, last);
        }

        private static void WriteCode(BinaryWriter packetWriter, string code)
        {
            // Writes a fixed-width code field so every packet reader knows exactly where following data begins.
            int characterIndex;

            for (characterIndex = 0; characterIndex < 8; characterIndex++)
            {
                char codeCharacter = ' ';
                if (code != null && characterIndex < code.Length)
                {
                    codeCharacter = code[characterIndex];
                }

                packetWriter.Write((byte)codeCharacter);
            }
        }

        private static Vector3 ReadVec3(BinaryReader packetReader)
        {
            // Reads vector components in the same X, Y, Z order used by the matching packet writer.
            return new Vector3(packetReader.ReadSingle(), packetReader.ReadSingle(), packetReader.ReadSingle());
        }

        private static void WriteVec3(BinaryWriter packetWriter, Vector3 vector)
        {
            // Writes three floats rather than serialising a Unity object, keeping the packet simple and portable.
            packetWriter.Write(vector.x);
            packetWriter.Write(vector.y);
            packetWriter.Write(vector.z);
        }

        private byte[] BuildFind()
        {
            // Creates the LAN discovery request containing only the shared code.
            MemoryStream packetStream = new MemoryStream(8);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgFind);
            WriteCode(packetWriter, NetSession.Code);
            return packetStream.ToArray();
        }

        private byte[] BuildHere()
        {
            // Replies to discovery with the code and host game port a client should connect to.
            MemoryStream packetStream = new MemoryStream(16);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgHere);
            WriteCode(packetWriter, NetSession.Code);
            packetWriter.Write((ushort)NetSession.GamePort);
            return packetStream.ToArray();
        }

        private byte[] BuildHello()
        {
            // Builds the first join handshake packet used to prove a client knows the lobby code.
            MemoryStream packetStream = new MemoryStream(8);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgHello);
            WriteCode(packetWriter, NetSession.Code);
            return packetStream.ToArray();
        }

        private byte[] BuildWelcome(int slot)
        {
            // Confirms a join and assigns the client slot and host-selected level.
            MemoryStream packetStream = new MemoryStream(8);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgWelcome);
            packetWriter.Write((byte)1);
            packetWriter.Write((byte)slot);
            packetWriter.Write((byte)NetSession.SelectedLevel);
            return packetStream.ToArray();
        }

        private byte[] BuildReject()
        {
            // Creates the smallest possible rejection packet for bad codes, full lobbies or started matches.
            MemoryStream packetStream = new MemoryStream(4);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgReject);
            return packetStream.ToArray();
        }

        private byte[] BuildLobby()
        {
            // Serialises complete lobby state so clients can redraw the selected level and occupied pilot slots.
            MemoryStream packetStream = new MemoryStream(16);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgLobby);
            packetWriter.Write((byte)NetSession.SelectedLevel);
            packetWriter.Write((byte)NetSession.PlayerCount);
            int pilotSlot;

            for (pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                // Writes one byte per slot because booleans need a predictable compact network representation.
                byte taken = 0;
                if (NetSession.SlotTaken(pilotSlot))
                {
                    taken = 1;
                }

                packetWriter.Write(taken);
            }

            return packetStream.ToArray();
        }

        private byte[] BuildStart()
        {
            // Serialises the final level and party layout all clients must use when loading combat.
            MemoryStream packetStream = new MemoryStream(16);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgStart);
            packetWriter.Write((byte)NetSession.SelectedLevel);
            packetWriter.Write((byte)NetSession.PlayerCount);
            int pilotSlot;

            for (pilotSlot = 0; pilotSlot < NetSession.MaxPlayers; pilotSlot++)
            {
                byte taken = 0;
                if (NetSession.SlotTaken(pilotSlot))
                {
                    taken = 1;
                }

                packetWriter.Write(taken);
            }

            packetWriter.Write(matchRound);
            return packetStream.ToArray();
        }

        private byte[] BuildPose(int slot, Vector3 position, float yawDegrees, float pitch, float bank, bool alive)
        {
            // Packages only the transform and life values remote clients need to display a pilot avatar.
            MemoryStream packetStream = new MemoryStream(32);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgPose);
            packetWriter.Write((byte)slot);
            WriteVec3(packetWriter, Game.World.FloatingOrigin.ToGlobalPosition(position));
            packetWriter.Write(yawDegrees);
            packetWriter.Write(pitch);
            packetWriter.Write(bank);
            byte live = 0;
            if (alive)
            {
                live = 1;
            }

            packetWriter.Write(live);
            return packetStream.ToArray();
        }

        private byte[] BuildFire(int slot, Vector3 origin, Vector3 direction, DamageSource source)
        {
            // Packages a projectile event as origin, direction and source instead of networking the full GameObject.
            MemoryStream packetStream = new MemoryStream(32);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgFire);
            packetWriter.Write((byte)slot);
            WriteVec3(packetWriter, Game.World.FloatingOrigin.ToGlobalPosition(origin));
            WriteVec3(packetWriter, direction);
            packetWriter.Write((byte)source);
            return packetStream.ToArray();
        }

        private byte[] BuildHealth(int slot, float current)
        {
            // Creates a small host-to-owner health packet for local player health bars in multiplayer.
            MemoryStream packetStream = new MemoryStream(16);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgHealth);
            packetWriter.Write((byte)slot);
            packetWriter.Write(current);
            packetWriter.Write(NetCombat.Instance != null ? NetCombat.Instance.PilotMaximum(slot) : 3f);
            packetWriter.Write(matchRound);
            packetWriter.Write(++healthSequence);
            return packetStream.ToArray();
        }

        private byte[] BuildLeave()
        {
            // Creates a short notification allowing the host to free a client slot immediately.
            MemoryStream packetStream = new MemoryStream(4);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgLeave);
            return packetStream.ToArray();
        }

        private byte[] BuildLobbyBack()
        {
            // Creates the host command that returns every player from combat to the lobby menu.
            MemoryStream packetStream = new MemoryStream(4);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgLobbyBack);
            return packetStream.ToArray();
        }

        private byte[] BuildFail()
        {
            // Creates the host party-wipe signal that opens the shared multiplayer death screen.
            MemoryStream packetStream = new MemoryStream(4);
            BinaryWriter packetWriter = new BinaryWriter(packetStream);
            packetWriter.Write(Magic);
            packetWriter.Write(MsgFail);
            return packetStream.ToArray();
        }
    }
}
