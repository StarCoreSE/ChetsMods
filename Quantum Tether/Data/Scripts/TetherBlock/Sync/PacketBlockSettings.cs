using ProtoBuf;
using Sandbox.ModAPI;

namespace InventoryTether.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketBlockSettings : PacketBase
    {
        [ProtoMember(1)]
        public long EntityId;

        [ProtoMember(2)]
        public TetherBlockSettings Settings;

        public PacketBlockSettings() { } // Empty constructor required for deserialization

        public void Send(long entityId, TetherBlockSettings settings)
        {
            EntityId = entityId;
            Settings = settings;

            if(MyAPIGateway.Multiplayer.IsServer)
                Networking.RelayToClients(this);
            else
                Networking.SendToServer(this);
        }

        public override void Received(ref bool relay)
        {
            var block = MyAPIGateway.Entities.GetEntityById(this.EntityId) as IMyCollector;

            if(block == null)
                return;

            var logic = block.GameLogic?.GetAs<InventoryTetherLogic>();

            if(logic == null)
                return;

            logic.Settings.BlockRange = this.Settings.BlockRange;

            relay = true;
        }
    }
}
