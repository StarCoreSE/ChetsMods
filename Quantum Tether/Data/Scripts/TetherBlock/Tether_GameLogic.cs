using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.GUI;
using Sandbox.Game;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Components;
using VRage.Game.Components.Interfaces;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using InventoryTether.Particle;
using static VRageRender.MyBillboard;
using InventoryTether.Config;

namespace InventoryTether
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class InventoryTetherLogic : MyGameLogicComponent
    {
        private IMyCollector InventoryTetherBlock;
        private IMyHudNotification NotifStatus = null;
        private IMyHudNotification NotifNoneStatus = null;

        public MyPoweredCargoContainerDefinition InventoryTetherBlockDef;
        public TetherBlockSettings Settings = new TetherBlockSettings();
        public readonly Tether_ConfigSettings Config = new Tether_ConfigSettings();
        public InventoryTetherMod Mod => InventoryTetherMod.Instance;
        private MyResourceSinkComponent Sink = null;
        public readonly Guid Settings_GUID = new Guid("47114A7D-B546-4C05-9E6E-2DFE3449E176");

        private float MinimumPowerRequirement;
        private float MaximumPowerRequirement;

        private float MinBlockRange;
        private float MaxBlockRange;
        private float BlockRange;

        private float MinStockAmount;
        private float MaxStockAmount;
        private float StockAmount;

        private bool ShowArea = false;
        private bool HardCap = false;

        int syncCountdown;

        public const int SettingsChangedCountdown = (60 * 1) / 10;

        private string EmissiveMaterialName = "Emissive";
        private bool EmissiveSet = false;

        List<string> targetSubtypes = new List<string>();

        List<string> defaultSubtypes = new List<string> { "SteelPlate", "Construction", "InteriorPlate", "BulletproofGlass", "Girder" };

        public float SettingsBlockRange
        {
            get { return Settings.BlockRange; }
            set
            {
                Settings.BlockRange = MathHelper.Clamp((int)Math.Floor(value), MinBlockRange, MaxBlockRange);

                SettingsChanged();

                if (Settings.BlockRange < 5)
                {
                    NeedsUpdate = MyEntityUpdateEnum.NONE;
                }
                else
                {
                    if ((NeedsUpdate & MyEntityUpdateEnum.EACH_10TH_FRAME) == 0)
                        NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                }

                InventoryTetherBlock?.Components?.Get<MyResourceSinkComponent>()?.Update();
            }
        }

        public float SettingsStockAmount
        {
            get { return Settings.StockAmount; }
            set
            {
                Settings.StockAmount = MathHelper.Clamp((int)Math.Floor(value), MinStockAmount, MaxStockAmount);

                SettingsChanged();

                if (Settings.BlockRange < 1)
                {
                    NeedsUpdate = MyEntityUpdateEnum.NONE;
                }
                else
                {
                    if ((NeedsUpdate & MyEntityUpdateEnum.EACH_10TH_FRAME) == 0)
                        NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                }

                InventoryTetherBlock?.Components?.Get<MyResourceSinkComponent>()?.Update();
            }
        }

        public bool SettingsShowArea
        {
            get { return Settings.ShowArea; }
            set
            {
                Settings.ShowArea = value;

                SettingsChanged();

                if ((NeedsUpdate & MyEntityUpdateEnum.EACH_10TH_FRAME) == 0)
                    NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

                InventoryTetherBlock?.Components?.Get<MyResourceSinkComponent>()?.Update();
            }
        }

        public bool SettingsHardCap
        {
            get { return Settings.HardCap; }
            set
            {
                Settings.HardCap = value;

                SettingsChanged();

                if ((NeedsUpdate & MyEntityUpdateEnum.EACH_10TH_FRAME) == 0)
                    NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;

                InventoryTetherBlock?.Components?.Get<MyResourceSinkComponent>()?.Update();
            }
        }

        #region Overrides
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {            
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                Config.Load();

                RetrieveValuesFromConfig();

                InventoryTetherBlock = (IMyCollector)Entity;

                if (InventoryTetherBlock.CubeGrid?.Physics == null)
                    return;

                InventoryTetherBlock.Enabled = false;

                SetupTerminalControls<IMyCollector>(MinBlockRange, MaxBlockRange, MinStockAmount, MaxStockAmount);

                if (!MyAPIGateway.Session.IsServer)
                    return;
                    
                InventoryTetherBlockDef = (MyPoweredCargoContainerDefinition)InventoryTetherBlock.SlimBlock.BlockDefinition;

                Sink = InventoryTetherBlock.Components.Get<MyResourceSinkComponent>();
                Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, RequiredInput);

                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

                LoadSettings();

                if (SettingsBlockRange <= 0)
                {
                    BlockRange = MaxBlockRange / 2;
                    SettingsBlockRange = MaxBlockRange / 2;
                }
                else
                    BlockRange = SettingsBlockRange;

                if (SettingsStockAmount <= 0)
                {
                    StockAmount = 10;
                    SettingsStockAmount = 10;
                }
                else
                    StockAmount = SettingsStockAmount;

                if (SettingsHardCap != HardCap)
                {
                    HardCap = SettingsHardCap;
                }

                if (SettingsShowArea != ShowArea)
                {
                    ShowArea = SettingsShowArea;
                }

                if (InventoryTetherBlock.CustomData.Equals(""))
                {
                    string listAsString = string.Join(Environment.NewLine, defaultSubtypes);
                    InventoryTetherBlock.CustomData = listAsString;
                }

                SaveSettings();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification($"{e}", 5000, "Red");
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (MyAPIGateway.Session.GameplayFrameCounter % 60 == 0 && InventoryTetherBlock.IsWorking)
                {
                    CheckCustomData();
                    ScanPlayerInventory();
                    Sink.Update();
                    ForceUpdateCustomInfo();
                }

                CheckShowArea();
                SetEmissives();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification($"{e}", 5000, "Red");
                MyLog.Default.WriteLineAndConsole($"{e}");
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            try
            {
                SyncSettings();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Close()
        {
            try
            {
                if (InventoryTetherBlock == null)
                    return;

                InventoryTetherBlock = null;
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification($"{e}", 5000, "Red");
            }
        }
        #endregion

        #region Utilities
        public void RetrieveValuesFromConfig()
        {
            // Assign General Values from Config
            if (MinimumPowerRequirement != Config.MinimumPowerRequirement)
                MinimumPowerRequirement = Config.MinimumPowerRequirement;

            if (MaximumPowerRequirement != Config.MaximumPowerRequirement)
                MaximumPowerRequirement = Config.MaximumPowerRequirement;

            if (MinBlockRange != Config.MinBlockRange)
                MinBlockRange = Config.MinBlockRange;

            if (MaxBlockRange != Config.MaxBlockRange)
                MaxBlockRange = Config.MaxBlockRange;

            if (MinStockAmount != Config.MinStockAmount)
                MinStockAmount = Config.MinStockAmount;

            if (MaxStockAmount != Config.MaxStockAmount)
                MaxStockAmount = Config.MaxStockAmount;
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

        private float RequiredInput()
        {
            if (!InventoryTetherBlock.IsWorking)
                return 0f;

            else if (BlockRange <= MinBlockRange)
            {
                return MinimumPowerRequirement;
            }
            else
            {
                float powerPrecentage = InventoryTetherBlockDef.RequiredPowerInput = MaximumPowerRequirement;
                float sliderValue = BlockRange;

                float ratio = sliderValue / MaxBlockRange;

                return powerPrecentage * ratio;
            }
        }

        private void ForceUpdateCustomInfo()
        {
            InventoryTetherBlock.RefreshCustomInfo();
            InventoryTetherBlock.SetDetailedInfoDirty();
        }

        private void CheckCustomData()
        {
            if (InventoryTetherBlock.CustomData != null)
            {
                string defaultListAsString = string.Join(Environment.NewLine, defaultSubtypes);
                string targetListAsString = string.Join(Environment.NewLine, targetSubtypes);

                if (!InventoryTetherBlock.CustomData.Equals(defaultListAsString) || !InventoryTetherBlock.CustomData.Equals(targetListAsString))
                {
                    string customData = InventoryTetherBlock.CustomData;
                    targetSubtypes = customData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
        }

        private void CheckShowArea()
        {
            if (SettingsShowArea && !MyAPIGateway.Utilities.IsDedicated)
            {
                Vector3 pos = InventoryTetherBlock.GetPosition();
                var matrix = MatrixD.CreateWorld(pos);
                int wireDivRatio = 360 / 15;

                Color color = Color.DarkGray;
                if (InventoryTetherBlock.Enabled)
                {
                    color = InventoryTetherBlock.IsWorking ? Color.LightGreen : Color.Yellow;
                }

                float radius = SettingsBlockRange / 2;
                float lineThickness = 0.15f + ((radius - 2.5f) / ((MaxBlockRange / 2) - (MinBlockRange / 2))) * (1.5f - 0.15f);

                MySimpleObjectDraw.DrawTransparentSphere(ref matrix, SettingsBlockRange / 2, ref color, MySimpleObjectRasterizer.Wireframe, wireDivRatio, null, MyStringId.GetOrCompute("WeaponLaserIgnoreDepth"), lineThickness, -1, null, BlendTypeEnum.SDR, 1);
            }
            else
                return;
        }

        private void SetEmissives()
        {
            if (InventoryTetherBlock.IsWorking)
            {
                if (!EmissiveSet)
                {
                    InventoryTetherBlock.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.DeepSkyBlue, 0.75f);
                    EmissiveSet = true;
                }
                // No action needed for EmissiveSet = true, just return
                return;
            }

            if (!InventoryTetherBlock.Enabled)
            {
                InventoryTetherBlock.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.Black, 100f);
                EmissiveSet = false;
            }
            else // InventoryTetherBlock.Enabled == true
            {
                InventoryTetherBlock.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.Yellow, 0.75f);
                EmissiveSet = false;
            }
        }
        #endregion

        #region Settings
        bool LoadSettings()
        {
            if (InventoryTetherBlock.Storage == null)
                return false;

            string rawData;
            if (!InventoryTetherBlock.Storage.TryGetValue(Settings_GUID, out rawData))
                return false;

            try
            {
                var loadedSettings = MyAPIGateway.Utilities.SerializeFromBinary<TetherBlockSettings>(Convert.FromBase64String(rawData));

                if (loadedSettings != null)
                {
                    Settings.BlockRange = loadedSettings.BlockRange;
                    Settings.StockAmount = loadedSettings.StockAmount;
                    Settings.ShowArea = loadedSettings.ShowArea;
                    return true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"Error loading settings!\n{e}");
            }

            return false;
        }

        void SaveSettings()
        {
            if (InventoryTetherBlock == null)
                return; // called too soon or after it was already closed, ignore

            if (Settings == null)
                throw new NullReferenceException($"Settings == null on entId={Entity?.EntityId}; modInstance={InventoryTetherMod.Instance != null}");

            if (MyAPIGateway.Utilities == null)
                throw new NullReferenceException($"MyAPIGateway.Utilities == null; entId={Entity?.EntityId}; modInstance={InventoryTetherMod.Instance != null}");

            if (InventoryTetherBlock.Storage == null)
                InventoryTetherBlock.Storage = new MyModStorageComponent();

            InventoryTetherBlock.Storage.SetValue(Settings_GUID, Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(Settings)));

            //MyAPIGateway.Utilities.ShowNotification(SettingsBlockRange.ToString(), 1000, "Red");
        }

        void SyncSettings()
        {
            if (syncCountdown > 0 && --syncCountdown <= 0)
            {
                SaveSettings();

                Mod.CachedPacketSettings.Send(InventoryTetherBlock.EntityId, Settings);
            }
        }

        void SettingsChanged()
        {
            if (syncCountdown == 0)
                syncCountdown = SettingsChangedCountdown;
        }

        public override bool IsSerialized()
        {
            try
            {
                SaveSettings();
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            return base.IsSerialized();
        }
        #endregion

        private void ScanPlayerInventory()
        {
            if (InventoryTetherBlock.Enabled)
            {
                foreach (IMyCharacter player in NearPlayers())
                {
                    //var color = Color.Green.ToVector4();
                    //MySimpleObjectDraw.DrawLine(InventoryTetherBlock.GetPosition(), player.GetPosition() + player.WorldMatrix.Up * 1, MyStringId.GetOrCompute("WeaponLaser"), ref color, 1f, BlendTypeEnum.SDR);

                    MyInventory playerInventory = player.GetInventory() as MyInventory;

                    foreach (var subtype in targetSubtypes)
                    {
                        bool subtypeFound = false;
                        bool itemStocked = false;
                        bool itemOverStocked = false;
                        MyFixedPoint itemAmount = 0;
                        MyFixedPoint overstockItemAmount = 0;

                        foreach (MyPhysicalInventoryItem item in playerInventory.GetItems())
                        {
                            MyObjectBuilder_Component component = item.Content as MyObjectBuilder_Component;
                            if (component != null && component.SubtypeName == subtype && item.Amount == (MyFixedPoint)StockAmount)
                            {
                                //SetStatus($"Found: {subtype}", 2000, "Green");
                                subtypeFound = true;
                                itemStocked = true;
                                break;
                            }
                            else if (component != null && component.SubtypeName == subtype && item.Amount > (MyFixedPoint)StockAmount && SettingsHardCap)
                            {
                                subtypeFound = true;
                                itemOverStocked = true;
                                itemStocked = true;
                                overstockItemAmount = item.Amount - (MyFixedPoint)StockAmount;
                            }
                            else if (component != null && component.SubtypeName == subtype && item.Amount < (MyFixedPoint)StockAmount)
                            {
                                subtypeFound = true;
                                itemStocked = false;
                                itemAmount = item.Amount;
                                break;
                            }
                        }

                        if (!subtypeFound || !itemStocked)
                        {
                            //SetStatus($"Missing: {subtype}", 2000, "Red");
                            AddMissingComponent(player, subtype, itemAmount);
                        }
                        else if (subtypeFound && itemOverStocked && HardCap)
                        {
                            RemoveExcessComponent(player, subtype, overstockItemAmount);
                        }
                    }
                }
            }
        }

        private List<IMyCharacter> NearPlayers()
        {
            List<IMyCharacter> nearPlayers = new List<IMyCharacter>();
            List<IMyPlayer> actualPlayers = new List<IMyPlayer>();

            if (InventoryTetherBlock != null)
            {
                List<MyEntity> nearEntities = new List<MyEntity>();
                var bound = new BoundingSphereD(InventoryTetherBlock.GetPosition(), BlockRange / 2);
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref bound, nearEntities);

                foreach (var entity in nearEntities)
                {
                    IMyCharacter player = entity as IMyCharacter;
                    if (player != null && player.IsPlayer && bound.Contains(player.GetPosition()) != ContainmentType.Disjoint)
                    {
                        MyAPIGateway.Players.GetPlayers(actualPlayers);

                        foreach ( IMyPlayer realplayer in actualPlayers)
                        {
                            if (realplayer.Character == player)
                            {
                                var playerRelation = InventoryTetherBlock.GetUserRelationToOwner(realplayer.IdentityId);

                                if (playerRelation.IsFriendly())
                                {
                                    nearPlayers.Add(player);
                                }
                                else
                                    continue;
                            }
                        }
                    }
                }
            }

            return nearPlayers;
        }

        private void AddMissingComponent(IMyCharacter player, string subtype, MyFixedPoint itemAmount)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));

            MyFixedPoint neededItemsNumber = (MyFixedPoint)StockAmount - itemAmount;

            bool invDisconnect = false;
            var gridHasComp = ScanGridInventoryForItem(subtype, neededItemsNumber, out invDisconnect);

            if (componentDefinition != null && gridHasComp)
            {
                var compRemoved = RemoveCompFromGrid(subtype, neededItemsNumber, gridHasComp);

                if (compRemoved)
                {
                    var playerInventory = player.GetInventory();

                    if (playerInventory.CanItemsBeAdded(1, componentDefinition.Id))
                    {
                        var inventoryItem = new MyObjectBuilder_InventoryItem()
                        {
                            Amount = neededItemsNumber,
                            Content = new MyObjectBuilder_Component() { SubtypeName = subtype },
                        };

                        //SetStatus($"Adding: {subtype}", 2000, "White");
                        playerInventory.AddItems(inventoryItem.Amount, inventoryItem.Content);
                    }
                }
                else if (!compRemoved)
                {
                    //SetStatus($"Removal Failed", 2000, "Red");
                }
            }

/*            if (!gridHasComp && !invDisconnect)
            {
                MyAPIGateway.Utilities.ShowNotification($"{(string)InventoryTetherBlock.CubeGrid.DisplayName} out of {subtype}", 900, "Red");
            }*/
            if (invDisconnect)
            {
                SetStatus($"Tether: Inventory Inaccessible for {subtype}", 2000, "Red");
            }
        }

        private bool ScanGridInventoryForItem(string subtype, MyFixedPoint neededItemsNumber, out bool invDisconnect)
        {
            MyCubeGrid tetherGrid = (MyCubeGrid)InventoryTetherBlock.CubeGrid;
            var gridGroup = tetherGrid.GetGridGroup(GridLinkTypeEnum.Logical);
            var subgroups = new List<IMyCubeGrid>();
            gridGroup.GetGrids(subgroups);

            bool subtypeFound = false;

            if (tetherGrid != null)
            {
                foreach (var grid in subgroups)
                {
                    var castedGrid = (MyCubeGrid)grid;
                    var gridFatblocks = castedGrid.GetFatBlocks();

                    foreach (var block in gridFatblocks)
                    {
                        MyInventory blockInv = block.GetInventory();

                        if (blockInv == null)
                        {
                            // Fatblocks without an Inventory get skipped
                            continue;
                        }

                        foreach (MyPhysicalInventoryItem item in blockInv.GetItems())
                        {
                            if (item.Content == null)
                            {
                                //SetStatus("Item or Content is null", 2000, "Red");
                                continue;
                            }

                            MyObjectBuilder_Component component = item.Content as MyObjectBuilder_Component;

                            if (component != null && component.SubtypeName == subtype && item.Amount >= neededItemsNumber)
                            {
                                //SetStatus($"Found: {subtype}", 2000, "Green");

                                IMyInventory tetherIMyInv = InventoryTetherBlock.GetInventory();
                                IMyInventory blockIMyInv = block.GetInventory();

                                if (tetherIMyInv.IsConnectedTo(blockIMyInv))
                                {
                                    subtypeFound = true;
                                    invDisconnect = false;
                                    return true;
                                }
                                else
                                {
                                    //SetStatus($"Inventory Not Connected: ({InventoryTetherBlock.DisplayNameText} to {block.DisplayNameText})", 2000, "Red");
                                    subtypeFound = false;
                                    invDisconnect = true;
                                    return false;
                                }
                            }
                        }
                    }
                }
            }         

            if (!subtypeFound || tetherGrid == null)
            {
                SetStatus($"Tether: {InventoryTetherBlock.CubeGrid.DisplayName} Missing: {subtype}", 2000, "Red");
                invDisconnect = false;
                return false;
            }

            invDisconnect = false;
            return false;
        }

        private bool RemoveCompFromGrid(string subtype, MyFixedPoint neededItemsNumber, bool gridHasComp)
        {
            MyCubeGrid tetherGrid = (MyCubeGrid)InventoryTetherBlock.CubeGrid;
            var gridGroup = tetherGrid.GetGridGroup(GridLinkTypeEnum.Logical);
            var subgroups = new List<IMyCubeGrid>();
            gridGroup.GetGrids(subgroups);

            if (tetherGrid != null)
            {
                if (gridHasComp)
                {
                    foreach (var grid in subgroups)
                    {
                        var castedGrid = (MyCubeGrid)grid;
                        var gridFatblocks = castedGrid.GetFatBlocks();

                        foreach (var block in gridFatblocks)
                        {
                            MyInventory blockInv = block.GetInventory();

                            if (blockInv == null)
                            {
                                continue;
                            }

                            foreach (MyPhysicalInventoryItem item in blockInv.GetItems())
                            {
                                MyObjectBuilder_Component component = item.Content as MyObjectBuilder_Component;
                                if (component != null && component.SubtypeName == subtype)
                                {
                                    blockInv.RemoveItemsOfType(neededItemsNumber, item.Content);
                                    //SetStatus($"Removed: {subtype}", 2000, "White");
                                    return true;
                                }
                            }
                        }
                    }
                }
                    //SetStatus($"Grid doesn't have: {subtype}", 2000, "Red");
                    return false;
            }            

            return false;
        }

        private void RemoveExcessComponent(IMyCharacter player, string subtype, MyFixedPoint overstockItemAmount)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));

            if (CheckBlockForSpace(subtype, overstockItemAmount))
            {
                var playerInventory = player.GetInventory();
                IMyInventory blockInventory = InventoryTetherBlock.GetInventory();

                if (playerInventory.ContainItems((MyFixedPoint)StockAmount + overstockItemAmount, componentDefinition.Id))
                {
                    var inventoryItem = new MyObjectBuilder_InventoryItem()
                    {
                        Amount = overstockItemAmount,
                        Content = new MyObjectBuilder_Component() { SubtypeName = subtype },
                    };

                    playerInventory.RemoveItemsOfType(inventoryItem.Amount, componentDefinition.Id);
                    blockInventory.AddItems(inventoryItem.Amount, inventoryItem.Content);
                }
                else
                    Log.Error($"Item [{subtype}] No Longer Exists in [{player.DisplayName}]'s Inventory");
            }
            else
                SetNoneStatus("No Space in Tether Block!", 2000, "Red");
        }

        private bool CheckBlockForSpace(string subtype, MyFixedPoint overstockItemAmount)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));

            IMyInventory blockInventory = InventoryTetherBlock.GetInventory();

            if (componentDefinition != null)
            {
                if (blockInventory.CanItemsBeAdded(overstockItemAmount, componentDefinition.Id))
                {
                    return true;
                }
                else
                    return false;
            }

            return false;
        }    

        #region Terminal Controls
        static void SetupTerminalControls<T>(float minBlockRange, float maxBlockRange, float minStockAmount, float maxStockAmount)
        {
            var mod = InventoryTetherMod.Instance;

            if (mod.ControlsCreated)
                return;

            mod.ControlsCreated = true;

            var tetherRangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("QuanTeth" + "BlockRange");
            tetherRangeSlider.Title = MyStringId.GetOrCompute("Range");
            tetherRangeSlider.Tooltip = MyStringId.GetOrCompute("Diameter of Operational Area centered on the Block");
            tetherRangeSlider.SetLimits(minBlockRange, maxBlockRange);
            tetherRangeSlider.Writer = Control_Range_Writer;
            tetherRangeSlider.Visible = Control_Visible;
            tetherRangeSlider.Getter = Control_Range_Getter;
            tetherRangeSlider.Setter = Control_Range_Setter;
            tetherRangeSlider.Enabled = Control_Visible;
            tetherRangeSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<T>(tetherRangeSlider);

            var tetherStockAmount = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("QuanTeth" + "StockAmount");
            tetherStockAmount.Title = MyStringId.GetOrCompute("Amount to Stock");
            tetherStockAmount.Tooltip = MyStringId.GetOrCompute("How many of Each Componenet to Stock");
            tetherStockAmount.SetLimits(minStockAmount, maxStockAmount);
            tetherStockAmount.Writer = Control_Stock_Writer;
            tetherStockAmount.Visible = Control_Visible;
            tetherStockAmount.Getter = Control_Stock_Getter;
            tetherStockAmount.Setter = Control_Stock_Setter;
            tetherStockAmount.Enabled = Control_Visible;
            tetherStockAmount.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<T>(tetherStockAmount);

            var tetherControlSeperator = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>("");
            tetherControlSeperator.Visible = Control_Visible;
            tetherControlSeperator.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(tetherControlSeperator);

            var tetherShowArea = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyCollector>("QuanTeth" + "ShowArea");
            tetherShowArea.Title = MyStringId.GetOrCompute("Show Area");
            tetherShowArea.Tooltip = MyStringId.GetOrCompute("Toggles a Visual of the Operational Area");
            tetherShowArea.Visible = Control_Visible;
            tetherShowArea.Getter = Control_ShowArea_Getter;
            tetherShowArea.Setter = Control_ShowArea_Setter;
            tetherShowArea.Enabled = Control_Visible;
            tetherShowArea.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(tetherShowArea);

            var tetherHardCap = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyCollector>("QuanTeth" + "HardCap");
            tetherHardCap.Title = MyStringId.GetOrCompute("Hard Cap");
            tetherHardCap.Tooltip = MyStringId.GetOrCompute("Items in Inventory over Stock Number gets shifted to Tether Block");
            tetherHardCap.Visible = Control_Visible;
            tetherHardCap.Getter = Control_HardCap_Getter;
            tetherHardCap.Setter = Control_HardCap_Setter;
            tetherHardCap.Enabled = Control_Visible;
            tetherHardCap.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(tetherHardCap);
        }

        static InventoryTetherLogic GetLogic(IMyTerminalBlock block) => block?.GameLogic?.GetAs<InventoryTetherLogic>();

        static bool Control_Visible(IMyTerminalBlock block)
        {
            return GetLogic(block) != null;
        }

        static float Control_Range_Getter(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return logic != null ? logic.BlockRange : 0f;
        }

        static void Control_Range_Setter(IMyTerminalBlock block, float value)
        {
            var logic = GetLogic(block);
            if (logic != null)
                logic.BlockRange = MathHelper.Clamp(value, logic.MinBlockRange, logic.MaxBlockRange);
            logic.BlockRange = (float)Math.Round(logic.BlockRange, 0);
            logic.SettingsBlockRange = logic.BlockRange;
        }

        static void Control_Range_Writer(IMyTerminalBlock block, StringBuilder writer)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                float value = logic.BlockRange;
                writer.Append(Math.Round(value, 0, MidpointRounding.ToEven)).Append("m");
            }
        }

        static float Control_Stock_Getter(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return logic != null ? logic.StockAmount : 1f;
        }

        static void Control_Stock_Setter(IMyTerminalBlock block, float value)
        {
            var logic = GetLogic(block);
            if (logic != null)
                logic.StockAmount = MathHelper.Clamp(value, logic.MinStockAmount, logic.MaxStockAmount);
            logic.StockAmount = (float)Math.Round(logic.StockAmount, 0);
            logic.SettingsStockAmount = logic.StockAmount;
        }

        static void Control_Stock_Writer(IMyTerminalBlock block, StringBuilder writer)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                float value = logic.StockAmount;
                writer.Append(Math.Round(value, 0, MidpointRounding.ToEven));
            }
        }

        static bool Control_ShowArea_Getter(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return logic != null ? logic.ShowArea : false;
        }

        static void Control_ShowArea_Setter(IMyTerminalBlock block, bool value)
        {
            var logic = GetLogic(block);
            if (logic != null)
                logic.ShowArea = value;
                logic.Settings.ShowArea = logic.ShowArea;
        }

        static bool Control_HardCap_Getter(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            return logic != null ? logic.HardCap : false;
        }

        static void Control_HardCap_Setter(IMyTerminalBlock block, bool value)
        {
            var logic = GetLogic(block);
            if (logic != null)
                logic.HardCap = value;
            logic.Settings.HardCap = logic.HardCap;
        }
        #endregion
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
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

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class QuantumTetherParticle : StandardParticleGamelogic
    {
        protected override void Setup()
        {
            Declare(dummy: "Tether_Particle_One", particle: "Quantum_Spark", condition: "working");

            Declare(dummy: "Tether_Particle_Two", particle: "Quantum_Core", condition: "enablednonworking");
        }
    }
}
