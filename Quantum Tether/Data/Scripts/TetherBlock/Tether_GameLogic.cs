using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.Common.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.ModAPI;
using VRage.Game;
using VRageMath;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.Components.Interfaces;
using Sandbox.Game.GUI;
using Sandbox.Game;
using Sandbox.Definitions;
using VRage.Utils;
using InventoryTether.Particle;
using static VRageRender.MyBillboard;
using VRage;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Net;

namespace InventoryTether
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class InventoryTetherLogic : MyGameLogicComponent
    {
        private IMyCollector InventoryTetherBlock;
        private IMyHudNotification NotifStatus = null;

        public MyPoweredCargoContainerDefinition InventoryTetherBlockDef;
        public TetherBlockSettings Settings = new TetherBlockSettings();
        public InventoryTetherMod Mod => InventoryTetherMod.Instance;
        private MyResourceSinkComponent Sink = null;
        public readonly Guid Settings_GUID = new Guid("47114A7D-B546-4C05-9E6E-2DFE3449E176");

        private float MinBlockRange = 5;
        private float MaxBlockRange = 500;
        private float BlockRange;

         int syncCountdown;

        public const int SettingsChangedCountdown = (60 * 1) / 10;
        private MyFixedPoint NumberToStock = 10;

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

        #region Overrides
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (!MyAPIGateway.Session.IsServer)
                return;

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                InventoryTetherBlock = (IMyCollector)Entity;

                if (InventoryTetherBlock.CubeGrid?.Physics == null)
                    return;

                InventoryTetherBlock.Enabled = false;

                SetupTerminalControls<IMyCollector>(MinBlockRange, MaxBlockRange);

                InventoryTetherBlockDef = (MyPoweredCargoContainerDefinition)InventoryTetherBlock.SlimBlock.BlockDefinition;

                Sink = InventoryTetherBlock.Components.Get<MyResourceSinkComponent>();
                Sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, RequiredInput);

                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

                LoadSettings();

                if (SettingsBlockRange <= 0)
                {
                    BlockRange = 250;
                    SettingsBlockRange = 250;
                }
                else
                    BlockRange = SettingsBlockRange;

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

        private float RequiredInput()
        {
            if (!InventoryTetherBlock.IsWorking)
                return 0f;

            else if (BlockRange <= 5f)
            {
                return 1.000f;
            }
            else
            {
                float powerPrecentage = InventoryTetherBlockDef.RequiredPowerInput = 100f;
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
                        MyFixedPoint itemAmount = 0;

                        foreach (MyPhysicalInventoryItem item in playerInventory.GetItems())
                        {
                            MyObjectBuilder_Component component = item.Content as MyObjectBuilder_Component;
                            if (component != null && component.SubtypeName == subtype && item.Amount >= NumberToStock)
                            {
                                //SetStatus($"Found: {subtype}", 2000, "Green");
                                subtypeFound = true;
                                itemStocked = true;
                                break;
                            }
                            else if (component != null && component.SubtypeName == subtype && item.Amount < NumberToStock)
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
                    }
                }
            }
        }

        private List<IMyCharacter> NearPlayers()
        {
            List<IMyCharacter> nearPlayers = new List<IMyCharacter>();

            if (InventoryTetherBlock != null)
            {
                List<MyEntity> nearEntities = new List<MyEntity>();
                var bound = new BoundingSphereD(InventoryTetherBlock.GetPosition(), BlockRange);
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref bound, nearEntities);

                foreach (var entity in nearEntities)
                {
                    IMyCharacter player = entity as IMyCharacter;
                    if (player != null && player.IsPlayer && bound.Contains(player.GetPosition()) != ContainmentType.Disjoint)
                    {
                        nearPlayers.Add(player);
                    }
                }
            }

            return nearPlayers;
        }

        private void AddMissingComponent(IMyCharacter player, string subtype, MyFixedPoint itemAmount)
        {
            var componentDefinition = MyDefinitionManager.Static.GetPhysicalItemDefinition(new MyDefinitionId(typeof(MyObjectBuilder_Component), subtype));

            MyFixedPoint neededItemsNumber = NumberToStock - itemAmount;

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
            var tetherGridFatblocks = tetherGrid.GetFatBlocks();

            bool subtypeFound = false;

            if (tetherGrid != null)
            {
                foreach (var block in tetherGridFatblocks)
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
            var tetherGridFatblocks = tetherGrid.GetFatBlocks();

            if (tetherGrid != null)
            {
                if (gridHasComp)
                {
                    foreach (var block in tetherGridFatblocks)
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
                //SetStatus($"Grid doesn't have: {subtype}", 2000, "Red");
                return false;
            }            

            return false;
        }

        #region Terminal Controls
        static void SetupTerminalControls<T>(float minBlockRange, float maxBlockRange)
        {
            var mod = InventoryTetherMod.Instance;

            if (mod.ControlsCreated)
                return;

            mod.ControlsCreated = true;

            var tetherRangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>("QuanTeth" + "Block Range");
            tetherRangeSlider.Title = MyStringId.GetOrCompute("Range");
            tetherRangeSlider.Tooltip = MyStringId.GetOrCompute("Adjusts Range at which the Tether operates");
            tetherRangeSlider.SetLimits(minBlockRange, maxBlockRange);
            tetherRangeSlider.Writer = Control_Power_Writer;
            tetherRangeSlider.Visible = Control_Visible;
            tetherRangeSlider.Getter = Control_Range_Getter;
            tetherRangeSlider.Setter = Control_Range_Setter;
            tetherRangeSlider.Enabled = Control_Visible; ;
            tetherRangeSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<T>(tetherRangeSlider);
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
                logic.BlockRange = MathHelper.Clamp(value, 5f, 500f);
            logic.BlockRange = (float)Math.Round(logic.BlockRange, 0);
            logic.SettingsBlockRange = logic.BlockRange;
        }

        static void Control_Power_Writer(IMyTerminalBlock block, StringBuilder writer)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                float value = logic.BlockRange;
                writer.Append(Math.Round(value, 0, MidpointRounding.ToEven)).Append("m");
            }
        }
        #endregion
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class ParticleOnReactor : StandardParticleGamelogic
    {
        protected override void Setup()
        {
            Declare(dummy: "Tether_Particle_One", particle: "Quantum_Spark", condition: "working");

            Declare(dummy: "Tether_Particle_Two", particle: "Quantum_Core", condition: "enablednonworking");
        }
    }
}