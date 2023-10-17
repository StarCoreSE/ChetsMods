using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI.Ingame.Utilities; // this ingame namespace is safe to use in mods as it has nothing to collide with
using VRage.Utils;

namespace InventoryTether.Config
{
    // This example is minimal code required for it to work and with comments so you can better understand what is going on.

    // The gist of it is: ini file is loaded/created that admin can edit, SetVariable is used to store that data in sandbox.sbc which gets automatically sent to joining clients.
    // Benefit of this is clients will be getting this data before they join, very good if you need it during LoadData()
    // This example does not support reloading config while server runs, you can however implement that by sending a packet to all online players with the ini data for them to parse.

    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class Tether_BasicConfig : MySessionComponentBase
    {
        Tether_ConfigSettings Config = new Tether_ConfigSettings();

        public override void LoadData()
        {
            Config.Load();

            // example usage/debug
            MyLog.Default.WriteLineAndConsole($"### SomeNumber value={Config.MinimumPowerRequirement}");
            MyLog.Default.WriteLineAndConsole($"### ToggleThings value={Config.MaximumPowerRequirement}");
            MyLog.Default.WriteLineAndConsole($"### ToggleThings value={Config.MinBlockRange}");
            MyLog.Default.WriteLineAndConsole($"### ToggleThings value={Config.MaxBlockRange}");
            MyLog.Default.WriteLineAndConsole($"### ToggleThings value={Config.MinStockAmount}");
            MyLog.Default.WriteLineAndConsole($"### ToggleThings value={Config.MaxStockAmount}");
        }
    }

    public class Tether_ConfigSettings
    {
        const string VariableId = nameof(Tether_BasicConfig); // IMPORTANT: must be unique as it gets written in a shared space (sandbox.sbc)
        const string FileName = "Quantum_Tether_Config.ini"; // the file that gets saved to world storage under your mod's folder
        const string IniSection = "Config";

        // settings you'd be reading, and their defaults.
        public float MinimumPowerRequirement = 1.0f;
        public float MaximumPowerRequirement = 50f;

        public float MinBlockRange = 5;
        public float MaxBlockRange = 500;

        public float MinStockAmount = 1;
        public float MaxStockAmount = 50;

        void LoadConfig(MyIni iniParser)
        {
            // repeat for each setting field
            MinimumPowerRequirement = iniParser.Get(IniSection, nameof(MinimumPowerRequirement)).ToSingle(MinimumPowerRequirement);
            MaximumPowerRequirement = iniParser.Get(IniSection, nameof(MaximumPowerRequirement)).ToSingle(MaximumPowerRequirement);
            MinBlockRange = iniParser.Get(IniSection, nameof(MinBlockRange)).ToSingle(MinBlockRange);
            MaxBlockRange = iniParser.Get(IniSection, nameof(MaxBlockRange)).ToSingle(MaxBlockRange);
            MinStockAmount = iniParser.Get(IniSection, nameof(MinStockAmount)).ToSingle(MinStockAmount);
            MaxStockAmount = iniParser.Get(IniSection, nameof(MaxStockAmount)).ToSingle(MaxStockAmount);
        }

        void SaveConfig(MyIni iniParser)
        {
            // repeat for each setting field
            iniParser.Set(IniSection, nameof(MinimumPowerRequirement), MinimumPowerRequirement);
            iniParser.SetComment(IniSection, nameof(MinimumPowerRequirement), "Default: 1.0f - Minimum Required Power at the Lowest Range in MW"); // optional

            iniParser.Set(IniSection, nameof(MaximumPowerRequirement), MaximumPowerRequirement);
            iniParser.SetComment(IniSection, nameof(MaximumPowerRequirement), "Default: 50.0f - Maximum Required Power at the Longest Range in MW"); // optional

            iniParser.Set(IniSection, nameof(MinBlockRange), MinBlockRange);
            iniParser.SetComment(IniSection, nameof(MinBlockRange), "Default: 5 - Minimum Block Range in Meters as a Diameter"); // optional

            iniParser.Set(IniSection, nameof(MaxBlockRange), MaxBlockRange);
            iniParser.SetComment(IniSection, nameof(MaxBlockRange), "Default: 500 - Maximum Block Range in Meters as a Diameter"); // optional

            iniParser.Set(IniSection, nameof(MinStockAmount), MinStockAmount);
            iniParser.SetComment(IniSection, nameof(MinStockAmount), "Default: 1 - Minimum Stock Amount for Components"); // optional

            iniParser.Set(IniSection, nameof(MaxStockAmount), MaxStockAmount);
            iniParser.SetComment(IniSection, nameof(MaxStockAmount), "Default: 50 - Maximum Stock Amount for Components"); // optional

        }

        // nothing to edit below this point

        public Tether_ConfigSettings()
        {
        }

        public void Load()
        {
            if(MyAPIGateway.Session.IsServer)
                LoadOnHost();
            else
                LoadOnClient();
        }

        void LoadOnHost()
        {
            MyIni iniParser = new MyIni();

            // load file if exists then save it regardless so that it can be sanitized and updated

            if(MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, typeof(Tether_ConfigSettings)))
            {
                using(TextReader file = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(Tether_ConfigSettings)))
                {
                    string text = file.ReadToEnd();

                    MyIniParseResult result;
                    if(!iniParser.TryParse(text, out result))
                        throw new Exception($"Config error: {result.ToString()}");

                    LoadConfig(iniParser);
                }
            }

            iniParser.Clear(); // remove any existing settings that might no longer exist

            SaveConfig(iniParser);

            string saveText = iniParser.ToString();

            MyAPIGateway.Utilities.SetVariable<string>(VariableId, saveText);

            using(TextWriter file = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(Tether_ConfigSettings)))
            {
                file.Write(saveText);
            }
        }

        void LoadOnClient()
        {
            string text;
            if(!MyAPIGateway.Utilities.GetVariable<string>(VariableId, out text))
                throw new Exception("No config found in sandbox.sbc!");

            MyIni iniParser = new MyIni();
            MyIniParseResult result;
            if(!iniParser.TryParse(text, out result))
                throw new Exception($"Config error: {result.ToString()}");

            LoadConfig(iniParser);
        }
    }
}