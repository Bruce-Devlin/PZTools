using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PZTools.Core.Functions.Zomboid
{
    internal class ZomboidGame
    {
        public static string GameDirectory 
        { 
            get 
            {
                if (GameMode == "existing")
                    return Config.GetVariable(VariableType.system, "ExistingGamePath");
                else
                    return Config.GetVariable(VariableType.system, "ManagedGamePath");
            }
        }

        public static string GameUserDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zomboid");
            }
        }

        public static string GameMode 
        { 
            get
            {
                return Config.GetVariable(VariableType.system, "GameMode");
            } 
        }
    }
}
