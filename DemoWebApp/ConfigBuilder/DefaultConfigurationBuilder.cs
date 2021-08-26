using System;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
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
                                     Environment.GetEnvironmentVariable("DEMO_ENVIRONMENT") ??
                                     DefaultEnvironmentName;

            IConfigurationBuilder builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", false, true)
                .AddJsonFile($"appsettings.{environmentName}.json", true, true)
                .AddEnvironmentVariables(source => source.Prefix = "DEMO_");

            if (environmentName.StartsWith("Development", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddUserSecrets<MvcApplication>();
            }
            else
            {
                // Set from either config or from env vars.
                // When I run this code, I use env vars to set the values after installing the certificate to 'Cert:\CurrentUser\My'
                // i.e. DEMO_ServicePrincipalId, DEMO_ServicePrincipalCertificateThumbprint, DEMO_TenantId
                IConfigurationRoot builtConfig = builder.Build();
                string vaultName = builtConfig["KeyVaultName"];
                string servicePrincipalId = builtConfig["ServicePrincipalId"];
                string servicePrincipalCertificateThumbprint = builtConfig["ServicePrincipalCertificateThumbprint"];
                string azureAdTenantId = builtConfig["TenantId"];

                using X509Store store = new X509Store(StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                X509Certificate2Collection certs = store.Certificates.Find(
                    X509FindType.FindByThumbprint,
                    servicePrincipalCertificateThumbprint, false);

                SecretClient secretClient = new SecretClient(
                    new Uri($"https://{vaultName}.vault.azure.net/"),
                    new ClientCertificateCredential(
                        azureAdTenantId,
                        servicePrincipalId,
                        certs.OfType<X509Certificate2>().Single()));

                // See: https://docs.microsoft.com/en-us/aspnet/core/security/key-vault-configuration?view=aspnetcore-2.2#use-managed-identities-for-azure-resources-1
                // Secret Names are like .Net Core Json Flattening. Except they use '--'.
                builder.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
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

            appSettingsConfigSection.GetReloadToken()
                .RegisterChangeCallback(AppSettingsSectionChanged, appSettingsConfigSection);

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
