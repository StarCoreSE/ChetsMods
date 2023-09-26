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

namespace InventoryTether
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class InventoryTetherLogic : MyGameLogicComponent
    {
        private IMyCollector InventoryTetherBlock;
        private IMyHudNotification NotifStatus = null;
        private int BlockRange = 250;
        private MyFixedPoint NumberToStock = 10;

        private string EmissiveMaterialName = "Emissive";
        private Color GreenColor = new Color(0, 255, 0);
        private Color YellowColor = new Color(255, 217, 0);

        private bool EmissiveSet = false;

        List<string> targetSubtypes = new List<string> { "SteelPlate", "Construction", "InteriorPlate", "BulletproofGlass", "Girder" };

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

                NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;

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
                if (MyAPIGateway.Session.GameplayFrameCounter % 120 == 0)
                    ScanPlayerInventory();

                if (InventoryTetherBlock.IsWorking && !EmissiveSet)
                {
                    InventoryTetherBlock.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.DeepSkyBlue, 0.75f);
                    EmissiveSet = true;
                }
                else if (!InventoryTetherBlock.IsWorking && EmissiveSet)
                {
                    InventoryTetherBlock.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.Black, 100f);
                    EmissiveSet = false;
                }
                else if (!InventoryTetherBlock.IsWorking && !EmissiveSet)
                {
                    InventoryTetherBlock.SetEmissivePartsForSubparts(EmissiveMaterialName, Color.Black, 100f);
                }
                else if (InventoryTetherBlock.IsWorking && EmissiveSet)
                {
                    return;
                }

            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification($"{e}", 5000, "Red");
                MyLog.Default.WriteLineAndConsole($"{e}");
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
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Collector), false, "Quantum_Tether")]
    public class ParticleOnReactor : StandardParticleGamelogic
    {
        protected override void Setup()
        {
            Declare(dummy: "Tether_Particle", particle: "Quantum_Spark", condition: "working");
        }
    }
}