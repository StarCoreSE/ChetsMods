using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace StarCore.Highlights
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), false, "HighlightsTool")]
    public class HighlightsTool : MyGameLogicComponent
    {
        private HighlightTool_Core Mod => HighlightTool_Core.Instance;

        private bool first = true;

        private IMyGunBaseUser Tool;
        private IMyCharacter ToolOwner;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        private void InitUpdate()
        {
            Tool = (IMyGunBaseUser)Entity;
            ToolOwner = Tool.Owner as IMyCharacter;

            if (Mod == null || ToolOwner == null || ToolOwner.Physics == null)
                return;

            var _rifle = (IMyAutomaticRifleGun)Entity;
            if (_rifle.GunBase == null)
            {
                MyLog.Default.WriteLineAndConsole($"[SysHighlight] _rifle.GunBase == null, Init Cancelled;\n ent={_rifle};\n owner={_rifle.Owner}/{_rifle.OwnerIdentityId.ToString()}");
                return;
            };

            if (_rifle.GunBase.CurrentAmmo <= 0)
                _rifle.GunBase.CurrentAmmo = 1;

            first = false;

            if (ToolOwner != null && MyAPIGateway.Session.Player != null && Tool.OwnerId == MyAPIGateway.Session.Player.IdentityId) 
            {
                Mod.EquipTool((IMyAutomaticRifleGun)Entity);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                if (first)
                {
                    InitUpdate();
                    return;
                }
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"[SysHighlight]\n{e}");
            }
        }
    }
}