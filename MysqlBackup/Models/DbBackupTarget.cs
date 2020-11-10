using System;
using Cronos;
using Microsoft.Extensions.Configuration;

namespace MysqlBackup.Models
{
    public class DbBackupTarget
    {
        private string _connectionString;

        public DbBackupTarget(IConfiguration configuration)
        {
            _connectionString = configuration["ConnectionString"];
            Name = configuration["Name"];
            CheckForUpdate = configuration.GetValue<bool>("CheckForUpdate");
            Cron = configuration["Cron"];
            Expression = CronExpression.Parse(configuration["Cron"]);
        }
        
        public string Cron { get; }
        public string Name { get; }
        public bool CheckForUpdate { get; }
        public CronExpression Expression { get; }

        public DateTime? NextTime => Expression.GetNextOccurrence(DateTime.UtcNow);

        public TimeSpan? RunsIn => NextTime - DateTime.UtcNow;

        public string RunsInText
        {
            get
            {
                var r = RunsIn;
                if (r == null) return "N/A";

                var days = r.Value.Days > 0 ? $"{r.Value.Days} days, ": "";
                return $"{days}{r.Value.Hours} hours, {r.Value.Minutes} minutes, {r.Value.Seconds} seconds";
            }
        }

    }
}