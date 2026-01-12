using System.Configuration;
using System.IO;

namespace PZTools.Core.Functions
{
    internal static class Config
    {


        /// <summary>
        /// Stores variable to config file
        /// </summary>
        /// <param name="type">Name of config file</param>
        /// <param name="name">Name of variable to store</param>
        /// <param name="data">Value to store</param>
        public static void StoreVariable(VariableType type, string name, string data)
        {
            ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = Path.Combine(AppPaths.ConfigsDirectory, $"{type}.config")
            };
            Configuration configuration = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = configuration.AppSettings.Settings;
            if (settings[name] == null) settings.Add(name, data);
            else settings[name].Value = data;
            configuration.Save(ConfigurationSaveMode.Modified);
        }
        public static void StoreVariable(VariableType type, string name, bool data) => StoreVariable(type, name, data.ToString().ToLower());
        public static void StoreVariable(VariableType type, string name, int data) => StoreVariable(type, name, data.ToString());



        /// <summary>
        /// Gets Variable if exists otherwise returns NULL
        /// </summary>
        /// <param name="type">Name of config file</param>
        /// <param name="variable">Name of variable you would like to retrieve</param>
        /// <returns></returns>
        public static string? GetVariable(VariableType type, string variable)
        {
            ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = Path.Combine(AppPaths.ConfigsDirectory, $"{type}.config")
            };
            Configuration configuration = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = configuration.AppSettings.Settings;
            if (settings[variable] == null) return null;
            else return settings[variable].Value;
        }

        /// <summary>
        /// Gets all variable values from the specified config file as a single list.
        /// </summary>
        /// <param name="type">Name of config file (without extension)</param>
        /// <returns>List of values, or empty if none found</returns>
        public static List<string> GetAllVariableValues(VariableType type)
        {
            var values = new List<string>();

            ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap
            {
                ExeConfigFilename = Path.Combine(AppPaths.ConfigsDirectory, $"{type}.config")
            };

            Configuration configuration = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = configuration.AppSettings.Settings;

            foreach (KeyValueConfigurationElement setting in settings)
            {
                values.Add(setting.Value);
            }

            return values;
        }


    }
}
