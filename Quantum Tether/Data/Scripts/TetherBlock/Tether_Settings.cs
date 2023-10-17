using ProtoBuf;

namespace InventoryTether
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class TetherBlockSettings
    {
        [ProtoMember(1)]
        public float BlockRange;

        [ProtoMember(2)]
        public float StockAmount;

        [ProtoMember(3)]
        public bool ShowArea;

        [ProtoMember(4)]
        public bool HardCap;
    }
}
