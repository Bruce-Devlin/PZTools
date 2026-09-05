using System.Configuration;
using System.IO;
using Newtonsoft.Json;
using PZTools.Core.Models;

namespace PZTools.Core.Functions
{
    internal static class Config
    {
        public static async Task PrintAppSettings()
        {
            await Console.Log("PZTools App Settings:");
            bool appSettingsExisted = false;
            var appSettings = Config.GetAppSettings(out appSettingsExisted);
            if (appSettingsExisted)
                await Console.Log("App settings existed!");

            foreach (var appSetting in appSettings.GetAll())
            {
                await Console.Log($"- Setting: {appSetting.Name} = {appSetting.Value}");
            }
        }
        public static AppSettings GetAppSettings()
        {
            var didExist = false;
            return GetAppSettings(out didExist);
        }

        public static AppSettings GetAppSettings(out bool didExist)
        {
            AppSettings? appSetting = null;
            didExist = false;

            var savedSettings = GetVariable(VariableType.system, "appSettings");
            if (savedSettings != null)
            {
                try
                {
                    appSetting = JsonConvert.DeserializeObject<AppSettings>(savedSettings);
                    didExist = appSetting != null;
                }
                catch (JsonException ex)
                {
                    Console.Log($"App settings are invalid and defaults will be used: {ex.Message}", Console.LogLevel.Warning);
                }
            }
            else
                Console.Log("No app settings config found; using defaults.");

            if (appSetting == null)
            {
                appSetting = new AppSettings();
            }

            return appSetting;
        }

        public static T? GetAppSetting<T>(string name)
        {
            var appSettings = GetAppSettings();
            if (appSettings == null)
                return default;

            var property = appSettings.GetType().GetProperty(name);
            if (property == null)
                return default;

            var value = property.GetValue(appSettings);

            if (value is null)
                return default;
            if (value is T typed)
                return typed;
            return (T?)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }


        public static void SetAppSetting(string name, object? value)
        {
            var appSettings = GetAppSettings();

            var property = appSettings.GetType().GetProperty(name);
            if (property == null || !property.CanWrite)
                throw new ArgumentException($"Unknown app setting '{name}'.", nameof(name));

            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var converted = value is null || targetType.IsInstanceOfType(value)
                ? value
                : Convert.ChangeType(value, targetType);
            property.SetValue(appSettings, converted, null);

            var settingsToSave = JsonConvert.SerializeObject(appSettings);
            StoreVariable(VariableType.system, "appSettings", settingsToSave);
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
            if (settings[name] == null)
                settings.Add(name, data);
            else
                settings[name].Value = data;
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
            if (settings[variable] == null)
                return null;
            else
                return settings[variable].Value;
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
