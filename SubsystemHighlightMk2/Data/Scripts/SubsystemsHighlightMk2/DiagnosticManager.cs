using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using VRage;
using VRageMath;
using VRage.Game;
using VRage.ModAPI;
using VRage.Game.ModAPI;
using VRage.Collections;
using VRage.Utils;
using VRage.Network;

using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;
using static Sandbox.ModAPI.MyAPIGateway;
using static VRageRender.MyBillboard;
using System.ComponentModel.Design;
using VRage.GameServices;

namespace StarCore.Highlights
{
    public class DiagnosticManager
    {
        public static DiagnosticManager I = new DiagnosticManager();

        public enum DiagnosticTypeEnum
        {
            Incomplete = 0,
            Enabled = 1,
            Working = 2,
        }
        internal DiagnosticTypeEnum DiagnosticType;

        internal Dictionary<long, DiagnosticTypeEnum> ActiveDiagnostics = new Dictionary<long, DiagnosticTypeEnum>();
        private Dictionary<IMyCubeGrid, Dictionary<IMySlimBlock, DiagnosticTypeEnum>> HighlightedBlocksPerGrid = new Dictionary<IMyCubeGrid, Dictionary<IMySlimBlock, DiagnosticTypeEnum>>();

        private int HighlightIntensity;

        #region Update Methods
        public void Init()
        {
            I = this;

            HighlightIntensity = 3;
        }

        public void Update()
        {

        }

        public void Draw()
        {
           
        }

        public void Unload()
        {
            I = null;
        }

        private void OnGridClose(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null) 
                return;

            ResetFilters(grid.EntityId);
        }
        #endregion

        #region Select Filter
        public void SwitchFilter(int direction)
        {
            int typeCount = Enum.GetNames(typeof(DiagnosticTypeEnum)).Length;
            int newIndex = ((int)DiagnosticType + direction + typeCount) % typeCount;
            DiagnosticType = (DiagnosticTypeEnum)newIndex;
        }

        public void ApplyFilter(long entityId)
        {
            var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
            if (cubeGrid == null) 
                return;

            ResetFilters(entityId);

            ActiveDiagnostics[entityId] = DiagnosticType;
            FilterWrapper(DiagnosticType, entityId);       
        }

        public void RemoveFilter(long entityId)
        {
            ResetFilters(entityId);
        }

        public void ResetFilters(long entityId)
        {
            var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            if (HighlightedBlocksPerGrid.ContainsKey(cubeGrid))
            {
                foreach (var kvp in HighlightedBlocksPerGrid[cubeGrid])
                {
                    var block = kvp.Key;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, -1);
                }

                HighlightedBlocksPerGrid.Remove(cubeGrid);
            }

            if (ActiveDiagnostics.ContainsKey(entityId))
                ActiveDiagnostics.Remove(entityId);

            cubeGrid.OnMarkForClose -= OnGridClose;
        }
        #endregion

        #region Filters
        public void FilterWrapper(DiagnosticTypeEnum type, long entityId)
        {
            if (entityId == 0)
                return;

            var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            if (!HighlightedBlocksPerGrid.ContainsKey(cubeGrid))
                HighlightedBlocksPerGrid[cubeGrid] = new Dictionary<IMySlimBlock, DiagnosticTypeEnum>();

            var blockList = new List<IMySlimBlock>();
            cubeGrid.GetBlocks(blockList);

            switch (type)
            {
                case DiagnosticTypeEnum.Incomplete:
                    IncompleteFilter(blockList, cubeGrid);         
                    break;
                case DiagnosticTypeEnum.Enabled:
                    EnabledFilter(blockList, cubeGrid);
                    break;
                case DiagnosticTypeEnum.Working:
                    FunctionalFilter(blockList, cubeGrid);
                    return;

            }

            cubeGrid.OnMarkForClose += OnGridClose;
        }

        private void IncompleteFilter(List<IMySlimBlock> blocks, IMyCubeGrid grid)
        {
            foreach (var block in blocks)
            {
                if (block.FatBlock == null || block.FatBlock.Name == null)
                    continue;

                if (block.CurrentDamage != 0)
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.Red);
                }
                else if (block.CurrentDamage == 0 && !block.IsFullIntegrity)
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.Yellow);
                }
            }
        }

        private void EnabledFilter(List<IMySlimBlock> blocks, IMyCubeGrid grid)
        {
            foreach (var block in blocks)
            {
                if (block.FatBlock == null || block.FatBlock.Name == null)
                    continue;

                var functionalBlock = block.FatBlock as IMyFunctionalBlock;
                if (functionalBlock == null)
                    continue;

                if (functionalBlock.Enabled)
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.Green);
                }
                else
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.Red);
                }
            }
        }

        private void FunctionalFilter(List<IMySlimBlock> blocks, IMyCubeGrid grid)
        {
            foreach (var block in blocks)
            {
                if (block.FatBlock == null || block.FatBlock.Name == null)
                    continue;

                var functionalBlock = block.FatBlock as IMyFunctionalBlock;
                if (functionalBlock == null)
                    continue;

                if (functionalBlock.IsWorking)
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.Green);
                }
                else if (!functionalBlock.IsWorking && functionalBlock.IsFunctional)
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.LightBlue);
                }
                else if (!functionalBlock.IsWorking && !functionalBlock.IsFunctional)
                {
                    if (!HighlightedBlocksPerGrid[grid].ContainsKey(block))
                        HighlightedBlocksPerGrid[grid][block] = DiagnosticType;

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, HighlightIntensity, -1, Color.Red);
                }
            }
        }
        #endregion

        #region Utility
        private Dictionary<TEnum, bool> GetOrCreateDictionary<TEnum>(Dictionary<long, Dictionary<TEnum, bool>> dictionary, long entityId)
        {
            Dictionary<TEnum, bool> nested;
            if (!dictionary.TryGetValue(entityId, out nested))
            {
                nested = new Dictionary<TEnum, bool>();
                dictionary[entityId] = nested;
            }
            return nested;
        }
        #endregion
    }
}
