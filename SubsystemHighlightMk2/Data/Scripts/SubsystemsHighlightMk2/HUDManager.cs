using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sandbox.Definitions;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Draygo.API;
using Sandbox.Engine.Utils;
using static Draygo.API.HudAPIv2;
using static VRageRender.MyBillboard;
using static StarCore.Highlights.HighlightManager;
using static StarCore.Highlights.CutawayManager;

namespace StarCore.Highlights
{
    public class HUDManager
    {
        public static HUDManager I = new HUDManager();
        private HudAPIv2 HudAPI = new HudAPIv2();
        private bool HUDAPILoaded = false;

        HUDMessage SubsystemHUD;
        StringBuilder SubsystemHUDContent;

        HUDMessage KeybindHUD;
        StringBuilder KeybindHUDContent;

        #region Update Methods
        public void Init()
        {
            I = this;

            if (HudAPI.Heartbeat)
            {
                HUDAPILoaded = true;
            }
        }

        public void Update()
        {

        }

        public void Draw(bool ToolEquipped)
        {
            #region Subsystem HUD
            if (!ToolEquipped)
            {
                if (SubsystemHUDContent != null)
                    SubsystemHUDContent.Clear();

                if (KeybindHUDContent != null)
                    KeybindHUDContent.Clear();

                return;
            }

            if (SubsystemHUDContent == null)
            {
                SubsystemHUDContent = new StringBuilder();
            }
            SubsystemHUDContent.Clear();

            var activeMode = HighlightTool_Core.Instance.CurrentMode.ToString();
            SubsystemHUDContent.Append($"<color=yellow>Mode: <color=white>{activeMode}\n");      

            if (HighlightTool_Core.Instance.CurrentMode == 0)
            {
                var selectedHighlight = HighlightManager.I.CurrentFilter.Name;

                SubsystemHUDContent.Append($"\n<color=yellow>Filter: <color=white>{selectedHighlight} \n");

                IMyCubeGrid grid = HighlightTool_Core.Instance.CastForGrid();
                long entityId = grid != null ? grid.EntityId : 0;
                var currentTarget = grid != null ? grid.DisplayName : "None";

                SubsystemHUDContent.Append($"\n<color=yellow>Current Target:\n<color=white>{currentTarget}\n");

                Dictionary<HighlightFilter, bool> activeHighlightsDict;
                if(HighlightManager.I.ActiveHighlights.TryGetValue(entityId, out activeHighlightsDict))
                {
                    string filterNames = string.Join("\n", activeHighlightsDict.Keys.Select(f => f.Name));
                    SubsystemHUDContent.Append($"\n<color=yellow>Applied Filters:\n<color=white>{filterNames}");
                }
                else if (grid != null)         
                    SubsystemHUDContent.Append($"\n<color=yellow>No Applied Filters");               
            }
            else if ((int)HighlightTool_Core.Instance.CurrentMode == 1)
            {
                var currentAxis = CutawayManager.I.CutawayAxis.ToString();
                SubsystemHUDContent.Append($"\n<color=yellow>Axis: <color=white>{currentAxis}\n");

                IMyCubeGrid grid = HighlightTool_Core.Instance.CastForGrid();
                var currentTarget = grid != null ? grid.DisplayName : "None";
                SubsystemHUDContent.Append($"\n<color=yellow>Current Target:\n<color=white>{currentTarget}\n");

                if (grid != null)
                {
                    var currentPosition = CutawayManager.I.CutawayPosition.ToString();
                    SubsystemHUDContent.Append($"\n<color=yellow>Position: <color=white>{currentPosition}\n");

                    SubsystemHUDContent.Append($"\n<color=yellow>Inverted: <color=white>{CutawayManager.I.IsNormalInverted.ToString()}\n");
                }           
            }
            else if ((int)HighlightTool_Core.Instance.CurrentMode == 2)
            {
                var currentType = DiagnosticManager.I.DiagnosticType.ToString();
                SubsystemHUDContent.Append($"\n<color=yellow>Type: <color=white>{currentType}\n");

                IMyCubeGrid grid = HighlightTool_Core.Instance.CastForGrid();
                var currentTarget = grid != null ? grid.DisplayName : "None";
                SubsystemHUDContent.Append($"\n<color=yellow>Current Target:\n<color=white>{currentTarget}\n");

                if (grid != null)
                {
                    var value = (DiagnosticManager.DiagnosticTypeEnum)0;
                    if (DiagnosticManager.I.ActiveDiagnostics.TryGetValue(grid.EntityId, out value))
                    {
                        var appliedType = DiagnosticManager.I.ActiveDiagnostics[grid.EntityId].ToString();
                        SubsystemHUDContent.Append($"\n<color=yellow>Applied Type: <color=white>{appliedType}\n");

                        switch (DiagnosticManager.I.ActiveDiagnostics[grid.EntityId])
                        {
                            case DiagnosticManager.DiagnosticTypeEnum.Incomplete:
                                SubsystemHUDContent.Append("\n<color=yellow>Yellow = Incomplete\n");
                                SubsystemHUDContent.Append("<color=yellow>Red = Damaged\n");
                                break;
                            case DiagnosticManager.DiagnosticTypeEnum.Enabled:
                                SubsystemHUDContent.Append("\n<color=yellow>Green = Enabled\n");
                                SubsystemHUDContent.Append("<color=yellow>Red = Disabled\n");
                                break;
                            case DiagnosticManager.DiagnosticTypeEnum.Working:
                                SubsystemHUDContent.Append("\n<color=yellow>Green = Functional, Working\n");
                                SubsystemHUDContent.Append("<color=yellow>Light Blue = Functional, Idle\n");
                                SubsystemHUDContent.Append("<color=yellow>Red = Damaged\n");
                                break;

                        }
                    }          
                }
            }

            if (SubsystemHUD == null && HudAPI.Heartbeat)
            {
                SubsystemHUD = new HUDMessage
                (
                    Message: SubsystemHUDContent,
                    Origin: new Vector2D(-0.25f, -0.3f),
                    TimeToLive: -1,
                    Scale: 0.75f,
                    HideHud: false,
                    Blend: BlendTypeEnum.PostPP,
                    Font: "monospace"
                );

               /* HighlighterHUD.Offset = HighlighterHUD.GetTextLength() / 2;*/
                SubsystemHUD.Visible = true;
            }
            #endregion

            #region Keybind HUD
            if(KeybindHUDContent == null)
            {
                KeybindHUDContent = new StringBuilder();
            }
            KeybindHUDContent.Clear();

            KeybindHUDContent.Append($"<color=yellow>[LShift+MW] Cycle Mode\n");

            if (HighlightTool_Core.Instance.CurrentMode == 0)
            {
                KeybindHUDContent.Append($"\n<color=yellow>[MW] Cycle Filter\n");
                KeybindHUDContent.Append($"\n<color=yellow>[R] Reset Filters\n");
                KeybindHUDContent.Append($"\n<color=yellow>[LMB] Apply Filter\n");
                KeybindHUDContent.Append($"\n<color=yellow>[RMB] Remove Filter\n");               
            }
            else if ((int)HighlightTool_Core.Instance.CurrentMode == 1)
            {
                KeybindHUDContent.Append($"\n<color=yellow>[MW] Move Cutaway\n");
                KeybindHUDContent.Append($"\n<color=yellow>[R] Reset Cutaway\n");
                KeybindHUDContent.Append($"\n<color=yellow>[MMB] Invert Cutaway\n");
                KeybindHUDContent.Append($"\n<color=yellow>[LMB] Cycle Axis\n");
                KeybindHUDContent.Append($"\n<color=yellow>[RMB] Cycle Axis\n");             
            }
            else if ((int)HighlightTool_Core.Instance.CurrentMode == 2)
            {
                KeybindHUDContent.Append($"\n<color=yellow>[MW] Cycle Filter\n");
                KeybindHUDContent.Append($"\n<color=yellow>[R] Reset Filter\n");
                KeybindHUDContent.Append($"\n<color=yellow>[LMB] Apply Filter\n");
                KeybindHUDContent.Append($"\n<color=yellow>[RMB] Remove Filter\n");
            }

            if (KeybindHUD == null && HudAPI.Heartbeat)
            {
                KeybindHUD = new HUDMessage
                (
                    Message: KeybindHUDContent,
                    Origin: new Vector2D(0.1f, -0.3f),
                    TimeToLive: -1,
                    Scale: 0.65f,
                    HideHud: false,
                    Blend: BlendTypeEnum.PostPP,
                    Font: "monospace"
                );

                /* HighlighterHUD.Offset = HighlighterHUD.GetTextLength() / 2;*/
                KeybindHUD.Visible = true;
            }
            #endregion
        }

        public void Unload()
        {
            I = null;

            if (HudAPI.Heartbeat)
            {
                HudAPI.Unload();
                HudAPI = null;
            }
        }
        #endregion
    }
}
