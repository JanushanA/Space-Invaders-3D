using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Game.Combat;
using Game.Core;
using Game.Player;
using UnityEngine;
namespace Game.Net
{
    // Adds acknowledged gameplay messages to the same protocol used by LAN and Relay.
    // Retries and duplicate detection protect one-time effects such as rewards and skill requests.
    public partial class NetRunner
    {
        private const byte MsgReliable = 15;
        private const byte MsgAck = 16;
        private const byte MsgLoadout = 17;
        private const byte MsgSkill = 18;
        private const byte MsgReward = 19;
        private const byte MsgResult = 22;
        private const byte MsgSalvage = 23;
        private const byte MsgIntercept = 24;
        private uint nextEnemyProjectileId;

        // Keep the serialized packet unchanged across retries so its round and message ID stay stable.
        private sealed class PendingMessage
        {
            public byte[] Packet;
            public IPEndPoint To;
            public float NextSend;
            public int Round;
            public uint Id;
        }
        private readonly List<PendingMessage> pendingMessages = new List<PendingMessage>();
        private readonly HashSet<string> receivedMessages = new HashSet<string>();
        private uint reliableId;
        private uint healthSequence;
        private uint receivedHealthSequence;
        private bool insideReliable;
        private uint lastLoadout;
        private int loadoutRound = -1;
        private int completedRound = -1;
        private bool returnToLobbyWhenSynced;

        private void ResetGameplayNetwork()
        {
            pendingMessages.Clear();
            receivedMessages.Clear();
            reliableId = 0;
            nextEnemyProjectileId = 0;
            healthSequence = receivedHealthSequence = 0;
            loadoutRound = completedRound = -1;
            returnToLobbyWhenSynced = false;
        }

        private void TickReliable()
        {
            // Retry until acknowledged, unless the round ended or the recipient left.
            // Unscaled time keeps delivery working while gameplay is paused.
            for (int entryIndex = pendingMessages.Count - 1; entryIndex >= 0; entryIndex--)
            {
                PendingMessage message = pendingMessages[entryIndex];
                if (message.Round != matchRound || (NetSession.IsHost && SlotForEnd(message.To) < 1))
                {
                    pendingMessages.RemoveAt(entryIndex);
                    continue;
                }

                if (Time.unscaledTime < message.NextSend)
                {
                    continue;
                }

                message.NextSend = Time.unscaledTime + 0.2f;
                Send(gameSock, message.Packet, message.To);
            }

            // Finish delivery before changing scenes so clients can apply the final match result.
            if (returnToLobbyWhenSynced && pendingMessages.Count == 0)
            {
                returnToLobbyWhenSynced = false;
                ReturnEveryoneToLobby();
            }
        }

        private void SendReliable(byte[] payload, IPEndPoint recipient)
        {
            if (recipient == null)
            {
                return;
            }

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                // Envelope: magic/type at bytes 0-1, round at 2-5, ID at 6-9, inner packet from 10.
                uint messageId = ++reliableId;
                writer.Write(Magic);
                writer.Write(MsgReliable);
                writer.Write(matchRound);
                writer.Write(messageId);
                writer.Write(payload);
                var message = new PendingMessage { Packet = stream.ToArray(), To = recipient, Round = matchRound, Id = messageId, NextSend = Time.unscaledTime + 0.2f };
                pendingMessages.Add(message);
                Send(gameSock, message.Packet, recipient);
            }
        }

        private void BroadcastReliable(byte[] payload)
        {
            for (int slot = 1; slot < peers.Length; slot++)
            {
                if (peers[slot] != null)
                {
                    SendReliable(payload, peers[slot]);
                }
            }
        }

        private static bool IsGameplayType(byte type)
        {
            return type == MsgReliable || type == MsgAck || type == MsgLoadout || type == MsgSkill || type == MsgReward || type == MsgResult || type == MsgSalvage || type == MsgIntercept;
        }

        private static bool ValidGameplayPacket(byte[] data)
        {
            switch (data[1])
            {
                case MsgReliable:

                    // The inner packet keeps its own header; nested envelopes and acknowledgements are forbidden.
                    if (data.Length < 12 || data[10] != Magic || data[11] == MsgReliable || data[11] == MsgAck)
                    {
                        return false;
                    }
                    byte[] payload = new byte[data.Length - 10];
                    Array.Copy(data, 10, payload, 0, payload.Length);
                    return BitConverter.ToUInt32(data, 6) != 0 && ValidPacket(payload);
                case MsgAck:
                    return data.Length == 10;
                case MsgIntercept:
                    return data.Length == 6 && BitConverter.ToUInt32(data, 2) != 0;
                case MsgLoadout:
                    return data.Length == 7 && data[2] > 0 && data[2] < NetSession.MaxPlayers && (BitConverter.ToUInt32(data, 3) & 0x80000000u) == 0;
                case MsgSkill:
                    return data.Length == 8 && data[2] > 0 && data[2] < NetSession.MaxPlayers && RemotePilotAuthority.IsActiveSkill((SkillId)data[3]) && (BitConverter.ToUInt32(data, 4) & 0x80000000u) == 0;
                case MsgReward:
                    return data.Length == 7 && BitConverter.ToInt32(data, 2) > 0 && BitConverter.ToInt32(data, 2) <= 1000 && data[6] <= 1;
                case MsgResult:
                    return data.Length == 4 && ValidLevel(data[2]) && data[3] >= 1 && data[3] <= 3;
                case MsgSalvage:
                    return data.Length == 10 && data[2] < 6 && data[3] > 0 && data[3] <= 50 && data[4] <= 6 && BitConverter.ToInt32(data, 6) >= 0 && BitConverter.ToInt32(data, 6) <= 1000;
                default:
                    return false;
            }
        }

        private bool AllowedGameplaySender(byte[] data, IPEndPoint from)
        {
            byte type = data[1];

            // Match the sender to an admitted guest, or to this client's known host endpoint.
            bool peer = NetSession.IsHost ? SlotForEnd(from) > 0 : EndsEqual(hostEnd, from);
            if (!peer)
            {
                return false;
            }

            if (type == MsgReliable || type == MsgAck)
            {
                return true;
            }

            // Gameplay effects must arrive through the duplicate-checked envelope.
            if (!insideReliable)
            {
                return false;
            }

            if (NetSession.IsHost)
            {
                return NetSession.InMatch && (type == MsgLoadout || type == MsgSkill) && data[2] == SlotForEnd(from);
            }

            return type == MsgReward || type == MsgResult || type == MsgSalvage || (type == MsgIntercept && NetSession.InMatch);
        }

        private void HandleGameplayPacket(byte[] data, IPEndPoint from)
        {
            byte type = data[1];
            if (type == MsgAck)
            {
                int round = BitConverter.ToInt32(data, 2);
                uint messageId = BitConverter.ToUInt32(data, 6);

                // An acknowledgement clears only this recipient's copy of this round's message.
                pendingMessages.RemoveAll(item => item.Round == round && item.Id == messageId && EndsEqual(item.To, from));
                return;
            }

            if (type == MsgReliable)
            {
                int round = BitConverter.ToInt32(data, 2);
                uint reliableMessageId = BitConverter.ToUInt32(data, 6);

                // A client may learn the next round number from its first reliable start message.
                bool nextStart = !NetSession.IsHost && data[11] == MsgStart && round > matchRound &&
                    BitConverter.ToInt32(data, data.Length - 4) == round;
                if (round != matchRound && !nextStart)
                {
                    return;
                }

                string key = from + ":" + round + ":" + reliableMessageId;
                byte[] acknowledgementPacket = new byte[10];
                acknowledgementPacket[0] = Magic;
                acknowledgementPacket[1] = MsgAck;
                Array.Copy(BitConverter.GetBytes(round), 0, acknowledgementPacket, 2, 4);
                Array.Copy(BitConverter.GetBytes(reliableMessageId), 0, acknowledgementPacket, 6, 4);

                // A lost acknowledgement causes a retry: acknowledge again without replaying the effect.
                if (receivedMessages.Contains(key))
                {
                    Send(gameSock, acknowledgementPacket, from);
                    return;
                }

                byte[] payload = new byte[data.Length - 10];
                Array.Copy(data, 10, payload, 0, payload.Length);
                if ((payload[1] == MsgFail || payload[1] == MsgLobbyBack) && !NetSession.InMatch)
                {
                    receivedMessages.Add(key);
                    Send(gameSock, acknowledgementPacket, from);
                    return;
                }

                // Reuse the normal dispatcher, with permission to accept reliable-only message types.
                insideReliable = true;
                try
                {
                    if (!AllowedSender(payload, from))
                    {
                        return;
                    }

                    // Leave the message unacknowledged while the new combat scene is still loading.
                    // The sender will retry after the matching round has created its NetCombat instance.
                    if ((payload[1] == MsgSkill || payload[1] == MsgLoadout || payload[1] == MsgReward || payload[1] == MsgSalvage || payload[1] == MsgIntercept) &&
                        (NetCombat.Instance == null || NetCombat.Instance.Round != matchRound))
                    {
                        return;
                    }

                    Handle(payload, from);
                    receivedMessages.Add(key);
                    Send(gameSock, acknowledgementPacket, from);
                }
                finally
                {
                    insideReliable = false;
                }

                return;
            }

            if (type == MsgLoadout && NetCombat.Instance != null)
            {
                NetCombat.Instance.ApplyLoadout(data[2], BitConverter.ToUInt32(data, 3));
            }
            else if (type == MsgSkill && NetCombat.Instance != null)
            {
                NetCombat.Instance.ApplyLoadout(data[2], BitConverter.ToUInt32(data, 4));
                NetCombat.Instance.ApplySkill(data[2], (SkillId)data[3]);
            }
            else if (type == MsgReward && NetCombat.Instance != null)
            {
                NetCombat.Instance.ApplyReward(BitConverter.ToInt32(data, 2), data[6] == 1);
            }
            else if (type == MsgIntercept && NetCombat.Instance != null)
            {
                NetCombat.Instance.CancelNetworkProjectile(BitConverter.ToUInt32(data, 2));
            }
            else if (type == MsgSalvage)
            {
                Game.Progression.PilotInventory.Grant(data[2], data[3], data[4] - 1, BitConverter.ToInt32(data, 6));
            }
            else if (type == MsgResult)
            {
                GameSession.CompleteLevel(data[2], data[3]);
                completedRound = matchRound;
            }
        }

        public void NoteInterception(uint projectileId)
        {
            if (!NetSession.Active || !NetSession.IsHost || !NetSession.InMatch || projectileId == 0)
            {
                return;
            }

            // The host sends the projectile ID even if a client has not yet received its launch.
            byte[] packet = new byte[6] { Magic, MsgIntercept, 0, 0, 0, 0 };
            Array.Copy(BitConverter.GetBytes(projectileId), 0, packet, 2, 4);
            BroadcastReliable(packet);
        }

        public void SendLoadout(uint unlockedSkills)
        {
            if (!NetSession.Active || NetSession.IsHost || !NetSession.InMatch || hostEnd == null)
            {
                return;
            }

            // One outstanding reliable declaration is enough until the loadout changes.
            if (loadoutRound == matchRound && lastLoadout == unlockedSkills)
            {
                return;
            }

            loadoutRound = matchRound;
            lastLoadout = unlockedSkills;
            byte[] packet = new byte[7];
            packet[0] = Magic;
            packet[1] = MsgLoadout;
            packet[2] = (byte)NetSession.LocalSlot;
            Array.Copy(BitConverter.GetBytes(unlockedSkills), 0, packet, 3, 4);
            SendReliable(packet, hostEnd);
        }

        public bool RequestSkill(SkillId skill, uint unlockedSkills)
        {
            if (!NetSession.Active || NetSession.IsHost || !NetSession.InMatch || hostEnd == null)
            {
                return false;
            }

            SendLoadout(unlockedSkills);

            // Carry the loadout again so this request does not depend on packet arrival order.
            byte[] packet = new byte[8] { Magic, MsgSkill, (byte)NetSession.LocalSlot, (byte)skill, 0, 0, 0, 0 };
            Array.Copy(BitConverter.GetBytes(unlockedSkills), 0, packet, 4, 4);
            SendReliable(packet, hostEnd);
            return true;
        }

        public void AwardKill(GameObject instigator, int experiencePoints, bool countKill)
        {
            if (!NetSession.Active || !NetSession.IsHost || !NetSession.InMatch || instigator == null)
            {
                return;
            }

            // The host applies its own reward locally; a guest receives a reliable owner-only grant.
            var remote = instigator.GetComponentInParent<RemotePilotAuthority>();
            if (remote == null)
            {
                PlayerProgression progression = instigator.GetComponentInParent<PlayerProgression>();
                if (progression != null)
                {
                    if (countKill)
                    {
                        GameSession.NoteKill(experiencePoints);
                    }

                    progression.AddXp(experiencePoints);
                }

                return;
            }

            byte[] packet = new byte[7];
            packet[0] = Magic;
            packet[1] = MsgReward;
            Array.Copy(BitConverter.GetBytes(experiencePoints), 0, packet, 2, 4);
            packet[6] = countKill ? (byte)1 : (byte)0;
            SendReliable(packet, peers[remote.Slot]);
        }

        public void CompletePartyLevel(int level, int stars)
        {
            if (!NetSession.Active || !NetSession.IsHost || !NetSession.InMatch || completedRound == matchRound)
            {
                return;
            }

            completedRound = matchRound;
            GameSession.CompleteLevel(level, stars);
            BroadcastReliable(new byte[] { Magic, MsgResult, (byte)level, (byte)stars });
        }

        public void AwardSalvage(GameObject instigator, int material, int count, int weapon, int credits, bool party)
        {
            if (!NetSession.Active || !NetSession.IsHost || !NetSession.InMatch)
            {
                return;
            }

            // Salvage fields: material, count, weapon+1, reserved byte, then 32-bit credits.
            // Encoding weapon+1 leaves zero available for "no weapon".
            var packet = new byte[10] { Magic, MsgSalvage, (byte)material, (byte)count, (byte)(weapon + 1), 0, 0, 0, 0, 0 };
            Array.Copy(BitConverter.GetBytes(credits), 0, packet, 6, 4);
            var remote = instigator != null ? instigator.GetComponentInParent<RemotePilotAuthority>() : null;
            if (party)
            {
                Game.Progression.PilotInventory.Grant(material, count, weapon, credits);
                BroadcastReliable(packet);
            }
            else if (remote != null)
            {
                SendReliable(packet, peers[remote.Slot]);
            }
            else if (instigator != null && instigator.GetComponentInParent<PlayerProgression>() != null)
            {
                Game.Progression.PilotInventory.Grant(material, count, weapon, credits);
            }
        }
    }
}
