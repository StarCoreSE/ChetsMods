using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Localization;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace InventoryTether
{
    public static class InventoryTetherControls
    {
        const string IdPrefix = "InventoryTether_";

        static bool Done = false;

        public static void DoOnce(IMyModContext context)
        {
            try
            {
                if (Done)
                    return;
                Done = true;


                CreateControls();
                CreateActions(context);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLine($"[InventoryTetherLogic] {e}");
            }
        }

        static bool IsVisible(IMyTerminalBlock b)
        {
            return b?.GameLogic?.GetAs<InventoryTether>() != null;
        }

        static void CreateControls()
        {
            #region Range Slider
            var RangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMyCollector>(IdPrefix + "RangeSlider");
            RangeSlider.Title = MyStringId.GetOrCompute("Range");
            RangeSlider.Tooltip = MyStringId.GetOrCompute("Diameter of Operational Area centered on the Block");
            RangeSlider.SetLimits
            (
                (b) => GetMinRangeLimit(b),
                (b) => GetMaxRangeLimit(b)
            );
            RangeSlider.Visible = IsVisible;
            RangeSlider.Enabled = IsVisible;
            RangeSlider.Writer = (b, w) =>
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    float value = logic.BlockRange;
                    w.Append(Math.Round(value, 1, MidpointRounding.ToEven)).Append('m');
                }
            };
            RangeSlider.Getter = (b) => b.GameLogic.GetAs<InventoryTether>().BlockRange;
            RangeSlider.Setter = (b, v) => b.GameLogic.GetAs<InventoryTether>().BlockRange = (int)Math.Round(v, 1);
            RangeSlider.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(RangeSlider);
            #endregion

            #region Seperator
            var seperatorOne = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>(""); // separators don't store the id
            seperatorOne.SupportsMultipleBlocks = true;
            seperatorOne.Visible = IsVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(seperatorOne);
            #endregion

            #region Hard Cap Toggle
            var HardCapToggle = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>(IdPrefix + "HardCapToggle");
            HardCapToggle.Title = MyStringId.GetOrCompute("Hard Cap");
            HardCapToggle.Tooltip = MyStringId.GetOrCompute("Items in Inventory over Stock Amount are pushed back to Storage");
            HardCapToggle.OnText = MyStringId.GetOrCompute("On");
            HardCapToggle.OffText = MyStringId.GetOrCompute("Off");
            HardCapToggle.Visible = IsVisible;
            HardCapToggle.Enabled = IsVisible;
            HardCapToggle.Getter = (b) => b.GameLogic.GetAs<InventoryTether>().HardCap;
            HardCapToggle.Setter = (b, v) => b.GameLogic.GetAs<InventoryTether>().HardCap = v;
            HardCapToggle.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(HardCapToggle);
            #endregion

            #region Show Area Toggle
            var ShowAreaToggle = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, IMyCollector>(IdPrefix + "ShowAreaToggle");
            ShowAreaToggle.Title = MyStringId.GetOrCompute("Show Area");
            ShowAreaToggle.Tooltip = MyStringId.GetOrCompute("Show Operational Area");
            ShowAreaToggle.OnText = MyStringId.GetOrCompute("On");
            ShowAreaToggle.OffText = MyStringId.GetOrCompute("Off");
            ShowAreaToggle.Visible = IsVisible;
            ShowAreaToggle.Enabled = IsVisible;
            ShowAreaToggle.Getter = (b) => b.GameLogic.GetAs<InventoryTether>().ShowArea;
            ShowAreaToggle.Setter = (b, v) => b.GameLogic.GetAs<InventoryTether>().ShowArea = v;
            ShowAreaToggle.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(ShowAreaToggle);
            #endregion

            #region Seperator
            var seperatorTwo = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyCollector>(""); // separators don't store the id
            seperatorTwo.SupportsMultipleBlocks = true;
            seperatorTwo.Visible = IsVisible;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(seperatorTwo);
            #endregion

            #region Selection Box
            var componentSelectionBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyCollector>(IdPrefix + "componentSelectionBox");
            componentSelectionBox.Title = MyStringId.GetOrCompute("Availible Components");
            componentSelectionBox.Visible = IsVisible;
            componentSelectionBox.VisibleRowsCount = 8;
            componentSelectionBox.Multiselect = true; // wether player can select muliple at once (ctrl+click, click&shift+click, etc)
            componentSelectionBox.ListContent = (b, content, preSelect) =>
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    foreach (var definition in logic.ComponentDefinitions)
                    {
                        var componentDef = definition as MyComponentDefinition;
                        if (componentDef != null)
                        {
                            var displayName = MyStringId.GetOrCompute(componentDef.DisplayNameText);
                            var subtypeName = MyStringId.GetOrCompute(componentDef.Id.SubtypeName);
                            content.Add(new MyTerminalControlListBoxItem(displayName, subtypeName, componentDef.Id));
                        }
                    }
                }
            };
            componentSelectionBox.ItemSelected = (b, selected) =>
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    logic.TempItemsToAdd = selected;
                }
            };
            componentSelectionBox.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(componentSelectionBox);
            #endregion

            #region Stock Amount Text Box
            var componentStockAmountBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlTextbox, IMyCollector>(IdPrefix + "componentStockAmountBox");
            componentStockAmountBox.Title = MyStringId.GetOrCompute("Stock Amount");
            componentStockAmountBox.Tooltip = MyStringId.GetOrCompute("How Much of the Selected Components you want Stocked [Limited by Config Min/Max]");
            componentStockAmountBox.Visible = IsVisible;
            componentStockAmountBox.Setter = (b, v) => 
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    logic.TempStockAmount = v;
                }
            };
            componentStockAmountBox.Getter = (b) => 
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    return logic.TempStockAmount;
                }
                else
                    return new StringBuilder("Error!");

            };
            componentStockAmountBox.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(componentStockAmountBox);
            #endregion

            #region Stocked Component List Box
            var selectionDisplayBox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyCollector>(IdPrefix + "selectionDisplayBox");
            selectionDisplayBox.Title = MyStringId.GetOrCompute("Stocked Components");
            selectionDisplayBox.Visible = IsVisible;
            selectionDisplayBox.VisibleRowsCount = 8;
            selectionDisplayBox.Multiselect = true;
            selectionDisplayBox.ListContent = (b, content, preSelect) =>
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    foreach (var kvp in logic.TargetItems)
                    {
                        var displayName = MyStringId.GetOrCompute(kvp.Value.DisplayNameText);
                        var tooltip = MyStringId.GetOrCompute($"Stock Amount: {kvp.Value.StockAmount}");
                        content.Add(new MyTerminalControlListBoxItem(displayName, tooltip, kvp.Key));
                    }
                }
            };
            selectionDisplayBox.ItemSelected = (b, selected) =>
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    logic.TempItemsToRemove = selected;
                }
            };
            selectionDisplayBox.SupportsMultipleBlocks = true;
            #endregion

            #region Add Component Button
            var addComponentButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>(IdPrefix + "addComponentButton");
            addComponentButton.Title = MyStringId.GetOrCompute("Add Component");
            addComponentButton.Tooltip = MyStringId.GetOrCompute("Adds Selected Component(s) And Stock Amount to Stock List");         
            addComponentButton.Visible = IsVisible;
            addComponentButton.Action = (b) => 
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    if (logic.TempItemsToAdd.Count == 0) 
                        return;

                    float floatStockAmount;
                    if (!float.TryParse(logic.TempStockAmount.ToString(), out floatStockAmount))
                    {
                        return;
                    }

                    foreach (var item in logic.TempItemsToAdd)
                    {
                        var componentDef = (MyDefinitionId)item.UserData;
                        var subtypeName = componentDef.SubtypeName;
                        var displayName = item.Text.ToString();
                        logic.AddOrUpdateTargetItem(subtypeName, new ComponentData(displayName, (MyFixedPoint)floatStockAmount));
                    }

                    selectionDisplayBox.UpdateVisual();
                }
            };
            addComponentButton.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(addComponentButton);
            #endregion
          
            // Needs to be below Add Comp because reasons
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(selectionDisplayBox);

            #region Remove Component Button
            var removeComponentButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCollector>(IdPrefix + "removeComponentButton");
            removeComponentButton.Title = MyStringId.GetOrCompute("Remove Component");
            removeComponentButton.Tooltip = MyStringId.GetOrCompute("Removes Selected Component(s) And Stock Amount from the Stock List");
            removeComponentButton.Visible = IsVisible;
            removeComponentButton.Action = (b) => 
            {
                var logic = GetLogic(b);
                if (logic != null)
                {
                    if (logic.TempItemsToRemove.Count == 0)
                        return;

                    foreach (var item in logic.TempItemsToRemove)
                    {
                        var subtypeName = item.UserData.ToString();

                        logic.RemoveTargetItem(subtypeName);
                    }

                    selectionDisplayBox.UpdateVisual();
                }
            };
            removeComponentButton.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<IMyCollector>(removeComponentButton);
            #endregion
        }

        static void CreateActions(IMyModContext context)
        {

        }

        static InventoryTether GetLogic(IMyTerminalBlock block) => block?.GameLogic?.GetAs<InventoryTether>();

        static float GetMinRangeLimit(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                return logic.IsSmallGrid() ? logic.Config.Small_MinBlockRange : logic.Config.MinBlockRange;
            }
            return 0;
        }

        static float GetMaxRangeLimit(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                return logic.IsSmallGrid() ? logic.Config.Small_MaxBlockRange : logic.Config.MaxBlockRange;
            }
            return 0;
        }

        static float GetMinStockLimit(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                return logic.Config.MinStockAmount;
            }
            return 0;
        }

        static float GetMaxStockLimit(IMyTerminalBlock block)
        {
            var logic = GetLogic(block);
            if (logic != null)
            {
                return logic.Config.MaxStockAmount;
            }
            return 0;
        }
    }
}
