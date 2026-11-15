using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
namespace Game.Net
{
    // Carries the existing lobby/combat packet bytes through Unity Relay for internet multiplayer.
    // Synthetic local endpoints let NetRunner use the same peer lookup as its LAN transport.
    public class NetRelay : MonoBehaviour
    {
        public bool Active;
        public bool IsHost;
        public bool ClientReady;
        public string JoinCode = "";
        public event Action ClientConnected;
        public event Action<byte[], IPEndPoint> Received;
        public event Action<IPEndPoint> PeerDropped;
        private NetworkDriver driver;
        private NetworkPipeline snapshotPipeline;
        private NetworkConnection clientToHost;
        private readonly NetworkConnection[] clients = new NetworkConnection[8];
        private readonly IPEndPoint[] clientEnds = new IPEndPoint[8];
        private int clientCount;
        private bool bound;
        private int generation;

        public async Task<bool> StartHost()
        {
            // Signs in anonymously, allocates Relay capacity, then starts a transport listener for the host.
            Stop();
            int operation = generation;
            if (!await SignIn() || this == null || operation != generation)
            {
                return false;
            }

            try
            {
                Allocation alloc = await RelayService.Instance.CreateAllocationAsync(NetSession.MaxPlayers - 1);
                if (this == null || operation != generation)
                {
                    return false;
                }

                // Requests one Relay connection for each possible guest because the host occupies slot zero.
                string code = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
                if (this == null || operation != generation)
                {
                    return false;
                }

                JoinCode = code;
                if (JoinCode != null)
                {
                    JoinCode = JoinCode.ToUpperInvariant();
                }

                RelayServerData relayData = HostRelayData(alloc);

                // Converts Unity Services allocation details into data understood by Unity Transport.
                NetworkSettings settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayData);
                ConfigureDriver(settings);
                if (driver.Bind(NetworkEndPoint.AnyIpv4) != 0)
                {
                    Debug.LogWarning("[NetRelay] Host bind failed.");
                    Stop();
                    return false;
                }

                if (driver.Listen() != 0)
                {
                    Debug.LogWarning("[NetRelay] Host listen failed.");
                    Stop();
                    return false;
                }

                bound = true;
                Active = true;
                IsHost = true;
                return true;
            }
            catch (Exception exception)
            {
                if (this == null || operation != generation)
                {
                    return false;
                }

                Debug.LogWarning("[NetRelay] Host failed: " + exception.Message);
                Stop();
                return false;
            }
        }

        public async Task<bool> StartClient(string code)
        {
            // Uses the shared join code to request the host's Relay allocation before opening a connection.
            Stop();
            int operation = generation;
            if (!await SignIn() || this == null || operation != generation)
            {
                return false;
            }

            try
            {
                JoinAllocation alloc = await RelayService.Instance.JoinAllocationAsync(code);
                if (this == null || operation != generation)
                {
                    return false;
                }

                RelayServerData relayData = ClientRelayData(alloc);
                NetworkSettings settings = new NetworkSettings();
                settings.WithRelayParameters(ref relayData);
                ConfigureDriver(settings);
                if (driver.Bind(NetworkEndPoint.AnyIpv4) != 0)
                {
                    Debug.LogWarning("[NetRelay] Client bind failed.");
                    Stop();
                    return false;
                }

                clientToHost = driver.Connect(relayData.Endpoint);
                bound = true;
                Active = true;
                IsHost = false;
                ClientReady = false;
                return true;
            }
            catch (Exception exception)
            {
                if (this == null || operation != generation)
                {
                    return false;
                }

                Debug.LogWarning("[NetRelay] Client failed: " + exception.Message);
                Stop();
                return false;
            }
        }

        public void Tick()
        {
            // Completes Unity Transport's scheduled update, then reads events for the current network role.
            if (!Active || !driver.IsCreated || !bound)
            {
                return;
            }

            driver.ScheduleUpdate().Complete();
            if (IsHost)
            {
                TickHost();
            }
            else
            {
                TickClient();
            }
        }

        public void SendToEnd(IPEndPoint recipient, byte[] packet)
        {
            // Routes a packet to a specific fake endpoint because Relay connections do not expose LAN addresses.
            if (!Active || packet == null || recipient == null)
            {
                return;
            }

            if (!IsHost)
            {
                SendOn(clientToHost, packet);
                return;
            }

            int clientIndex;

            for (clientIndex = 0; clientIndex < clientCount; clientIndex++)
            {
                if (clientEnds[clientIndex] != null && EndsEqual(clientEnds[clientIndex], recipient))
                {
                    SendOn(clients[clientIndex], packet);
                    return;
                }
            }
        }

        public void SendToHost(byte[] packet)
        {
            SendOn(clientToHost, packet);
        }

        public void Broadcast(byte[] packet)
        {
            // Sends host packets to every connected client, while a client routes through its host connection.
            if (!IsHost)
            {
                SendToHost(packet);
                return;
            }

            int clientIndex;

            for (clientIndex = 0; clientIndex < clientCount; clientIndex++)
            {
                SendOn(clients[clientIndex], packet);
            }
        }

        public void BroadcastExcept(byte[] packet, IPEndPoint skip)
        {
            // Relays a host packet to all peers except the client that originally sent it.
            if (!IsHost)
            {
                SendToHost(packet);
                return;
            }

            int clientIndex;

            for (clientIndex = 0; clientIndex < clientCount; clientIndex++)
            {
                if (skip != null && clientEnds[clientIndex] != null && EndsEqual(clientEnds[clientIndex], skip))
                {
                    continue;
                }

                SendOn(clients[clientIndex], packet);
            }
        }

        public void Stop()
        {
            // Clears session fields and disposes the driver so a new lobby starts with no old connections.
            generation++;
            Active = false;
            IsHost = false;
            ClientReady = false;
            bound = false;
            JoinCode = "";
            clientCount = 0;
            clientToHost = default(NetworkConnection);
            int clientIndex;

            for (clientIndex = 0; clientIndex < clients.Length; clientIndex++)
            {
                clients[clientIndex] = default(NetworkConnection);
                clientEnds[clientIndex] = null;
            }

            if (driver.IsCreated)
            {
                driver.Dispose();
            }
        }

        private void TickHost()
        {
            // Accepts new Relay transport connections, then pumps packet events from every connected client.
            while (true)
            {
                NetworkConnection incoming = driver.Accept();
                if (!incoming.IsCreated)
                {
                    break;
                }

                int free = 0;

                while (free < clientCount && clients[free].IsCreated)
                {
                    free++;
                }

                if (free == clients.Length)
                {
                    driver.Disconnect(incoming);
                    continue;
                }

                clients[free] = incoming;
                clientEnds[free] = FakeEnd(free);
                if (free == clientCount)
                {
                    clientCount++;
                }

                Debug.Log("[NetRelay] Accepted relay client.");
            }

            int clientIndex;

            for (clientIndex = 0; clientIndex < clientCount; clientIndex++)
            {
                PumpConnection(clients[clientIndex], clientEnds[clientIndex]);
            }
        }

        private void TickClient()
        {
            // Reads transport events from the single host connection on a joining player's machine.
            if (!clientToHost.IsCreated)
            {
                return;
            }

            NetworkEvent.Type type;
            DataStreamReader stream;

            while ((type = driver.PopEventForConnection(clientToHost, out stream)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Connect)
                {
                    // Marks the client ready only after the Relay transport confirms the host connection.
                    ClientReady = true;
                    if (ClientConnected != null)
                    {
                        ClientConnected();
                    }
                }
                else if (type == NetworkEvent.Type.Data)
                {
                    // Passes raw packet bytes into the existing custom protocol layer.
                    RaiseReceived(stream, FakeEnd(0));
                }
                else if (type == NetworkEvent.Type.Disconnect)
                {
                    ClientReady = false;
                    clientToHost = default(NetworkConnection);
                    if (PeerDropped != null)
                    {
                        PeerDropped(FakeEnd(0));
                    }

                    return; // The callback may dispose this driver.
                }
            }
        }

        private void PumpConnection(NetworkConnection conn, IPEndPoint from)
        {
            // Reads every waiting event for one client connection on the host.
            if (!conn.IsCreated)
            {
                return;
            }

            NetworkEvent.Type type;
            DataStreamReader stream;

            while ((type = driver.PopEventForConnection(conn, out stream)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Data)
                {
                    RaiseReceived(stream, from);
                }
                else if (type == NetworkEvent.Type.Disconnect)
                {
                    // Removes the disconnected connection and tells the lobby layer which fake endpoint left.
                    int clientIndex;

                    for (clientIndex = 0; clientIndex < clientCount; clientIndex++)
                    {
                        if (clients[clientIndex] == conn)
                        {
                            clients[clientIndex] = default(NetworkConnection);
                        }
                    }

                    if (PeerDropped != null)
                    {
                        PeerDropped(from);
                    }
                }
            }
        }

        private void RaiseReceived(DataStreamReader stream, IPEndPoint from)
        {
            // Copies the transport reader into a normal byte array so higher-level packet code can parse it safely.
            int packetLength = stream.Length;
            if (packetLength <= 0 || packetLength > 16384)
            {
                // Rejects empty or unusually large packets before allocating memory for them.
                return;
            }

            byte[] data = new byte[packetLength];
            int byteIndex;

            for (byteIndex = 0; byteIndex < packetLength; byteIndex++)
            {
                data[byteIndex] = stream.ReadByte();
            }

            if (Received != null)
            {
                Received(data, from);
            }
        }

        private void ConfigureDriver(NetworkSettings settings)
        {
            try
            {
                settings.WithFragmentationStageParameters(payloadCapacity: 16384);
                driver = NetworkDriver.Create(settings);

                // UTP 1.3 requires fragmentation before reliability so each fragment fits the MTU.
                snapshotPipeline = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            }
            finally
            {
                settings.Dispose();
            }
        }

        private void SendOn(NetworkConnection conn, byte[] packet)
        {
            // Serialises each custom packet byte into Unity Transport's writer and sends it on one connection.
            if (!driver.IsCreated || !conn.IsCreated || packet == null || packet.Length == 0)
            {
                return;
            }

            DataStreamWriter writer;

            // Large packets and enemy snapshots need the fragmentation/reliability pipeline.
            // Small gameplay events still use NetRunner's explicit acknowledgement protocol when required.
            NetworkPipeline pipeline = packet.Length > 1200 || (packet.Length > 1 && packet[1] == 7)
                ? snapshotPipeline : NetworkPipeline.Null;
            if (driver.BeginSend(pipeline, conn, out writer, packet.Length) != 0)
            {
                return;
            }

            int byteIndex;

            for (byteIndex = 0; byteIndex < packet.Length; byteIndex++)
            {
                writer.WriteByte(packet[byteIndex]);
            }

            driver.EndSend(writer);
        }

        private static async Task<bool> SignIn()
        {
            // Initialises Unity Services and signs in anonymously because Relay requires an authenticated session.
            try
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[NetRelay] Sign-in failed: " + exception.Message);
                return false;
            }
        }

        private static RelayServerData HostRelayData(Allocation alloc)
        {
            // Uses host connection data for both sides of the host transport setup.
            string host;
            ushort port;
            PickUdp(alloc.ServerEndpoints, alloc.RelayServer, out host, out port);
            return new RelayServerData(
                host,
                port,
                alloc.AllocationIdBytes,
                alloc.ConnectionData,
                alloc.ConnectionData,
                alloc.Key,
                false);
        }

        private static RelayServerData ClientRelayData(JoinAllocation alloc)
        {
            // Uses client and host connection data from the join allocation to reach the existing host.
            string host;
            ushort port;
            PickUdp(alloc.ServerEndpoints, alloc.RelayServer, out host, out port);
            return new RelayServerData(
                host,
                port,
                alloc.AllocationIdBytes,
                alloc.ConnectionData,
                alloc.HostConnectionData,
                alloc.Key,
                false);
        }

        private static void PickUdp(List<RelayServerEndpoint> endpoints, RelayServer fallback, out string host, out ushort port)
        {
            // Prefers a UDP Relay endpoint, then falls back to the allocation's older server information.
            host = "";
            port = 0;
            if (endpoints != null)
            {
                int endpointIndex;

                for (endpointIndex = 0; endpointIndex < endpoints.Count; endpointIndex++)
                {
                    RelayServerEndpoint relayEndpoint = endpoints[endpointIndex];
                    if (relayEndpoint == null || relayEndpoint.ConnectionType != "udp")
                    {
                        // Skips unsupported endpoints because this project uses the UDP transport path.
                        continue;
                    }

                    host = relayEndpoint.Host;
                    port = (ushort)relayEndpoint.Port;
                    if (host != null && host.IndexOf(':') < 0)
                    {
                        return;
                    }
                }
            }

            if (host != null && host.Length > 0)
            {
                return;
            }

            if (fallback != null)
            {
                host = fallback.IpV4;
                port = (ushort)fallback.Port;
            }
        }

        private static IPEndPoint FakeEnd(int index)
        {
            // Creates a local-only identity for Relay peers so the existing LAN-oriented packet code still works.
            return new IPEndPoint(IPAddress.Parse("127.0.0.2"), 21000 + index);
        }

        private static bool EndsEqual(IPEndPoint firstEndpoint, IPEndPoint secondEndpoint)
        {
            // Compares address and port together because either value alone is not a unique peer identity.
            if (firstEndpoint == null || secondEndpoint == null)
            {
                return false;
            }

            return firstEndpoint.Address.Equals(secondEndpoint.Address) && firstEndpoint.Port == secondEndpoint.Port;
        }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
