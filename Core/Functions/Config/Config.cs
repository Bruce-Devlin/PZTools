using Newtonsoft.Json;
using PZTools.Core.Models;
using System.Configuration;
using System.IO;

namespace PZTools.Core.Functions
{
    internal static class Config
    {
        public static string GetAppSetting(string name)
        {
            var savedSettings = GetVariable(VariableType.system, "appSettings");
            var appSettings = JsonConvert.DeserializeObject<AppSettings>(savedSettings);

            if (appSettings != null)
            {
                var property = appSettings.GetType().GetProperty(name);
                if (property != null)
                {
                    var value = property.GetValue(appSettings);
                    return value?.ToString() ?? string.Empty;
                }
                else
                {
                    return string.Empty;
                }
            }
            else return string.Empty;
        }

        public static void SetAppSetting(string name, string value)
        {
            var savedSettings = GetVariable(VariableType.system, "appSettings");
            var appSettings = JsonConvert.DeserializeObject<AppSettings>(savedSettings);

            if (appSettings != null)
            {
                var property = appSettings.GetType().GetProperty(name);
                if (property != null)
                {
                    property.SetValue(appSettings, value, null);
                }
            }

            var settingToSave = JsonConvert.SerializeObject(appSettings);
            StoreVariable(VariableType.user, "appSettings", settingToSave);
        }

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
        public static void StoreVariable(VariableType type, string name, double data) => StoreVariable(type, name, data.ToString());
        public static void StoreVariable(VariableType type, string name, float data) => StoreVariable(type, name, data.ToString());
        public static void StoreObject(VariableType type, string name, object obj) => StoreVariable(type, name, JsonConvert.SerializeObject(obj));


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

        public static T? GetObject<T>(VariableType type, string name)
        {
            string? jsonString = GetVariable(type, name);
            if (jsonString == null)
                return default;

            return JsonConvert.DeserializeObject<T>(jsonString);
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
