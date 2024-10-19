using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;

namespace InventoryTether.Networking.Custom
{
    [ProtoContract]
    public class FloatSyncPacket : PacketBase
    {
        [ProtoMember(31)] public string propertyName;
        [ProtoMember(32)] private float value;
        [ProtoMember(33)] public long entityId;

        public override void Received(ulong SenderSteamId)
        {
            Log.Info($"Received Float Sync: {propertyName} = {value}");

            var inventoryTether = InventoryTether.GetLogic<InventoryTether>(entityId);

            if (inventoryTether != null)
            {
                switch (propertyName)
                {
                    case nameof(InventoryTether.BlockRange):
                        inventoryTether.BlockRange = value;
                        break;
                        // Add other float properties as needed
                }

                if (MyAPIGateway.Session.IsServer)
                {
                    HeartNetwork.I.SendToEveryone(this);
                }
            }
            else
            {
                Log.Info($"Received method failed: InventoryTether is null. Entity ID: {entityId}");
            }
        }

        public static void SyncFloatProperty(long entityId, string propertyName, float value)
        {
            try
            {
                var packet = new FloatSyncPacket
                {
                    entityId = entityId,
                    propertyName = propertyName,
                    value = value
                };
                   
                Log.Info($"Float-Type Packet Added to Queue: {propertyName} = {value}");

                PacketQueueManager.I.EnqueuePacket(packet);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }
}