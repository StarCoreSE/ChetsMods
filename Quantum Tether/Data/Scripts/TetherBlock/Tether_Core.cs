using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using InventoryTether.Particle;
using static VRageRender.MyBillboard;
using InventoryTether.Config;
using InventoryTether.Networking.Custom;
using ProtoBuf;
using VRage.Collections;
using static VRage.Game.MyObjectBuilder_BehaviorTreeDecoratorNode;

namespace InventoryTether
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether", "Quantum_Tether_Small", "Quantum_Tether_Medium")]
    public class InventoryTether : MyGameLogicComponent
    {
        private IMyCollector Block;
        private readonly bool IsServer = MyAPIGateway.Session.IsServer;
        private bool IsDedicated = MyAPIGateway.Utilities.IsDedicated;
        private bool ClientSettingsLoaded = false;

        public readonly Guid SettingsID = new Guid("47114A7D-B546-4C05-9E6E-2DFE3449E176");
        public MyPoweredCargoContainerDefinition InventoryTetherBlockDef;
        public readonly Tether_ConfigSettings Config = new Tether_ConfigSettings();       

        #region Properties
        //Synced Values
        public bool HardCap
        {
            get { return _hardCap; }
            set
            {
                if (_hardCap != value)
                {
                    _hardCap = value;
                    SaveSettings();
                    BoolSyncPacket.SyncBoolProperty(Block.EntityId, nameof(HardCap), _hardCap);
                }
            }
        }
        public bool _hardCap;

        public float BlockRange
        {
            get { return _blockRange; }
            set
            {
                if (_blockRange != value)
                {
                    _blockRange = value;
                    SaveSettings();
                    FloatSyncPacket.SyncFloatProperty(Block.EntityId, nameof(BlockRange), _blockRange);
                }
            }
        }
        public float _blockRange;

        public Dictionary<string, ComponentData> TargetItems
        {
            get { return _targetItems; }
            set
            {
                if (_targetItems != value)
                {
                    _targetItems = value;
                    SaveSettings();
                }
            }
        }
        public Dictionary<string, ComponentData> _targetItems = new Dictionary<string, ComponentData>();

        //Client Values
        public bool ShowArea = false;
        #endregion

        private string EmissiveMaterialName = "Emissive";
        private bool EmissiveSet = false;

        public DictionaryValuesReader<MyDefinitionId, MyDefinitionBase> definitionsInSession = new DictionaryValuesReader<MyDefinitionId, MyDefinitionBase>();
        public StringBuilder TempStockAmount = new StringBuilder();
        public List<MyTerminalControlListBoxItem> TempItemsToAdd = new List<MyTerminalControlListBoxItem>();
        public List<MyTerminalControlListBoxItem> TempItemsToRemove = new List<MyTerminalControlListBoxItem>();

        private MyResourceSinkComponent Sink = null;

        private IMyHudNotification NotifStatus = null;
        private IMyHudNotification NotifNoneStatus = null;

        #region Overrides
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Block = (IMyCollector)Entity;

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            if (Block?.CubeGrid?.Physics == null)
                return;

            Config.Load();

            definitionsInSession = MyDefinitionManager.Static.GetAllDefinitions();
            InventoryTetherControls.DoOnce(ModContext);

            Sink = Block.Components.Get<MyResourceSinkComponent>();
            Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, RequiredInput);

            InventoryTetherBlockDef = (MyPoweredCargoContainerDefinition)Block.SlimBlock.BlockDefinition;

            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();

            CheckShowArea();
            SetEmissives();

            if (!IsServer)
                return;

            if (MyAPIGateway.Session.GameplayFrameCounter % 60 == 0)
            {
                if (Block.IsWorking)
                {
                    Sink.Update();

                    ScanForTargets();
                }              
            }         
        }

        public override void UpdateAfterSimulation100()
        {
            base.UpdateAfterSimulation100();

            if (IsDedicated)
            {
                NeedsUpdate &= ~MyEntityUpdateEnum.EACH_100TH_FRAME;
                return;
            }
            if (!LoadSettings())
            {
                if (Block.Enabled)
                    Block.Enabled = false;

                BlockRange = IsSmallGrid() ? Config.Small_MaxBlockRange / 2 : Config.MaxBlockRange / 2; ;
            }

            ClientSettingsLoaded = true;
            NeedsUpdate &= ~MyEntityUpdateEnum.EACH_100TH_FRAME;
            return;
        }

        public override void Close()
        {
            try
            {
                if (Block == null)
                    return;

                Block = null;
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification($"{e}", 5000, "Red");
            }
        }
        #endregion

        #region Utilities
        public static T GetLogic<T>(long entityId) where T : MyGameLogicComponent
        {
            IMyEntity targetEntity = MyAPIGateway.Entities.GetEntityById(entityId);
            if (targetEntity == null)
            {
                Log.Info("GetLogic failed: Entity not found. Entity ID: " + entityId);
                return null;
            }

            IMyTerminalBlock targetBlock = targetEntity as IMyTerminalBlock;
            if (targetBlock == null)
            {
                Log.Info("GetLogic failed: Target entity is not a terminal block. Entity ID: " + entityId);
                return null;
            }

            var logic = targetBlock.GameLogic?.GetAs<T>();
            if (logic == null)
            {
                Log.Info("GetLogic failed: Logic component not found. Entity ID: " + entityId);
            }

            return logic;
        }

        private float RequiredInput()
        {
            if (!Block.IsWorking)
                return 0f;

            float minPower = IsSmallGrid() ? Config.Small_MinimumPowerRequirement : Config.MinimumPowerRequirement;
            float maxPower = IsSmallGrid() ? Config.Small_MaximumPowerRequirement : Config.MaximumPowerRequirement;

            float blockRange = BlockRange;

            if (blockRange <= (IsSmallGrid() ? Config.Small_MinBlockRange : Config.MinBlockRange))
                return minPower;

            float ratio = blockRange / (IsSmallGrid() ? Config.Small_MaxBlockRange : Config.MaxBlockRange);
            return maxPower * ratio;
        }

        private void SetStatus(string text, int aliveTime = 300, string font = MyFontEnum.Green)
        {
            if (NotifStatus == null)
                NotifStatus = MyAPIGateway.Utilities.CreateNotification("", aliveTime, font);

            NotifStatus.Hide();
            NotifStatus.Font = font;
            NotifStatus.Text = text;
            NotifStatus.AliveTime = aliveTime;
            NotifStatus.Show();
        }

        private void SetNoneStatus(string text, int aliveTime = 300, string font = MyFontEnum.Green)
        {
            if (NotifNoneStatus == null)
                NotifNoneStatus = MyAPIGateway.Utilities.CreateNotification("", aliveTime, font);

            NotifNoneStatus.Hide();
            NotifNoneStatus.Font = font;
            NotifNoneStatus.Text = text;
            NotifNoneStatus.AliveTime = aliveTime;
            NotifNoneStatus.Show();
        }

        private List<IMyCharacter> NearPlayers()
        {
            List<IMyCharacter> nearbyPlayers = new List<IMyCharacter>();
            List<IMyPlayer> actualPlayers = new List<IMyPlayer>();

            if (Block == null) 
                return nearbyPlayers;

            var entities = new List<MyEntity>();
            var bound = new BoundingSphereD(Block.GetPosition(), BlockRange / 2);
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref bound, entities);
            MyAPIGateway.Players.GetPlayers(actualPlayers);

            foreach (var entity in entities)
            {
                IMyCharacter player = entity as IMyCharacter;
                if (player != null && player.IsPlayer)
                {
                    if (bound.Contains(player.GetPosition()) != ContainmentType.Disjoint)
                    {
                        foreach (IMyPlayer realplayer in actualPlayers)
                        {
                            if (realplayer.Character == player)
                            {
                                var playerRelation = Block.GetUserRelationToOwner(realplayer.IdentityId);

                                if (playerRelation.IsFriendly())
                                {
                                    Log.Info($"Valid Player Detected: {player.DisplayName}");
                                    nearbyPlayers.Add(player);
                                }
                                else
                                    continue;
                            }
                        }
                    }
                }
            }

            return nearbyPlayers;
        }

        public bool IsSmallGrid()
        {
            return Block.CubeGrid.GridSizeEnum == MyCubeSize.Small || 
                (Block.BlockDefinition.SubtypeId == "Quantum_Tether_Small" ||
                Block.BlockDefinition.SubtypeId == "Quantum_Tether_Medium");
        }

        private void CheckShowArea()
        {
            if (!ShowArea || MyAPIGateway.Utilities.IsDedicated)
                return;

            Vector3 pos = Block.GetPosition();
            var matrix = MatrixD.CreateWorld(pos);
            int wireDivRatio = 360 / 15;

            Color color = Block.IsWorking ? Color.LightGreen : Color.Yellow;
            if (!Block.Enabled)
            {
                color = Color.DarkGray;
            }

            float radius = BlockRange / 2;
            float lineThickness = 0.15f + ((radius - 2.5f) / ((Config.MaxBlockRange / 2) - (Config.MinBlockRange / 2))) * (1.5f - 0.15f);

            MySimpleObjectDraw.DrawTransparentSphere(ref matrix, BlockRange / 2, ref color, MySimpleObjectRasterizer.Wireframe, wireDivRatio, null, MyStringId.GetOrCompute("WeaponLaserIgnoreDepth"), lineThickness, -1, null, BlendTypeEnum.SDR, 1);
        }

        private void SetEmissives()
        {
            if (Block.IsWorking)
            {
                if (!EmissiveSet)
                {
                    Block.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.DeepSkyBlue, 0.75f);
                    EmissiveSet = true;
                }
                // No action needed for EmissiveSet = true, just return
                return;
            }

            if (!Block.Enabled)
            {
                Block.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.Black, 100f);
                EmissiveSet = false;
            }
            else // InventoryTetherBlock.Enabled == true
            {
                Block.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.Yellow, 0.75f);
                EmissiveSet = false;
            }
        }

        private MyFixedPoint ClampMyFixedPoint(MyFixedPoint value, MyFixedPoint min, MyFixedPoint max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // Garnular Dict Sync
        public void AddOrUpdateTargetItem(string key, ComponentData componentData)
        {
            componentData.StockAmount = ClampMyFixedPoint(componentData.StockAmount, (MyFixedPoint)Config.MinStockAmount, (MyFixedPoint)Config.MaxStockAmount);

            if (!TargetItems.ContainsKey(key))
            {
                TargetItems.Add(key, componentData);
            }
            else
            {
                TargetItems[key] = componentData;
            }

            SaveSettings();
            DictSyncPacket.SyncDictionaryEntry(Block.EntityId, nameof(TargetItems), key, componentData);
        }

        public void RemoveTargetItem(string key)
        {
            if (TargetItems.ContainsKey(key))
            {
                TargetItems.Remove(key);

                SaveSettings();
                DictSyncPacket.SyncDictionaryEntry(Block.EntityId, nameof(TargetItems), key, null);
            }
        }

        private void SyncTargetItemsToClients()
        {
            foreach (var item in TargetItems)
            {
                DictSyncPacket.SyncDictionaryEntry(Block.EntityId, nameof(TargetItems), item.Key, item.Value);
            }
        }
        #endregion

        #region Settings
        bool LoadSettings()
        {
            if (Block.Storage == null)
            {
                Log.Info($"LoadSettings: Block storage is null for {Block.EntityId}");
                return false;
            }

            string rawData;
            if (!Block.Storage.TryGetValue(SettingsID, out rawData))
            {
                Log.Info($"LoadSettings: No data found for {Block.EntityId}");
                return false;
            }

            try
            {
                var loadedSettings = MyAPIGateway.Utilities.SerializeFromBinary<RepairSettings>(Convert.FromBase64String(rawData));

                if (loadedSettings != null)
                {
                    Log.Info($"LoadSettings: Successfully loaded settings for {Block.EntityId}");
                    Log.Info($"Loaded values: HardCap={loadedSettings.Stored_HardCap}, BlockRange={loadedSettings.Stored_BlockRange}, TargetItems={loadedSettings.Stored_TargetItems.Count}");

                    HardCap = loadedSettings.Stored_HardCap;
                    BlockRange = loadedSettings.Stored_BlockRange;
                    TargetItems = new Dictionary<string, ComponentData>(loadedSettings.Stored_TargetItems);

                    SyncTargetItemsToClients();

                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error loading settings for {Block.EntityId}!\n{e}");
            }

            return false;
        }

        void SaveSettings()
        {
            if (Block == null)
            {
                Log.Info("SaveSettings called but Block is null.");
                return;
            }

            try
            {
                if (MyAPIGateway.Utilities == null)
                    throw new NullReferenceException($"MyAPIGateway.Utilities == null; entId={Entity?.EntityId};");

                if (Block.Storage == null)
                {
                    Log.Info($"Creating new storage for {Block.EntityId}");
                    Block.Storage = new MyModStorageComponent();
                }

                var settings = new RepairSettings
                {
                    Stored_HardCap = HardCap,
                    Stored_BlockRange = BlockRange,
                    Stored_TargetItems = new Dictionary<string, ComponentData>(TargetItems)
                };

                string serializedData = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(settings));
                Block.Storage.SetValue(SettingsID, serializedData);
                Log.Info($"SaveSettings: Successfully saved settings for {Block.EntityId}");
                Log.Info($"Saved values: HardCap={HardCap}, BlockRange={BlockRange}, TargetItems={TargetItems.Count}");
            }
            catch (Exception e)
            {
                Log.Error($"Error saving settings for {Block.EntityId}!\n{e}");
            }
        }
        #endregion

        #region Main
        private void ScanForTargets()
        {
            if (!Block.Enabled) return;

            foreach (var player in NearPlayers())
            {
                Log.Info($"Handling Inventory for: {player.DisplayName}");
                HandleInventory(
                    player.GetInventory() as MyInventory, player,
                    (p, subtype, amount) => AddItemToInventory(p, subtype, amount, t => t.GetInventory() as MyInventory),
                    (p, subtype, amount) => RemoveItemFromInventory(p, subtype, amount, t => t.GetInventory() as MyInventory)
                );
            }

            /*foreach (var grid in NearGrids())
            {
                var gridGroup = grid.GetGridGroup(GridLinkTypeEnum.Logical);
                var subgroups = new List<IMyCubeGrid>();
                gridGroup.GetGrids(subgroups);

                foreach (var groupGrid in subgroups)
                {
                    var fatblocks = ((MyCubeGrid)groupGrid).GetFatBlocks();
                    foreach (var block in fatblocks)
                    {
                        IMyCubeBlock castedBlock = block as IMyCubeBlock;

                        if (castedBlock.BlockDefinition.SubtypeName == "GridTetherReciever")
                        {
                            HandleInventory(block.GetInventory(), block,
                                (b, subtype, amount) => AddItemToInventory(b, subtype, amount, t => t.GetInventory() as MyInventory),
                                (b, subtype, amount) => RemoveItemFromInventory(b, subtype, amount, t => t.GetInventory() as MyInventory)
                            );
                        }
                    }
                }
            }*/
        }

        private void HandleInventory<T>(MyInventory inventory, T target, Action<T, string, MyFixedPoint> addItemMethod, Action<T, string, MyFixedPoint> removeItemMethod)
        {
            if (inventory == null)
                return;

            if (TargetItems.Count == 0 && HardCap)
            {
                foreach (var item in inventory.GetItems())
                {
                    var component = item.Content as MyObjectBuilder_Component;
                    if (component != null && item.Amount > 0)
                    {
                        removeItemMethod(target, component.SubtypeName, item.Amount);
                        break;
                    }
                }
                return;
            }

            foreach (var subtype in TargetItems.Keys)
            {
                bool subtypeFound = false;
                bool itemStocked = false;
                bool itemOverStocked = false;
                MyFixedPoint itemAmount = 0;
                MyFixedPoint overstockItemAmount = 0;

                ComponentData componentData;
                if (!TargetItems.TryGetValue(subtype, out componentData))
                    continue;

                foreach (var item in inventory.GetItems())
                {
                    var component = item.Content as MyObjectBuilder_Component;
                    if (component == null || component.SubtypeName != subtype)
                        continue;

                    if (item.Amount >= componentData.StockAmount)
                    {
                        itemStocked = true;

                        if (item.Amount > componentData.StockAmount && HardCap)
                        {
                            itemOverStocked = true;
                            overstockItemAmount = item.Amount - componentData.StockAmount;
                        }
                        break;
                    }
                    else if (item.Amount < componentData.StockAmount)
                    {
                        itemAmount = item.Amount;
                        break;
                    }
                }

                if (!itemStocked)
                {                    
                    addItemMethod(target, subtype, itemAmount);
                }
                else if (itemOverStocked && HardCap)
                {
                    removeItemMethod(target, subtype, overstockItemAmount);
                }
            }
        }

        private void AddItemToInventory<T>(T target, string subtype, MyFixedPoint currentItemAmount, Func<T, MyInventory> getInventory)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));
            if (componentDefinition == null)
                return;

            ComponentData componentData;
            if (!TargetItems.TryGetValue(subtype, out componentData))
                return;

            MyFixedPoint neededItemsNumber = componentData.StockAmount - currentItemAmount;
            if (neededItemsNumber <= 0)
                return;

            bool invDisconnect = false;
            if (!TetherGridHasItem(subtype, neededItemsNumber, out invDisconnect))
            {
                return;
            }

            if (RemoveItemFromTetherGrid(subtype, neededItemsNumber, true))
            {
                var inventory = getInventory(target);
                if (inventory.CanItemsBeAdded(neededItemsNumber, componentDefinition.Id))
                {
                    var inventoryItem = new MyObjectBuilder_InventoryItem()
                    {
                        Amount = neededItemsNumber,
                        Content = new MyObjectBuilder_Component() { SubtypeName = subtype },
                    };

                    inventory.AddItems(inventoryItem.Amount, inventoryItem.Content);
                }
            }
        }

        private void RemoveItemFromInventory<T>(T target, string subtype, MyFixedPoint overstockItemAmount, Func<T, MyInventory> getInventory)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));
            if (componentDefinition == null || !TetherBlockHasSpace(subtype, overstockItemAmount))
                return;

            var targetInventory = getInventory(target);
            var blockInventory = Block.GetInventory();

            ComponentData componentData = null;
            TargetItems.TryGetValue(subtype, out componentData);

            if (targetInventory.ContainItems(overstockItemAmount, componentDefinition.Id))
            {
                var inventoryItem = new MyObjectBuilder_InventoryItem()
                {
                    Amount = overstockItemAmount,
                    Content = new MyObjectBuilder_Component() { SubtypeName = subtype },
                };

                targetInventory.RemoveItemsOfType(inventoryItem.Amount, componentDefinition.Id);
                blockInventory.AddItems(inventoryItem.Amount, inventoryItem.Content);
            }
            else
            {
                Log.Error($"Item [{subtype}] no longer exists in [{target}]'s Inventory");
            }
        }

        private bool TetherGridHasItem(string subtype, MyFixedPoint neededItemsNumber, out bool invDisconnect)
        {
            MyCubeGrid tetherGrid = (MyCubeGrid)Block.CubeGrid;
            var gridGroup = tetherGrid.GetGridGroup(GridLinkTypeEnum.Logical);
            var subgroups = new List<IMyCubeGrid>();
            gridGroup.GetGrids(subgroups);

            foreach (var grid in subgroups)
            {
                foreach (var block in ((MyCubeGrid)grid).GetFatBlocks())
                {
                    var blockInventory = block.GetInventory();
                    if (blockInventory == null) continue;

                    foreach (var item in blockInventory.GetItems())
                    {
                        var component = item.Content as MyObjectBuilder_Component;
                        if (component == null || component.SubtypeName != subtype || item.Amount < neededItemsNumber) continue;

                        if (Block.GetInventory().IsConnectedTo(blockInventory))
                        {
                            invDisconnect = false;
                            return true;
                        }
                        else
                        {
                            invDisconnect = true;
                            return false;
                        }
                    }
                }
            }

            invDisconnect = false;
            return false;
        }

        private bool TetherBlockHasSpace(string subtype, MyFixedPoint overstockItemAmount)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));
            return componentDefinition != null && Block.GetInventory().CanItemsBeAdded(overstockItemAmount, componentDefinition.Id);
        }

        private bool RemoveItemFromTetherGrid(string subtype, MyFixedPoint neededItemsNumber, bool gridHasComp)
        {
            MyCubeGrid tetherGrid = (MyCubeGrid)Block.CubeGrid;
            var gridGroup = tetherGrid.GetGridGroup(GridLinkTypeEnum.Logical);
            var subgroups = new List<IMyCubeGrid>();
            gridGroup.GetGrids(subgroups);

            if (gridHasComp)
            {
                foreach (var grid in subgroups)
                {
                    foreach (var block in ((MyCubeGrid)grid).GetFatBlocks())
                    {
                        var blockInventory = block.GetInventory();
                        if (blockInventory == null) continue;

                        foreach (var item in blockInventory.GetItems())
                        {
                            var component = item.Content as MyObjectBuilder_Component;
                            if (component != null && component.SubtypeName == subtype)
                            {
                                blockInventory.RemoveItemsOfType(neededItemsNumber, item.Content);
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
        #endregion
    }

    [ProtoContract]
    public class ComponentData
    {
        [ProtoMember(61)]
        public string DisplayNameText { get; set; }

        [ProtoMember(62)]
        public MyFixedPoint StockAmount { get; set; }

        public ComponentData() { }

        public ComponentData(string displayNameText, MyFixedPoint stockAmount)
        {
            DisplayNameText = displayNameText;
            StockAmount = stockAmount;
        }
    }

    [ProtoContract]
    public class RepairSettings
    {
        [ProtoMember(51)]
        public bool Stored_HardCap { get; set; }

        [ProtoMember(52)]
        public float Stored_BlockRange { get; set; }

        [ProtoMember(53)]
        public Dictionary<string, ComponentData> Stored_TargetItems { get; set; } = new Dictionary<string, ComponentData>();
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether", "Quantum_Tether_Small", "Quantum_Tether_Medium")]
    public class TetherRingsAnimation : MyGameLogicComponent
    {
        private const string SubpartOneName = "Torus_1"; // dummy name without the "subpart_" prefix
        private const float SubOne_DegreesPerTick = 10f; // rotation per tick in degrees (60 ticks per second)
        private const float SubOne_AccelPercentPerTick = 0.05f; // aceleration percent of "DEGREES_PER_TICK" per tick.
        private const float SubOne_DeaccelPercentPerTick = 0.0035f; // deaccleration percent of "DEGREES_PER_TICK" per tick.
        private readonly Vector3 SubOne_RotAxis = Vector3.Forward; // rotation axis for the subpart, you can do new Vector3(0.0f, 0.0f, 0.0f) for custom values
        private const float SubOne_MaxDistSq = 1000 * 1000; // player camera must be under this distance (squared) to see the subpart spinning

        private const string SubpartTwoName = "Torus_2"; // dummy name without the "subpart_" prefix
        private const float SubTwo_DegreesPerTick = 7.5f; // rotation per tick in degrees (60 ticks per second)
        private const float SubTwo_AccelPercentPerTick = 0.035f; // aceleration percent of "DEGREES_PER_TICK" per tick.
        private const float SubTwo_DeaccelPercentPerTick = 0.005f; // deaccleration percent of "DEGREES_PER_TICK" per tick.
        private readonly Vector3 SubTwo_RotAxis = Vector3.Left; // rotation axis for the subpart, you can do new Vector3(0.0f, 0.0f, 0.0f) for custom values
        private const float SubTwo_MaxDistSq = 1000 * 1000; // player camera must be under this distance (squared) to see the subpart spinning

        private const string SubpartThreeName = "Torus_3"; // dummy name without the "subpart_" prefix
        private const float SubThree_DegreesPerTick = 5f; // rotation per tick in degrees (60 ticks per second)
        private const float SubThree_AccelPercentPerTick = 0.025f; // aceleration percent of "DEGREES_PER_TICK" per tick.
        private const float SubThree_DeaccelPercentPerTick = 0.0075f; // deaccleration percent of "DEGREES_PER_TICK" per tick.
        private readonly Vector3 SubThree_RotAxis = Vector3.Up; // rotation axis for the subpart, you can do new Vector3(0.0f, 0.0f, 0.0f) for custom values
        private const float SubThree_MaxDistSq = 1000 * 1000; // player camera must be under this distance (squared) to see the subpart spinning

        private IMyFunctionalBlock block;

        private bool SubOneFirstFind = true;
        private Matrix SubOneLocalMatrix; // keeping the matrix here because subparts are being re-created on paint, resetting their orientations
        private float SubOneTargetSpeedMultiplier; // used for smooth transition

        private bool SubTwoFirstFind = true;
        private Matrix SubTwoLocalMatrix; // keeping the matrix here because subparts are being re-created on paint, resetting their orientations
        private float SubTwoTargetSpeedMultiplier; // used for smooth transition

        private bool SubThreeFoundFirst = true;
        private Matrix SubThreeLocalMatrix; // keeping the matrix here because subparts are being re-created on paint, resetting their orientations
        private float SubThreeTargetSpeedMultiplier; // used for smooth transition

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (MyAPIGateway.Utilities.IsDedicated)
                return;

            block = (IMyFunctionalBlock)Entity;

            if (block.CubeGrid?.Physics == null)
                return;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public override void UpdateBeforeSimulation()
        {
            try
            {
                SpinSubpartOne();
                SpinSubpartTwo();
                SpinSubpartThree();

            }
            catch (Exception e)
            {
                AddToLog(e);
            }
        }

        private void SpinSubpartOne()
        {
            bool shouldSpin = block.IsWorking; // if block is functional and enabled and powered.

            if (!shouldSpin && Math.Abs(SubOneTargetSpeedMultiplier) < 0.00001f)
                return;

            if (shouldSpin && SubOneTargetSpeedMultiplier < 1)
            {
                SubOneTargetSpeedMultiplier = Math.Min(SubOneTargetSpeedMultiplier + SubOne_AccelPercentPerTick, 1);
            }
            else if (!shouldSpin && SubOneTargetSpeedMultiplier > 0)
            {
                SubOneTargetSpeedMultiplier = Math.Max(SubOneTargetSpeedMultiplier - SubOne_DeaccelPercentPerTick, 0);
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation; // local machine camera position

            if (Vector3D.DistanceSquared(camPos, block.GetPosition()) > SubOne_MaxDistSq)
                return;

            MyEntitySubpart subpart;
            if (Entity.TryGetSubpart(SubpartOneName, out subpart)) // subpart does not exist when block is in build stage
            {
                if (SubOneFirstFind) // first time the subpart was found
                {
                    SubOneFirstFind = false;
                    SubOneLocalMatrix = subpart.PositionComp.LocalMatrixRef;
                }

                if (SubOneTargetSpeedMultiplier > 0)
                {
                    SubOneLocalMatrix *= Matrix.CreateFromAxisAngle(SubOne_RotAxis, MathHelper.ToRadians(SubOneTargetSpeedMultiplier * SubOne_DegreesPerTick));
                    SubOneLocalMatrix = Matrix.Normalize(SubOneLocalMatrix); // normalize to avoid any rotation inaccuracies over time resulting in weird scaling
                }

                subpart.PositionComp.SetLocalMatrix(ref SubOneLocalMatrix);
            }
        }

        private void SpinSubpartTwo()
        {
            bool shouldSpin = block.IsWorking; // if block is functional and enabled and powered.

            if (!shouldSpin && Math.Abs(SubTwoTargetSpeedMultiplier) < 0.00001f)
                return;

            if (shouldSpin && SubTwoTargetSpeedMultiplier < 1)
            {
                SubTwoTargetSpeedMultiplier = Math.Min(SubTwoTargetSpeedMultiplier + SubTwo_AccelPercentPerTick, 1);
            }
            else if (!shouldSpin && SubTwoTargetSpeedMultiplier > 0)
            {
                SubTwoTargetSpeedMultiplier = Math.Max(SubTwoTargetSpeedMultiplier - SubTwo_DeaccelPercentPerTick, 0);
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation; // local machine camera position

            if (Vector3D.DistanceSquared(camPos, block.GetPosition()) > SubTwo_MaxDistSq)
                return;

            MyEntitySubpart subpart;
            if (Entity.TryGetSubpart(SubpartTwoName, out subpart)) // subpart does not exist when block is in build stage
            {
                if (SubTwoFirstFind) // first time the subpart was found
                {
                    SubTwoFirstFind = false;
                    SubTwoLocalMatrix = subpart.PositionComp.LocalMatrixRef;
                }

                if (SubTwoTargetSpeedMultiplier > 0)
                {
                    SubTwoLocalMatrix *= Matrix.CreateFromAxisAngle(SubTwo_RotAxis, MathHelper.ToRadians(SubTwoTargetSpeedMultiplier * SubTwo_DegreesPerTick));
                    SubTwoLocalMatrix = Matrix.Normalize(SubTwoLocalMatrix); // normalize to avoid any rotation inaccuracies over time resulting in weird scaling
                }

                subpart.PositionComp.SetLocalMatrix(ref SubTwoLocalMatrix);
            }
        }

        private void SpinSubpartThree()
        {
            bool shouldSpin = block.IsWorking; // if block is functional and enabled and powered.

            if (!shouldSpin && Math.Abs(SubThreeTargetSpeedMultiplier) < 0.00001f)
                return;

            if (shouldSpin && SubThreeTargetSpeedMultiplier < 1)
            {
                SubThreeTargetSpeedMultiplier = Math.Min(SubThreeTargetSpeedMultiplier + SubThree_AccelPercentPerTick, 1);
            }
            else if (!shouldSpin && SubThreeTargetSpeedMultiplier > 0)
            {
                SubThreeTargetSpeedMultiplier = Math.Max(SubThreeTargetSpeedMultiplier - SubThree_DeaccelPercentPerTick, 0);
            }

            var camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation; // local machine camera position

            if (Vector3D.DistanceSquared(camPos, block.GetPosition()) > SubThree_MaxDistSq)
                return;

            MyEntitySubpart subpart;
            if (Entity.TryGetSubpart(SubpartThreeName, out subpart)) // subpart does not exist when block is in build stage
            {
                if (SubThreeFoundFirst) // first time the subpart was found
                {
                    SubThreeFoundFirst = false;
                    SubThreeLocalMatrix = subpart.PositionComp.LocalMatrixRef;
                }

                if (SubThreeTargetSpeedMultiplier > 0)
                {
                    SubThreeLocalMatrix *= Matrix.CreateFromAxisAngle(SubThree_RotAxis, MathHelper.ToRadians(SubThreeTargetSpeedMultiplier * SubThree_DegreesPerTick));
                    SubThreeLocalMatrix = Matrix.Normalize(SubThreeLocalMatrix); // normalize to avoid any rotation inaccuracies over time resulting in weird scaling
                }

                subpart.PositionComp.SetLocalMatrix(ref SubThreeLocalMatrix);
            }
        }

        private void AddToLog(Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"ERROR {GetType().FullName}: {e.ToString()}");

            if (MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ ERROR: {GetType().FullName}: {e.Message} | Send SpaceEngineers.Log to mod author ]", 10000, MyFontEnum.Red);
        }
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether", "Quantum_Tether_Small", "Quantum_Tether_Medium")]
    public class QuantumTetherParticle : StandardParticleGamelogic
    {
        protected override void Setup()
        {
            Declare(dummy: "Tether_Particle_One", particle: "Quantum_Spark", condition: "working");

            Declare(dummy: "Tether_Particle_Two", particle: "Quantum_Core", condition: "enablednonworking");
        }
    }
}
