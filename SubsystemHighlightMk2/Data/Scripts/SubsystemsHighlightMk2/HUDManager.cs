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

        HUDMessage HighlighterHUD;
        StringBuilder HighlighterHUDContent;

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
            if (!ToolEquipped)
            {
                if (HighlighterHUDContent != null)
                    HighlighterHUDContent.Clear();

                return;
            }

            if (HighlighterHUDContent == null)
            {
                HighlighterHUDContent = new StringBuilder();
            }
            HighlighterHUDContent.Clear();

            var activeMode = HighlightTool_Core.Instance.CurrentMode.ToString();
            HighlighterHUDContent.Append($"<color=yellow>Mode: <color=white>{activeMode}\n");      

            if (HighlightTool_Core.Instance.CurrentMode == 0)
            {
                var selectedHighlight = HighlightManager.I.CurrentFilter.Name;

                HighlighterHUDContent.Append($"\n<color=yellow>Filter: <color=white>{selectedHighlight} \n");

                IMyCubeGrid grid = HighlightTool_Core.Instance.CastForGrid();
                long entityId = grid != null ? grid.EntityId : 0;
                var currentTarget = grid != null ? grid.DisplayName : "None";

                HighlighterHUDContent.Append($"\n<color=yellow>Current Target:\n<color=white>{currentTarget}\n");

                Dictionary<HighlightFilter, bool> activeHighlightsDict;
                if(HighlightManager.I.ActiveHighlights.TryGetValue(entityId, out activeHighlightsDict))
                {
                    string filterNames = string.Join("\n", activeHighlightsDict.Keys.Select(f => f.Name));
                    HighlighterHUDContent.Append($"\n<color=yellow>Applied Filters:\n<color=white>{filterNames}");
                }
                else if (grid != null)         
                    HighlighterHUDContent.Append($"\n<color=yellow>No Applied Filters");               
            }
            else if ((int)HighlightTool_Core.Instance.CurrentMode == 1)
            {
                var currentAxis = CutawayManager.I.CutawayAxis.ToString();
                HighlighterHUDContent.Append($"\n<color=yellow>Axis: <color=white>{currentAxis}\n");

                IMyCubeGrid grid = HighlightTool_Core.Instance.CastForGrid();
                var currentTarget = grid != null ? grid.DisplayName : "None";
                HighlighterHUDContent.Append($"\n<color=yellow>Current Target:\n<color=white>{currentTarget}\n");

                if (grid != null)
                {
                    var currentPosition = CutawayManager.I.CutawayPosition.ToString();
                    HighlighterHUDContent.Append($"\n<color=yellow>Position: <color=white>{currentPosition}\n");

                    HighlighterHUDContent.Append($"\n<color=yellow>Inverted: <color=white>{CutawayManager.I.IsNormalInverted.ToString()}\n");
                }           
            }
            else if ((int)HighlightTool_Core.Instance.CurrentMode == 2)
            {

            }

            if (HighlighterHUD == null && HudAPI.Heartbeat)
            {
                HighlighterHUD = new HUDMessage
                (
                    Message: HighlighterHUDContent,
                    Origin: new Vector2D(-0.3f, -0.3f),
                    TimeToLive: -1,
                    Scale: 0.75f,
                    HideHud: false,
                    Blend: BlendTypeEnum.PostPP,
                    Font: "monospace"
                );

               /* HighlighterHUD.Offset = HighlighterHUD.GetTextLength() / 2;*/
                HighlighterHUD.Visible = true;
            }

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
