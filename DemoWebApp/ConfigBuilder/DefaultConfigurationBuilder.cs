using System;
using System.Configuration;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using ConfigurationBuilder = System.Configuration.ConfigurationBuilder;
using ConfigurationSection = System.Configuration.ConfigurationSection;

namespace DemoWebApp.ConfigBuilder
{
    public class DefaultConfigurationBuilder : ConfigurationBuilder
    {
#if DEBUG
        private const string DefaultEnvironmentName = "Development";
#else
        private const string DefaultEnvironmentName = "Production";
#endif

        private readonly Lazy<IConfigurationRoot> Configuration = new Lazy<IConfigurationRoot>(() =>
        {
            string environmentName = Environment.GetEnvironmentVariable("ASPNET_ENVIRONMENT") ??
                                     Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ??
                                     DefaultEnvironmentName;

            IConfigurationBuilder builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            if (environmentName.StartsWith("Development", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddUserSecrets<MvcApplication>();
            }

            return builder.Build();
        });

        public override ConfigurationSection ProcessConfigurationSection(ConfigurationSection configSection)
        {
            switch (configSection)
            {
                case AppSettingsSection appSettingsSection:
                    return ProcessAppSettingsSection(appSettingsSection);
                case ConnectionStringsSection connectionStringsSection:
                    return ProcessConnectionStringsSection(connectionStringsSection);
                default:
                    return base.ProcessConfigurationSection(configSection);
            }
        }

        private AppSettingsSection ProcessAppSettingsSection(AppSettingsSection section)
        {
            IConfigurationSection appSettingsConfigSection = Configuration.Value.GetSection("AppSettings");
            if (appSettingsConfigSection == null)
            {
                return section;
            }

            foreach (IConfigurationSection setting in appSettingsConfigSection.GetChildren())
            {
                // keys that contain subsections have a null value
                if (setting.Value == null)
                {
                    continue;
                }

                if (section.Settings[setting.Key] is KeyValueConfigurationElement existingElement)
                {
                    existingElement.Value = setting.Value;
                }
                else
                {
                    section.Settings.Add(setting.Key, setting.Value);
                }
            }

            appSettingsConfigSection.GetReloadToken().RegisterChangeCallback(AppSettingsSectionChanged, appSettingsConfigSection);

            return section;
        }

        private static void AppSettingsSectionChanged(object configSectionObj)
        {
            IConfigurationSection configSection = (IConfigurationSection)configSectionObj;
            foreach (IConfigurationSection setting in configSection.GetChildren())
            {
                if (setting.Value == null)
                {
                    continue;
                }

                ConfigurationManager.AppSettings.Set(setting.Key, setting.Value);
            }

            configSection.GetReloadToken().RegisterChangeCallback(AppSettingsSectionChanged, configSection);
        }

        private ConnectionStringsSection ProcessConnectionStringsSection(ConnectionStringsSection section)
        {
            IConfigurationSection connectionStringsSection = Configuration.Value.GetSection("ConnectionStrings");
            if (connectionStringsSection == null)
            {
                return section;
            }

            ConnectionStringSettingsCollection connectionStringSettingsCollection = section.ConnectionStrings;

            foreach (IConfigurationSection setting in connectionStringsSection.GetChildren())
            {
                string key = setting.Key;
                string value;
                string providerName = null;
                // keys that contain subsections have a null value
                if (setting.Value == null)
                {
                    value = setting["ConnectionString"];
                    providerName = setting["ProviderName"];
                }
                else
                {
                    value = setting.Value;
                }

                if (value == null)
                {
                    continue;
                }

                if (connectionStringSettingsCollection[key] is ConnectionStringSettings existingConnectionString)
                {
                    existingConnectionString.ConnectionString = value;
                    if (providerName != null)
                    {
                        existingConnectionString.ProviderName = providerName;
                    }
                }
                else
                {
                    connectionStringSettingsCollection.Add(providerName != null
                        ? new ConnectionStringSettings(key, value, providerName)
                        : new ConnectionStringSettings(key, value));
                }
            }

            return section;
        }
    }
}
