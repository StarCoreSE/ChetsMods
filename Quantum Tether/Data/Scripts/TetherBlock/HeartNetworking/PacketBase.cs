using System;
using ProtoBuf;
using InventoryTether.Networking.Custom;

namespace InventoryTether.Networking
{
    [ProtoContract(UseProtoMembersOnly = true)]
    [ProtoInclude(1, typeof(BoolSyncPacket))]
    [ProtoInclude(2, typeof(DictSyncPacket))]
    [ProtoInclude(3, typeof(FloatSyncPacket))]
    public abstract class PacketBase
    {
        public static readonly Type[] PacketTypes =
        {
            typeof(PacketBase),
            typeof(BoolSyncPacket),
            typeof(DictSyncPacket),
            typeof(FloatSyncPacket),
        };

        /// <summary>
        ///     Called whenever your packet is received.
        /// </summary>
        /// <param name="SenderSteamId"></param>
        public abstract void Received(ulong SenderSteamId);
    }
}