using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;

namespace StarCore.Highlights
{
    public class HUDManager
    {
        public static HUDManager I = new HUDManager();

        public void Init()
        {
            I = this;

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
    }
}
