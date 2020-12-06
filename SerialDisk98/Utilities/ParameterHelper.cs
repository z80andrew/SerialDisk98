using AtariST.SerialDisk98.Common;
using AtariST.SerialDisk98.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;

namespace SerialDiskXtreme.Utilities
{
    public static class ParameterHelper
    {
        public static ApplicationSettings MapConfigFiles(ApplicationSettings applicationSettings)
        {
            XmlDocument settingsXml = new XmlDocument();

            // Default resource file config

            var resourceFiles = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            var defaultConfigName = "Resources.serialdisk_default_config.xml";
            string defaultConfigResourceName = null;

            foreach(var resourceName in resourceFiles)
            {
                if (resourceName.Contains(defaultConfigName)) defaultConfigResourceName = resourceName;
            }

            if (!string.IsNullOrEmpty(defaultConfigResourceName))
            {
                using (var defaultConfigStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(defaultConfigResourceName))
                {
                    settingsXml.Load(defaultConfigStream);
                    MapXmlForObject(applicationSettings.SerialSettings, settingsXml, nameof(applicationSettings.SerialSettings), Constants.ConsoleParameterMappings);
                    MapXmlForObject(applicationSettings.DiskSettings, settingsXml, nameof(applicationSettings.DiskSettings), Constants.ConsoleParameterMappings);
                    MapXmlForObject(applicationSettings, settingsXml, null, Constants.ConsoleParameterMappings);
                }
            }

            // Config file on disk

            var configFilePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\serialdisk_config.xml";

            if (File.Exists(configFilePath))
            {
                using (var configFileStream = File.OpenRead(configFilePath))
                {
                    settingsXml.Load(configFileStream);
                    MapXmlForObject(applicationSettings.SerialSettings, settingsXml, nameof(applicationSettings.SerialSettings), Constants.ConsoleParameterMappings);
                    MapXmlForObject(applicationSettings.DiskSettings, settingsXml, nameof(applicationSettings.DiskSettings), Constants.ConsoleParameterMappings);
                    MapXmlForObject(applicationSettings, settingsXml, null, Constants.ConsoleParameterMappings);
                }
            }
            return applicationSettings;
        }

        public static ApplicationSettings MapConsoleParameters(ApplicationSettings applicationSettings, string[] args)
        {
            // Console parameters

            MapArgumentsForObject(applicationSettings.SerialSettings, nameof(applicationSettings.SerialSettings), Constants.ConsoleParameterMappings, args);
            MapArgumentsForObject(applicationSettings.DiskSettings, nameof(applicationSettings.DiskSettings), Constants.ConsoleParameterMappings, args);
            MapArgumentsForObject(applicationSettings, null, Constants.ConsoleParameterMappings, args);

            return applicationSettings;
        }

        private static void MapXmlForObject<T>(T settingsObject, XmlDocument xmlDoc, string settingsPrefix, Dictionary<string, string> parameterMappings)
        {
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            string xPathString = "//configuration";
            if(!String.IsNullOrEmpty(settingsPrefix)) xPathString += $"/{ settingsPrefix}";
            XmlNode xmlNode = xmlDoc.DocumentElement.SelectSingleNode(xPathString, nsmgr);

            if(xmlNode != null)
            {
                foreach (KeyValuePair<string, string> keyValuePair in parameterMappings)
                {
                    string propertyName = null;

                    // Property name prefixed parameters
                    if (settingsPrefix != null && keyValuePair.Value.Contains(settingsPrefix))
                        propertyName = keyValuePair.Value.Replace($"{ settingsPrefix}:", string.Empty);

                    // Base parameters
                    else if (settingsPrefix == null && !keyValuePair.Value.Contains(":"))
                        propertyName = keyValuePair.Value;

                    if (propertyName != null)
                    {
                        var propertyValue = xmlNode.SelectSingleNode(propertyName, nsmgr)?.InnerText;                        
                        if(propertyValue != null) SetPropertyValue(settingsObject, propertyName, propertyValue);
                    }
                }
            }            
        }

        private static void MapArgumentsForObject<T>(T settingsObject, string settingsPrefix, Dictionary<string,string> parameterMappings, string[] args)
        {
            foreach (KeyValuePair<string, string> keyValuePair in parameterMappings)
            {
                int argIndex = -1;
                string propertyName = null;

                // Property name prefixed parameters
                if (settingsPrefix != null && keyValuePair.Value.Contains(settingsPrefix))
                    propertyName = keyValuePair.Value.Replace($"{ settingsPrefix}:", string.Empty);

                // Base parameters
                else if(settingsPrefix == null && !keyValuePair.Value.Contains(":"))
                    propertyName = keyValuePair.Value;

                if (propertyName != null)
                {
                    if((argIndex = Array.FindIndex(args, arg => arg == keyValuePair.Key)) != -1)
                        SetPropertyValue(settingsObject, propertyName, args[argIndex + 1]);
                }
            }
        }

        private static void SetPropertyValue<T>(T settingsObject, string propertyName, string propertyValue)
        {
            var pinfo = typeof(T).GetProperty(propertyName);

            if (pinfo.PropertyType.BaseType == typeof(Enum))
                pinfo.SetValue(settingsObject, Enum.Parse(pinfo.PropertyType, propertyValue), null);

            else if (pinfo.PropertyType == typeof(bool))
                pinfo.SetValue(settingsObject, Boolean.Parse(propertyValue), null);

            else if (pinfo.PropertyType == typeof(int))
                pinfo.SetValue(settingsObject, Int32.Parse(propertyValue), null);

            else if (pinfo.PropertyType == typeof(string))
                pinfo.SetValue(settingsObject, propertyValue, null);
        }
    }
}