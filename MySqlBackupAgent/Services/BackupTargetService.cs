using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlBackupAgent.Models;

namespace MySqlBackupAgent.Services
{
    public class BackupTargetService : IHostedService
    {
        private readonly ConcurrentDictionary<string, DbBackupTarget> _targets;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private IConfiguration _section;
        
        public BackupTargetService(IConfiguration section, IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _section = section;
            _targets = new ConcurrentDictionary<string, DbBackupTarget>();
        }

        public IReadOnlyDictionary<string, DbBackupTarget> Targets => _targets;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var environment = scope.ServiceProvider.GetService<IWebHostEnvironment>();
            var workingPath = Path.Combine(environment.WebRootPath, "scratch_dir");

            _logger.Log(LogLevel.Information, "Creating backup targets");
            foreach (var child in _section.GetChildren())
            {
                // Construct the backup collection
                var backupCollection = new BackupCollection(child.Key, _scopeFactory, _logger);
                await backupCollection.GetExistingBackups();
                
                // Construct the backup target itself
                var target = new DbBackupTarget(child.Key, child, workingPath, backupCollection);
                
                _logger.Log(LogLevel.Information, "Created target {0}", target.Name);
                _targets[child.Key] = target;
                _targets[child.Key].ScheduleNext();
            }

        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static string SanitizeName(string name)
        {
            var invalids = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries))
                .TrimEnd('.').Trim().Replace(" ", "_"); 
        }
    }

    public static class Extension
    {
        public static void AddBackupTargets(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton(x => new BackupTargetService(config, 
                x.GetService<IServiceScopeFactory>(), 
                x.GetService<ILogger<BackupTargetService>>()));
            services.AddHostedService(x => x.GetService<BackupTargetService>());
        }
    }
}