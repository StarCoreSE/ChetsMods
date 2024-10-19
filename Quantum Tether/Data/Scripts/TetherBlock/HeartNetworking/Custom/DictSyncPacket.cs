using System;
using ProtoBuf;
using Sandbox.ModAPI;

namespace InventoryTether.Networking.Custom
{
    [ProtoContract]
    public class DictSyncPacket : PacketBase
    {
        [ProtoMember(41)] public string propertyName;
        [ProtoMember(42)] public string key;
        [ProtoMember(43)] public ComponentData value;
        [ProtoMember(44)] public long entityId;

        public override void Received(ulong SenderSteamId)
        {
            Log.Info("Received Dictionary Sync Packet");

            var inventoryTether = InventoryTether.GetLogic<InventoryTether>(entityId);

            if (inventoryTether != null)
            {
                switch (propertyName)
                {
                    case nameof(InventoryTether.TargetItems):
                        if (value != null)
                        {
                            inventoryTether.TargetItems[key] = value;
                        }
                        else
                        {
                            inventoryTether.TargetItems.Remove(key);
                        }
                        break;
                }

                if (MyAPIGateway.Session.IsServer)
                {
                    HeartNetwork.I.SendToEveryone(this); // Broadcast the change to all clients
                }
            }
            else
            {
                Log.Info($"Received method failed: InventoryTether is null. Entity ID: {entityId}");
            }
        }

        public static void SyncDictionaryEntry(long entityId, string propertyName, string key, ComponentData value)
        {
            try
            {
                var packet = new DictSyncPacket
                {
                    entityId = entityId,
                    propertyName = propertyName,
                    key = key,
                    value = value // If null, this will represent a removal
                };

                Log.Info("Dictionary Entry Sync Packet Added to Queue");

                PacketQueueManager.I.EnqueuePacket(packet);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }
}