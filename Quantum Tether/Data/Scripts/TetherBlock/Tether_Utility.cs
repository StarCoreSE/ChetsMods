using System.Collections.Generic;
using InventoryTether.Sync;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Utils;

namespace InventoryTether
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class InventoryTetherMod : MySessionComponentBase
    {
        public static InventoryTetherMod Instance;

        public bool ControlsCreated = false;
        public Networking Networking = new Networking(58936);
        public List<MyEntity> Entities = new List<MyEntity>();
        public PacketBlockSettings CachedPacketSettings;

        public readonly MyStringId MATERIAL_SQUARE = MyStringId.GetOrCompute("Square");
        public readonly MyStringId MATERIAL_DOT = MyStringId.GetOrCompute("WhiteDot");

        public override void LoadData()
        {
            Instance = this;

            Networking.Register();

            CachedPacketSettings = new PacketBlockSettings();
        }

        protected override void UnloadData()
        {
            Instance = null;

            Networking?.Unregister();
            Networking = null;
        }
    }
}
