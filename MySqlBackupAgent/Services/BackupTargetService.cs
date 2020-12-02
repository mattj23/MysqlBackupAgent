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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var environment = scope.ServiceProvider.GetService<IWebHostEnvironment>();
            var workingPath = Path.Combine(environment.WebRootPath, "scratch_dir");

            _logger.Log(LogLevel.Information, "Creating backup targets");
            foreach (var child in _section.GetChildren())
            {
                var target = new DbBackupTarget(child, workingPath, _scopeFactory);
                _logger.Log(LogLevel.Information, "Created target {0}", target.Name);
                _targets[target.Name] = target;
                _targets[target.Name].ScheduleNext();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
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