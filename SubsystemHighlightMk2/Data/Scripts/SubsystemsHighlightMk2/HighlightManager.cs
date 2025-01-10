using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.Game;
using static Sandbox.ModAPI.MyAPIGateway;
using static VRageRender.MyBillboard;
using Sandbox.ModAPI;
using CoreSystems.Api;
using System.Xml.Serialization;
using Sandbox.Definitions;
using VRage.Collections;
using Sandbox.Common.ObjectBuilders;
using VRage.Utils;

namespace StarCore.Highlights
{
    public class HighlightManager
    {
        public static HighlightManager I = new HighlightManager();
        private WcApi CoreSysAPI = new WcApi();
        private bool CoreAPILoaded = false;

        public class HighlightFilter
        {
            public string Name;
            public List<string> Subtypes;

            public HighlightFilter(string name, List<string> subtypes)
            {
                Name = name;
                Subtypes = subtypes;
            }

            public override int GetHashCode()
            {
                return (Name?.GetHashCode() ?? 0) ^ (Subtypes?.GetHashCode() ?? 0);
            }

            public override bool Equals(object obj)
            {
                if (obj == null || !(obj is HighlightFilter))
                    return false;

                var other = (HighlightFilter)obj;
                return (Name == other.Name && Subtypes == other.Subtypes);
            }
        }
   
        private float Transparency;
        private int HighlightIntensity;

        DictionaryValuesReader<MyDefinitionId, MyDefinitionBase> allDefs;
        public List<MyDefinitionBase> cachedDefs;
        public List<MyDefinitionBase> blockDefs = new List<MyDefinitionBase>();

        public HighlightFilter CurrentFilter;
        public bool InitDefs = false;

        public int TotalCount => Filters.Count;
        public List<HighlightFilter> Filters = new List<HighlightFilter>
        {
            new HighlightFilter("Conveyor", new List<string>()),
            new HighlightFilter("Thruster", new List<string>()),
            new HighlightFilter("Steering", new List<string>()),
            new HighlightFilter("Power", new List<string>()),
            new HighlightFilter("Weapons", new List<string>()),
            new HighlightFilter("HeavyArmor", new List<string>()),
            new HighlightFilter("LightArmor", new List<string>()),
        };

        public Dictionary<long, Dictionary<HighlightFilter, bool>> ActiveHighlights = new Dictionary<long, Dictionary<HighlightFilter, bool>>();

        public Dictionary<long, IMyCubeGrid> ActiveGrids = new Dictionary<long, IMyCubeGrid>();
        public Dictionary<long, List<IMySlimBlock>> ActiveGridBlockLists = new Dictionary<long, List<IMySlimBlock>>();
        public Dictionary<IMyCubeGrid, Dictionary<IMySlimBlock, string>> HighlightedBlocksPerGrid = new Dictionary<IMyCubeGrid, Dictionary<IMySlimBlock, string>> ();

        private Dictionary<string, Color> _typeColors = new Dictionary<string, Color>
        {
            { "Conveyor", new Color(255, 255, 0, 255) },   // Yellow
            { "Thruster", new Color(0, 128, 0, 255) },     // Green
            { "Steering", new Color(75, 0, 130, 255) },    // Indigo
            { "Power", new Color(135, 206, 235, 255) },    // Sky Blue
            { "Weapons", new Color(255, 165, 0, 255) },     // Orange
            { "HeavyArmor", new Color(199, 21, 133, 255) * 2 },// Purplish
            { "LightArmor", new Color(0, 255, 0, 255) },     // Green?
        };

        #region Update Methods
        public void Init()
        {
            I = this;

            if (CoreSysAPI.IsReady)
            {
                CoreSysAPI.Load();
                CoreAPILoaded = true;
            }
            

            Transparency = -0.5f;
            HighlightIntensity = 3;

            if (!Utilities.FileExistsInGlobalStorage("HighlightColors.xml"))
            {
                SaveColors("HighlightColors.xml");
            }

            LoadColors("HighlightColors.xml");

            Session.DamageSystem.RegisterBeforeDamageHandler(0, HandleDamageEvent);

            allDefs = MyDefinitionManager.Static.GetAllDefinitions();
            cachedDefs = allDefs.ToList();

            foreach (var def in cachedDefs)
            {
                if (def as MyCubeBlockDefinition != null)
                {
                    blockDefs.Add(def);
                }
            }

            foreach (var filter in Filters)
            {
                foreach (var def in blockDefs)
                {
                    if (MatchDefToDefaultFilter(def, filter.Name) && filter.Subtypes.Contains(def.Id.SubtypeName) == false)
                    {
                        filter.Subtypes.Add(GetBlockSubtypeOrFallback(def));
                    }
                }
            }

            if (CoreAPILoaded)
            {
                HashSet<MyDefinitionId> defs = new HashSet<MyDefinitionId>();
                CoreSysAPI.GetAllCoreWeapons(defs);

                foreach (var def in defs)
                {
                    Filters[5].Subtypes.Add(def.SubtypeName);
                }
            }

            MyLog.Default.WriteLine(string.Join(", ", Filters[5].Subtypes));

            CurrentFilter = Filters.First();

            Filters.Add(new HighlightFilter("CustomTest01", new List<string> { "LargeBlockLargeIndustrialContainer" }));
            _typeColors.Add("CustomTest01", Color.Maroon);
        }

        public void Update()
        {
            
        }

        public void Draw()
        {
            if (ActiveGrids.Any() && HighlightedBlocksPerGrid.Any())
            {
                foreach (IMyCubeGrid grid in ActiveGrids.Values)
                {
                    if (grid == null || grid.MarkedForClose || !HighlightedBlocksPerGrid.ContainsKey(grid))
                        continue;

                    Dictionary<IMySlimBlock, string> slimBlockDict = HighlightedBlocksPerGrid[grid];

                    foreach (var entry in slimBlockDict.ToList())
                    {
                        IMySlimBlock slimBlock = entry.Key;

                        if (slimBlock == null || slimBlock.CubeGrid == null || slimBlock.CubeGrid.MarkedForClose)
                        {
                            slimBlockDict.Remove(slimBlock);
                            continue;
                        }

                        HighlightFilter highlightFilter;
                        TryGetFilterByName(entry.Value, out highlightFilter);

                        if (highlightFilter.Name != "HeavyArmor" && highlightFilter.Name != "LightArmor")
                            continue;

                        if (slimBlock.Dithering != 1.0f)
                            slimBlock.Dithering = 1.0f;

                        Vector3D blockPosition;
                        Matrix blockRotation;

                        slimBlock.ComputeWorldCenter(out blockPosition);
                        slimBlock.Orientation.GetMatrix(out blockRotation);

                        MatrixD gridRotationMatrix = slimBlock.CubeGrid.WorldMatrix;
                        gridRotationMatrix.Translation = Vector3D.Zero;
                        blockRotation *= gridRotationMatrix;
                        MatrixD blockWorldMatrix = MatrixD.CreateWorld(blockPosition, blockRotation.Forward, blockRotation.Up);

                        float unit = slimBlock.CubeGrid.GridSize * 0.5f;
                        Vector3 halfExtents = new Vector3((float)unit, (float)unit, (float)unit);
                        BoundingBoxD box = new BoundingBoxD(-halfExtents, halfExtents);
                        Color c = _typeColors[highlightFilter.Name];

                        MySimpleObjectDraw.DrawTransparentBox(ref blockWorldMatrix, ref box, ref c, MySimpleObjectRasterizer.Solid, 1, 0.001f, null, null, true, -1, BlendTypeEnum.AdditiveTop, 1000f);
                    }
                }
            }
        }

        public void Unload()
        {
            I = null;

            if (CoreSysAPI.IsReady)
            {
                CoreSysAPI.Unload();
            }

            Filters.Clear();
            ActiveHighlights.Clear();
        }

        private void HandleDamageEvent(object target, ref MyDamageInformation info)
        {
            var targetBlock = target as IMySlimBlock;

            if (targetBlock != null)
            {
                var targetID = targetBlock.CubeGrid.EntityId;
                if (ActiveGrids.ContainsKey(targetID))
                {
                    Utilities.ShowNotification("Grid Damaged! Highlights Cancelled!", 9000, "Red");
                    ResetHighlights(targetBlock.CubeGrid.EntityId);
                }
            }
        }

        private void HandleGridSplit(IMyCubeGrid originalGrid, IMyCubeGrid newGrid)
        {
            if (originalGrid != null && newGrid != null)
            {
                var originalGridID = originalGrid.EntityId;
                if (ActiveGrids.ContainsKey(originalGridID))
                {
                    List<IMySlimBlock> originalGridBlocks = new List<IMySlimBlock>();
                    List<IMySlimBlock> newGridBlocks = new List<IMySlimBlock>();

                    originalGrid.GetBlocks(originalGridBlocks);
                    newGrid.GetBlocks(newGridBlocks);

                    Utilities.ShowNotification("Grid Split Detected! Highlights Cancelled!", 9000, "Red");

                    ResetHighlights(originalGrid.EntityId);
                    ResetHighlights(newGrid.EntityId);
                }
            }
        }

        private void HandleGridClose(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid == null)
                return;

            if (ActiveGrids.ContainsKey(grid.EntityId))
            {
                ResetHighlights(grid.EntityId);

                ActiveGrids.Remove(grid.EntityId);
                HighlightedBlocksPerGrid.Remove(grid);

                grid.OnMarkForClose -= HandleGridClose;
            }
        }
        #endregion

        #region Cycle Filters
        public void SwitchFilter(int direction, ref int currentIndex)
        {

            currentIndex += direction;

            if (currentIndex >= TotalCount)
                currentIndex = 0;
            else if (currentIndex < 0)
                currentIndex = TotalCount - 1;

            CurrentFilter = Filters[currentIndex];
        }

        public void ApplyFilter(long entityId)
        {
            Dictionary<HighlightFilter, bool> activeHighlightsDict;
            if (!ActiveHighlights.TryGetValue(entityId, out activeHighlightsDict) || activeHighlightsDict == null)
            {
                activeHighlightsDict = new Dictionary<HighlightFilter, bool>();
                ActiveHighlights[entityId] = activeHighlightsDict;

                var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
                if (cubeGrid != null && !ActiveGridBlockLists.ContainsKey(entityId))
                {
                    List<IMySlimBlock> blockList = new List<IMySlimBlock>();
                    cubeGrid.GetBlocks(blockList);
                    ActiveGridBlockLists[entityId] = blockList;
                }
            }

            if (!activeHighlightsDict.ContainsKey(CurrentFilter) || !activeHighlightsDict[CurrentFilter])
            {
                activeHighlightsDict[CurrentFilter] = true;
                HighlightWrapper(CurrentFilter, entityId);

                Utilities.ShowNotification($"[Highlight] Applied Filter [{CurrentFilter.Name}]",
                1000, "White");
            }
        }

        public void RemoveFilter(long entityId)
        {
            Dictionary<HighlightFilter, bool> activeHighlightsDict;
            if (ActiveHighlights.TryGetValue(entityId, out activeHighlightsDict) && activeHighlightsDict.ContainsKey(CurrentFilter))
            {
                activeHighlightsDict.Remove(CurrentFilter);
                ClearHighlightWrapper(CurrentFilter, entityId);

                if (activeHighlightsDict.Count == 0)
                {
                    ActiveHighlights.Remove(entityId);
                    ResetHighlights(entityId);

                    Utilities.ShowNotification($"[Highlight] Removed Last Filter [{CurrentFilter.Name}]",
                    1000, "White");
                    Utilities.ShowNotification("Highlights Reset!", 1000, "Green");
                }
                else
                    Utilities.ShowNotification($"[Highlight] Removed Filter [{CurrentFilter.Name}]",
                    1000, "White");
                
            }
        }
        #endregion

        #region Wrappers
        private void HighlightWrapper(HighlightFilter filter, long entityId)
        {
            if (entityId == 0)
                return;

            var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            if (!ActiveGrids.ContainsKey(entityId))
            {
                cubeGrid.OnGridSplit += HandleGridSplit;
                cubeGrid.OnMarkForClose += HandleGridClose;
                ActiveGrids[entityId] = cubeGrid;
            }

            Dictionary<IMySlimBlock, string> highlightedBlocksPerGridDict;
            if (!HighlightedBlocksPerGrid.TryGetValue(cubeGrid, out highlightedBlocksPerGridDict) || highlightedBlocksPerGridDict == null)
            {
                highlightedBlocksPerGridDict = new Dictionary<IMySlimBlock, string>();
                HighlightedBlocksPerGrid[cubeGrid] = highlightedBlocksPerGridDict;
            }

            HandleHighlights(cubeGrid, filter.Subtypes, _typeColors[filter.Name],ActiveGridBlockLists[entityId]);
        }

        private void ClearHighlightWrapper(HighlightFilter filter, long entityId)
        {
            if (entityId == 0)
                return;

            var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            if (!ActiveGrids.ContainsKey(entityId))
            {
                cubeGrid.OnGridSplit += HandleGridSplit;
                cubeGrid.OnMarkForClose += HandleGridClose;
                ActiveGrids[entityId] = cubeGrid;
            }

            Dictionary<IMySlimBlock, string> highlightedBlocksPerGridDict;
            if (!HighlightedBlocksPerGrid.TryGetValue(cubeGrid, out highlightedBlocksPerGridDict) || highlightedBlocksPerGridDict == null)
            {
                highlightedBlocksPerGridDict = new Dictionary<IMySlimBlock, string>();
                HighlightedBlocksPerGrid[cubeGrid] = highlightedBlocksPerGridDict;
            }

            ClearHighlights(cubeGrid, filter.Subtypes, ActiveGridBlockLists[entityId]);
        }

        public void ResetHighlights(long entityId)
        {
            var cubeGrid = MyAPIGateway.Entities.GetEntityById(entityId) as IMyCubeGrid;
            if (cubeGrid == null)
                return;

            cubeGrid.OnGridSplit -= HandleGridSplit;
            cubeGrid.OnMarkForClose -= HandleGridClose;

            if (ActiveGridBlockLists.ContainsKey(entityId) && ActiveGridBlockLists?[entityId] != null)
            {
                foreach (var block in ActiveGridBlockLists[cubeGrid.EntityId])
                {
                    if (HighlightedBlocksPerGrid[cubeGrid].ContainsKey(block))
                        HighlightedBlocksPerGrid[cubeGrid].Remove(block);

                    if (block.FatBlock != null)
                        MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, -1);

                    if (block.Dithering != 0f)
                        block.Dithering = 0f;
                }
            }     

            Dictionary<HighlightFilter, bool> highlightDict;
            if (ActiveHighlights.TryGetValue(entityId, out highlightDict))
                ActiveHighlights.Remove(entityId);

            if (ActiveGrids.ContainsKey(entityId))
                ActiveGrids.Remove(entityId);

            if (ActiveGridBlockLists.ContainsKey(entityId))
                ActiveGridBlockLists.Remove(entityId);
        }
        #endregion

        #region Handle Highlights
        private void HandleHighlights(IMyCubeGrid cubeGrid, List<string> subTypes, Color color, List<IMySlimBlock> blockList)
        {
            bool hasAnyMatches = subTypes.Any(subtype => blockList.Any(b => BlockMatchesSubtype(b, subtype)));

            if (!hasAnyMatches)
            {
                Utilities.ShowNotification($"No Blocks Found for Filter {CurrentFilter.Name}!", 1000, "Red");
                RemoveFilter(cubeGrid.EntityId);
                return;
            }

            foreach (var block in blockList)
            {
                bool matched = false;
                foreach (var subType in subTypes)
                {
                    if (BlockMatchesSubtype(block, subType))
                    {
                        matched = true;
                        if (!HighlightedBlocksPerGrid[cubeGrid].ContainsKey(block))
                            HighlightedBlocksPerGrid[cubeGrid][block] = CurrentFilter.Name;

                        if (block.FatBlock != null)
                            MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, 3, -1, color);
                        break;
                    }
                    else
                        block.Dithering = -0.5f;
                }          
            }
        }

        private void ClearHighlights(IMyCubeGrid cubeGrid, List<string> subTypes, List<IMySlimBlock> blockList)
        {
            foreach (var block in blockList)
            {
                if (HighlightedBlocksPerGrid[cubeGrid].ContainsKey(block) && HighlightedBlocksPerGrid[cubeGrid][block] == CurrentFilter.Name)
                {
                    foreach (var subType in subTypes)
                    {
                        if (BlockMatchesSubtype(block, subType))
                        {
                            HighlightedBlocksPerGrid[cubeGrid].Remove(block);
                            if (block.FatBlock != null)
                            {
                                MyVisualScriptLogicProvider.SetHighlightLocal(block.FatBlock.Name, -1);
                            }
                            else
                                continue;
                        }
                    }
                }
            }
        }
        #endregion

        #region Utility
        public bool MatchDefToDefaultFilter(MyDefinitionBase def, string filterName)
        {
            switch (filterName)
            {
                case "Conveyor":
                    return def.Id.TypeId == typeof(MyObjectBuilder_Conveyor)
                        || def.Id.TypeId == typeof(MyObjectBuilder_ConveyorLine)
                        || def.Id.TypeId == typeof(MyObjectBuilder_ConveyorConnector);
                case "Thruster":
                    return def.Id.TypeId == typeof(MyObjectBuilder_Thrust);
                case "Steering":
                    return def.Id.TypeId == typeof(MyObjectBuilder_Gyro);
                case "Power":
                    return def.Id.TypeId == typeof(MyObjectBuilder_Reactor)
                    || def.Id.TypeId == typeof(MyObjectBuilder_BatteryBlock)
                    || def.Id.TypeId == typeof(MyObjectBuilder_SolarPanel)
                    || def.Id.TypeId == typeof(MyObjectBuilder_WindTurbine)
                    || def.Id.TypeId == typeof(MyObjectBuilder_HydrogenEngine);
                case "Weapons":
                    return def.Id.TypeId == typeof(MyObjectBuilder_LargeGatlingTurret)
                    || def.Id.TypeId == typeof(MyObjectBuilder_LargeMissileTurret)
                    || def.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret);
                case "HeavyArmor":
                    return def.Id.TypeId == typeof(MyObjectBuilder_CubeBlock)
                        && def.Id.SubtypeName.ToLower().Contains("heavy")
                        && !def.Id.SubtypeName.ToLower().Contains("panel");
                case "LightArmor":
                    return def.Id.TypeId == typeof(MyObjectBuilder_CubeBlock)
                        && def.Id.SubtypeName.ToLower().Contains("armor")
                        && !def.Id.SubtypeName.ToLower().Contains("heavy")
                        && !def.Id.SubtypeName.ToLower().Contains("panel");
            }

            return false;

        }

        public bool BlockMatchesSubtype(IMySlimBlock block, string customType = null)
        {
            return GetBlockSubtypeOrFallback(block.BlockDefinition).ToLower() == customType.ToLower();       
        }

        private string GetBlockSubtypeOrFallback(MyDefinitionBase def)
        {
            if (!string.IsNullOrEmpty(def.Id.SubtypeName))
                return def.Id.SubtypeName;
            else
                return def.Id.TypeId.ToString();
        }

        public bool TryGetFilterByName(string name, out HighlightFilter filter)
        {
            filter = Filters.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return (filter != null);
        }
        #endregion

        #region Custom Colors Save/Load
        public void LoadColors(string filePath)
        {
            if (!Utilities.FileExistsInGlobalStorage(filePath))
                return;

            using (var reader = Utilities.ReadFileInGlobalStorage(filePath))
            {
                var xml = reader.ReadToEnd();
                var serializableData = Utilities.SerializeFromXML<List<SerializableColor>>(xml);

                foreach (var entry in serializableData)
                {
                    HighlightFilter type;
                    if (TryGetFilterByName(entry.Name, out type) && entry.Rgba.Length == 4)
                    {
                        _typeColors[type.Name] = new Color(entry.Rgba[0], entry.Rgba[1], entry.Rgba[2], entry.Rgba[3]);
                    }
                }
            }
        }

        public void SaveColors(string filePath)
        {
            var colorData = _typeColors.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => new[] { kvp.Value.R, kvp.Value.G, kvp.Value.B, kvp.Value.A }
            );

            var serializableData = colorData.Select(kvp => new SerializableColor
            {
                Name = kvp.Key,
                Rgba = kvp.Value.Select(b => (int)b).ToArray()
            }).ToList();

            using (var writer = Utilities.WriteFileInGlobalStorage(filePath))
            {
                var xml = Utilities.SerializeToXML(serializableData);
                writer.Write(xml);
            }
        }

        public class SerializableColor
        {
            [XmlElement("FilterName")]
            public string Name { get; set; }

            [XmlArray("ColorValues")]
            [XmlArrayItem("Value")]
            public int[] Rgba { get; set; }
        }
        #endregion
    }
}
