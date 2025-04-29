using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using static Sandbox.ModAPI.MyAPIGateway;

namespace StarCore.Highlights
{
    public enum ModeSwitchEnum
    {
        Highlight = 0,
        Cutaway = 1,
        Diagnostic = 2,
    }

    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public partial class HighlightTool_Core : MySessionComponentBase
    {
        public static HighlightTool_Core Instance = null;
        private IMyAutomaticRifleGun EquippedTool;

        internal ModeSwitchEnum CurrentMode;         
        
        private int highlightIndex = 0;    

        #region Update Methods
        public override void LoadData()
        {
            Instance = this;
        }

        public override void BeforeStart()
        {
            if (Utilities.IsDedicated && MyAPIGateway.Multiplayer.IsServer)
                return;

            CutawayManager.I.Init();
            HighlightManager.I.Init();
            DiagnosticManager.I.Init();
            HUDManager.I.Init();

            var gunDef = MyDefinitionManager.Static.GetWeaponDefinition(new MyDefinitionId(typeof(MyObjectBuilder_WeaponDefinition), "WeaponHighlightTool"));
            for (int i = 0; i < gunDef.WeaponAmmoDatas.Length; i++)
            {
                var ammoData = gunDef.WeaponAmmoDatas[i];

                if (ammoData == null)
                    continue;

                ammoData.ShootIntervalInMiliseconds = int.MaxValue;
            }

            SetUpdateOrder(MyUpdateOrder.AfterSimulation);
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            if (EquippedTool == null || EquippedTool.Closed || EquippedTool.MarkedForClose)
            {
                HolsterTool();
                return;
            }

            IMyCharacter character = MyAPIGateway.Session.ControlledObject as IMyCharacter;
            if (character != null)
            {
                HoldingTool(character);
            }
        }

        public override void Draw()
        {
            base.Draw();

            CutawayManager.I.Draw();
            HighlightManager.I.Draw();
            HUDManager.I.Draw(EquippedTool != null);
        }

        protected override void UnloadData()
        {
            base.UnloadData();

            CutawayManager.I.Unload();
            HighlightManager.I.Unload();
            DiagnosticManager.I.Unload();
            HUDManager.I.Unload();

            Instance = null;
        }
        #endregion

        #region Tool
        public void EquipTool(IMyAutomaticRifleGun gun)
        {
            EquippedTool = gun;
        }

        private void HolsterTool()
        {
            EquippedTool = null;
        }

        public void HoldingTool(IMyCharacter character)
        {
            bool inputreadable = !Gui.IsCursorVisible && !Gui.ChatEntryVisible;
            if (!inputreadable)
                return;

            // Prevent ADS
            var weaponComp = character.Components.Get<MyCharacterWeaponPositionComponent>();
            if (weaponComp.IsInIronSight)
            {
                EquippedTool.EndShoot(MyShootActionEnum.SecondaryAction);
                EquippedTool.Shoot(MyShootActionEnum.SecondaryAction, Vector3.Forward, null, null);
                EquippedTool.EndShoot(MyShootActionEnum.SecondaryAction);
            }

            int scrollDelta = Input.DeltaMouseScrollWheelValue();
            bool leftClick = Input.IsNewGameControlPressed(MyControlsSpace.PRIMARY_TOOL_ACTION);
            bool rightClick = Input.IsNewGameControlPressed(MyControlsSpace.SECONDARY_TOOL_ACTION);
            bool shiftPressed = Input.IsAnyShiftKeyPressed();
            bool middleMousebuttonPressed = Input.IsNewMiddleMousePressed();
            bool rKeyPressed = Input.IsNewKeyPressed(VRage.Input.MyKeys.R);

            if (shiftPressed)
            {
                if (scrollDelta > 0)
                {
                    CurrentMode = (ModeSwitchEnum)(((int)CurrentMode + 1) % 3);
                    if (CastForGrid() != null && CutawayManager.I.cachedGrid != null)
                        CutawayManager.I.ClearCache(true, CastForGrid());
                    if (CastForGrid() != null && HighlightManager.I.ActiveGrids.ContainsKey(CastForGrid().EntityId))
                        HighlightManager.I.ResetHighlights(CastForGrid().EntityId);
                }
                else if (scrollDelta < 0)
                {
                    CurrentMode = (ModeSwitchEnum)(((int)CurrentMode + 3 - 1) % 3);
                    if (CastForGrid() != null && CutawayManager.I.cachedGrid != null)
                        CutawayManager.I.ClearCache(true, CastForGrid());
                    if (CastForGrid() != null && HighlightManager.I.ActiveGrids.ContainsKey(CastForGrid().EntityId))
                        HighlightManager.I.ResetHighlights(CastForGrid().EntityId);
                }
                return;
            }

            switch (CurrentMode)
            {
                case ModeSwitchEnum.Highlight:
                    HandleHighlightMode(scrollDelta, leftClick, rightClick, rKeyPressed);
                    break;

                case ModeSwitchEnum.Cutaway:
                    HandleCutawayMode(scrollDelta, leftClick, rightClick, middleMousebuttonPressed, rKeyPressed);
                    break;

                case ModeSwitchEnum.Diagnostic:
                    HandleDiagnosticMode(scrollDelta, leftClick, rightClick, rKeyPressed);
                    break;
            }
        }
        #endregion

        private void HandleHighlightMode(int scrollDelta, bool leftClick, bool rightClick, bool rKeyPressed)
        {
            var grid = CastForGrid();

            if (rKeyPressed)
            {
                long entityId = grid != null ? grid.EntityId : 0;
                HighlightManager.I.ResetHighlights(entityId);
            }

            if (scrollDelta != 0)
            {
                int direction = scrollDelta > 0 ? 1 : -1;
                HighlightManager.I.SwitchFilter(direction, ref highlightIndex);
            }

            if (leftClick)
            {
                long entityId = grid != null ? grid.EntityId : 0;
                HighlightManager.I.ApplyFilter(entityId);         
            }
            else if (rightClick)
            {
                long entityId = grid != null ? grid.EntityId : 0;
                HighlightManager.I.RemoveFilter(entityId);           
            }
        }

        private void HandleCutawayMode(int scrollDelta, bool leftClick, bool rightClick, bool middleMousebuttonPressed, bool rKeyPressed)
        {
            var grid = CastForGrid();

            if (rKeyPressed)
            {
                CutawayManager.I.ClearCache(false);
            }

            if (grid == null)
            {
                return;
            }

            CutawayManager.I.Update(CastForGrid());

            if (scrollDelta != 0)
            {
                double step = scrollDelta / 48.0;
                CutawayManager.I.CutawayPosition += (float)step;
                CutawayManager.I.UpdateBlocks(grid);
            }

            if (leftClick)
            {
                CutawayManager.I.CutawayAxis = (CutawayManager.CutawayAxisEnum)(((int)CutawayManager.I.CutawayAxis + 1) % 3);
                CutawayManager.I.CutawayPosition = (float)1.25;
                CutawayManager.I.UpdateBlocks(grid);
            }
            else if (rightClick)
            {
                CutawayManager.I.CutawayAxis = (CutawayManager.CutawayAxisEnum)(((int)CutawayManager.I.CutawayAxis + 3 - 1) % 3);
                CutawayManager.I.CutawayPosition = (float)1.25;
                CutawayManager.I.UpdateBlocks(grid);
            }

            if (middleMousebuttonPressed)
            {
                CutawayManager.I.IsNormalInverted = !CutawayManager.I.IsNormalInverted;
                CutawayManager.I.UpdateBlocks(grid);
            }        
        }

        private void HandleDiagnosticMode(int scrollDelta, bool leftClick, bool rightClick, bool rKeyPressed)
        {
            var grid = CastForGrid();

            if (rKeyPressed)
            {
                long entityId = grid != null ? grid.EntityId : 0;
                DiagnosticManager.I.ResetFilters(entityId);
            }

            if (scrollDelta != 0)
            {
                int direction = scrollDelta > 0 ? 1 : -1;
                DiagnosticManager.I.SwitchFilter(direction);
            }

            if (leftClick)
            {
                long entityId = grid != null ? grid.EntityId : 0;
                DiagnosticManager.I.ApplyFilter(entityId);
            }
            else if (rightClick)
            {
                long entityId = grid != null ? grid.EntityId : 0;
                DiagnosticManager.I.RemoveFilter(entityId);
            }
        }

        #region Utils
        internal IMyCubeGrid CastForGrid()
        {
            var playerCamera = MyAPIGateway.Session.Camera;
            var cameraMatrix = playerCamera.WorldMatrix;
            var rayOrigin = cameraMatrix.Translation;
            var rayDirection = cameraMatrix.Translation + cameraMatrix.Forward * 150;

            var raycastCache = new List<MyLineSegmentOverlapResult<MyEntity>>();
            var ray = new LineD(rayOrigin, rayDirection);
            MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref ray, raycastCache);

            foreach (var result in raycastCache)
            {
                var entity = result.Element;
                var cubeGrid = entity as IMyCubeGrid;
                if (cubeGrid != null)
                {
                    return cubeGrid;
                }
            }

            return null;
        }
        #endregion
    }
}
