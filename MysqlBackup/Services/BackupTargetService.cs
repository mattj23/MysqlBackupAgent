using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MysqlBackup.Models;

namespace MysqlBackup.Services
{
    public class BackupTargetService
    {
        private readonly ConcurrentDictionary<string, DbBackupTarget> _targets;
        
        public BackupTargetService(IConfiguration section)
        {
            _targets = new ConcurrentDictionary<string, DbBackupTarget>();
            foreach (var child in section.GetChildren())
            {
                var target = new DbBackupTarget(child);
                _targets[target.Name] = target;
            }
        }

        public IReadOnlyDictionary<string, DbBackupTarget> Targets => _targets;

    }

    public static class Extension
    {
        public static void AddBackupTargets(this IServiceCollection services, IConfiguration config)
        {
            var targetService = new BackupTargetService(config);
            services.AddSingleton(targetService);
        }
    }
}