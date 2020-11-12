using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace MysqlBackup.Models
{
    public class DbBackupTarget
    {
        private readonly string _connectionString;
        private readonly string _scratchPath;
        private readonly BehaviorSubject<double> _progressSubject;

        public DbBackupTarget(IConfiguration configuration, string scratchPath)
        {
            _scratchPath = scratchPath;
            _connectionString = configuration["ConnectionString"];
            _progressSubject = new BehaviorSubject<double>(0);
            
            Name = configuration["Name"];
            
            var invalids = Path.GetInvalidFileNameChars();
            SafeName = string.Join("_", Name.Split(invalids, StringSplitOptions.RemoveEmptyEntries))
                .TrimEnd('.').Trim(); 
            
            CheckForUpdate = configuration.GetValue<bool>("CheckForUpdate");
            Cron = configuration["Cron"];
            Expression = CronExpression.Parse(configuration["Cron"]);
        }
        
        public string Cron { get; }
        public string Name { get; }
        
        public bool IsRunning { get; private set; }
        
        public string SafeName { get; }
        
        public bool CheckForUpdate { get; }
        public CronExpression Expression { get; }

        public IObservable<double> Progress => _progressSubject.AsObservable();

        public DateTime? NextTime => Expression.GetNextOccurrence(DateTime.UtcNow);

        public TimeSpan? RunsIn => NextTime - DateTime.UtcNow;

        /// <summary>
        /// Schedule the next observable
        /// </summary>
        public void ScheduleNext()
        {
            var offset = RunsIn;
            if (offset == null)
            {
                throw new NullReferenceException($"Unable to compute the time offset for the backup target {Name}");
            }

            Observable.Timer(offset.Value)
                .Subscribe(l => RunJob());
        }

        /// <summary>
        /// Kicks off the backup task and reschedules the next observable. Is effectively an event handler, no caller
        /// ever needs to be waiting on this.
        /// </summary>
        private async void RunJob()
        {
            await PerformBackup().ConfigureAwait(false);

            ScheduleNext();
        }

        private async Task PerformBackup()
        {
            IsRunning = true;
            _progressSubject.OnNext(0);
            
            // Build the full connection string
            var connectionString = _connectionString + (_connectionString.EndsWith(";") ? string.Empty : ";") + 
                                   "charset=utf8;convertzerodatetime=true;";
            
            // Prepare the scratch directory and temporary file
            if (!Directory.Exists(_scratchPath))
            {
                Directory.CreateDirectory(_scratchPath);
            }

            var timeText = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm");
            var fileName = $"{SafeName}_{timeText}.sql";
            var filePath = Path.Combine(_scratchPath, fileName);

            using var connection = new MySqlConnection(connectionString);
            using var cmd = new MySqlCommand();
            using var backup = new MySqlBackup(cmd);

            backup.ExportProgressChanged += BackupOnExportProgressChanged;
            cmd.Connection = connection;
            await connection.OpenAsync();
            backup.ExportToFile(filePath);
            backup.ExportProgressChanged -= BackupOnExportProgressChanged;
            await connection.CloseAsync();
            
            IsRunning = false;
            _progressSubject.OnNext(100);
        }

        private void BackupOnExportProgressChanged(object sender, ExportProgressArgs e)
        {
            _progressSubject.OnNext(e.CurrentRowIndexInAllTables / e.TotalRowsInAllTables * 100.0);
        }
    }
}