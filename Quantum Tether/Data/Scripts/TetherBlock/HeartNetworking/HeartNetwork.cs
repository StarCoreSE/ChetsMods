﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace InventoryTether.Networking
{
    public class HeartNetwork : ComponentBase
    {
        public static HeartNetwork I;

        private int _networkLoadUpdate;
        public int NetworkLoadTicks = 240;

        private readonly List<IMyPlayer> TempPlayers = new List<IMyPlayer>();
        public Dictionary<Type, int> TypeNetworkLoad = new Dictionary<Type, int>();

        public ushort NetworkId { get; private set; }
        public int TotalNetworkLoad { get; private set; }

        private Dictionary<long, DateTime> _rateLimiter = new Dictionary<long, DateTime>();
        
        public override void Init(string id)
        {
            base.Init(id);
            I = this;

            NetworkId = 8706;
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(NetworkId, ReceivedPacket);

            foreach (var type in PacketBase.PacketTypes) TypeNetworkLoad.Add(type, 0);
        }

        public override void UpdateTick()
        {
            _networkLoadUpdate--;
            if (_networkLoadUpdate <= 0)
            {
                _networkLoadUpdate = NetworkLoadTicks;
                TotalNetworkLoad = 0;
                foreach (var networkLoadArray in TypeNetworkLoad.Keys.ToArray())
                {
                    TotalNetworkLoad += TypeNetworkLoad[networkLoadArray];
                    TypeNetworkLoad[networkLoadArray] = 0;
                }

                TotalNetworkLoad /= NetworkLoadTicks / 60; // Average per-second

                ctr++;
                if (ctr % 4 == 0)
                {
                    Log.Info($"Network Load: {TotalNetworkLoad}");
                }
            }
        }

        public override void Close()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(NetworkId, ReceivedPacket);
            I = null;
        }

        #region Public Methods
        /// <summary>
        /// Used to rate-limit packets by ID.
        /// </summary>
        /// <param name="id"></param>
        /// /// <param name="delayMs">Milliseconds to wait before allowing another packet</param>
        /// <returns>Bool</returns>
        public static bool CheckRateLimit(long id, double delayMs = 5000 / 60d)
        {
            if (I == null)
                throw new Exception("Null HeartNetwork.I!");

            DateTime originalTime;
            DateTime nowTime = DateTime.Now; // caching because i'm a nerd
            if (!I._rateLimiter.TryGetValue(id, out originalTime) || (nowTime - originalTime).TotalMilliseconds >= delayMs)
            {
                I._rateLimiter[id] = nowTime;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the Type with the highest network load
        /// </summary>
        /// <returns>KeyValuePair (Type,int)</returns>
        public KeyValuePair<Type, int> HighestNetworkLoad()
        {
            Type highest = null;

            foreach (var networkLoadArray in TypeNetworkLoad)
                if (highest == null || networkLoadArray.Value > TypeNetworkLoad[highest])
                    highest = networkLoadArray.Key;

            return new KeyValuePair<Type, int>(highest, TypeNetworkLoad[highest]);
        }

        /// <summary>
        /// Relays Packet to Client
        /// </summary>
        /// <param name="packet"></param>
        /// /// <param name="playerSteamId"></param>
        /// /// /// <param name="serialized"></param>
        public void SendToPlayer(PacketBase packet, ulong playerSteamId, byte[] serialized = null)
        {
            RelayToClient(packet, playerSteamId, MyAPIGateway.Session?.Player?.SteamUserId ?? 0, serialized);
        }

        /// <summary>
        /// Relays Packet to Everyone
        /// </summary>
        /// <param name="packet"></param>
        /// /// <param name="serialized"></param>
        public void SendToEveryone(PacketBase packet, byte[] serialized = null)
        {
            RelayToClients(packet, MyAPIGateway.Session?.Player?.SteamUserId ?? 0, serialized);
        }

        /// <summary>
        /// Relays Packet to Server
        /// </summary>
        /// <param name="packet"></param>
        /// /// <param name="serialized"></param>
        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            RelayToServer(packet, MyAPIGateway.Session?.Player?.SteamUserId ?? 0, serialized);
        }
        #endregion

        #region Private Methods
        private int ctr = 0;

        private void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer)
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(serialized);
                TypeNetworkLoad[packet.GetType()] += serialized.Length;
                HandlePacket(packet, senderSteamId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RepairModule] Error in HeatNetwork Sync! See Log for more Details!");
            }
        }

        private void HandlePacket(PacketBase packet, ulong senderSteamId)
        {
            packet.Received(senderSteamId);
        }

        private void RelayToClients(PacketBase packet, ulong senderSteamId = 0, byte[] serialized = null)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            TempPlayers.Clear();
            MyAPIGateway.Players.GetPlayers(TempPlayers);

            foreach (var p in TempPlayers)
            {
                // skip sending to self (server player) or back to sender
                if (p.SteamUserId == MyAPIGateway.Multiplayer.ServerId || p.SteamUserId == senderSteamId)
                    continue;

                if (serialized == null) // only serialize if necessary, and only once.
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(NetworkId, serialized, p.SteamUserId);
            }

            TempPlayers.Clear();
        }

        private void RelayToClient(PacketBase packet, ulong playerSteamId, ulong senderSteamId, byte[] serialized = null)
        {
            if (playerSteamId == MyAPIGateway.Multiplayer.ServerId || playerSteamId == senderSteamId)
                return;

            if (serialized == null) // only serialize if necessary, and only once.
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageTo(NetworkId, serialized, playerSteamId);
        }

        private void RelayToServer(PacketBase packet, ulong senderSteamId = 0, byte[] serialized = null)
        {
            if (senderSteamId == MyAPIGateway.Multiplayer.ServerId)
                return;

            if (serialized == null) // only serialize if necessary, and only once.
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageToServer(NetworkId, serialized);
        }
        #endregion
    }
}