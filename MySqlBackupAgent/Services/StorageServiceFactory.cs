using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace MySqlBackupAgent.Services
{
    public static class StorageServiceFactory
    {
        
        public static IStorageService Build(IConfiguration config)
        {
            var builders = new Dictionary<string, Func<IConfiguration, MinioFileService>> {{"S3", MinioFromConfig}};

            string serviceType = config["Type"];
            if (!builders.ContainsKey(serviceType))
            {
                throw new ArgumentException($"The storage type '{serviceType}' was unrecognized.");
            }

            return builders[serviceType](config);
        }

        private static MinioFileService MinioFromConfig(IConfiguration config)
        {
            var settings = config.Get<MinioSettings>();
            return new MinioFileService(settings);
        }
    }
}