using System;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Cronos;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using MySqlBackupAgent.Services;

namespace MySqlBackupAgent.Models
{
    public class DbBackupTarget
    {
        private readonly string _connectionString;
        private readonly string _scratchPath;
        private TargetState _state;
        
        /// <summary>
        /// A behavior subject used to emit progress updates
        /// </summary>
        private readonly BehaviorSubject<double> _progressSubject;

        /// <summary>
        /// A subject used to emit updates when the state changes
        /// </summary>
        private readonly Subject<TargetState> _stateSubject;

        /// <summary>
        /// A subject used to emit updates when the next scheduled time changes
        /// </summary>
        private readonly BehaviorSubject<DateTime> _nextTimeSubject;
        
        /// <summary>
        /// The subscription for the scheduled backup job
        /// </summary>
        private IDisposable _subscription;

        private readonly IServiceScopeFactory _scopeFactory;
        

        public DbBackupTarget(IConfiguration configuration, string scratchPath, IServiceScopeFactory scopeFactory)
        {
            _scratchPath = scratchPath;
            _scopeFactory = scopeFactory;
            _connectionString = configuration["ConnectionString"];
            _progressSubject = new BehaviorSubject<double>(0);
            _nextTimeSubject = new BehaviorSubject<DateTime>(default);
            _stateSubject = new Subject<TargetState>();
            
            Name = configuration["Name"];
            
            var invalids = Path.GetInvalidFileNameChars();
            SafeName = string.Join("_", Name.Split(invalids, StringSplitOptions.RemoveEmptyEntries))
                .TrimEnd('.').Trim().Replace(" ", "_"); 
            
            CheckForUpdate = configuration.GetValue<bool>("CheckForUpdate");
            Expression = CronExpression.Parse(configuration["Cron"]);
        }
        
        /// <summary>
        /// The name of the backup target
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// Gets the current state of the target. Triggers StateChange when set.
        /// </summary>
        public TargetState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                _stateSubject.OnNext(_state);
            }
        }
        
        public string SafeName { get; }
        
        public bool CheckForUpdate { get; }
        public CronExpression Expression { get; }

        public DateTime? NextTime => Expression.GetNextOccurrence(DateTime.UtcNow);

        public TimeSpan? RunsIn => NextTime - DateTime.UtcNow;


        public IObservable<double> Progress => _progressSubject.AsObservable();

        public IObservable<TargetState> StateChange => _stateSubject.AsObservable();

        public IObservable<DateTime> ScheduledChange => _nextTimeSubject.AsObservable();
        
        /// <summary>
        /// Schedule the next job and save the subscription. If an existing subscription already exists we will dispose
        /// of it before creating the new one.
        /// </summary>
        public void ScheduleNext()
        {
            var offset = RunsIn;
            if (offset == null)
            {
                throw new NullReferenceException($"Unable to compute the time offset for the backup target {Name}");
            }
            
            // Throw away the existing subscription if it exists
            _subscription?.Dispose();

            // Subscribe to the next scheduled time
            _subscription = Observable.Timer(offset.Value) .Subscribe(l => RunJob());
            if (NextTime != null) _nextTimeSubject.OnNext(NextTime.Value);
        }

        /// <summary>
        /// Kicks off the backup task and reschedules the next observable. Is effectively an event handler, no caller
        /// ever needs to be waiting on this.
        /// </summary>
        private async void RunJob()
        {
            _subscription?.Dispose();
            await PerformBackup().ConfigureAwait(false);

            ScheduleNext();
        }

        private async Task PerformBackup()
        {
            // Prepare the scratch directory and temporary file
            if (!Directory.Exists(_scratchPath))
            {
                Directory.CreateDirectory(_scratchPath);
            }

            var timeText = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm");
            var fileName = $"{SafeName}_{timeText}.sql";
            var filePath = Path.Combine(_scratchPath, fileName);

            // Dump to the file
            await DumpFromMySql(filePath);
            
            // Compress file
            var compressedPath = await CompressFile(filePath);
            
            // Upload
            State = TargetState.UploadingToStorage;
            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetService<IStorageService>();
            await storage.UploadFile(compressedPath);
            

            State = TargetState.Scheduled;
            _progressSubject.OnNext(100);
        }

        private async Task DumpFromMySql(string filePath)
        {
            State = TargetState.BackingUp;
            _progressSubject.OnNext(0);
            
            // Dump to file from MySQL
            // Build the full connection string
            var connectionString = _connectionString + (_connectionString.EndsWith(";") ? string.Empty : ";") + 
                                   "charset=utf8;convertzerodatetime=true;";

            await using var connection = new MySqlConnection(connectionString);
            await using var cmd = new MySqlCommand();
            using var backup = new MySqlBackup(cmd);

            backup.ExportProgressChanged += BackupOnExportProgressChanged;
            cmd.Connection = connection;
            await connection.OpenAsync();
            backup.ExportToFile(filePath);
            backup.ExportProgressChanged -= BackupOnExportProgressChanged;
            await connection.CloseAsync();
        }

        private async Task<string> CompressFile(string filePath)
        {
            State = TargetState.Compressing;
            var compressedPath = filePath + ".gz";
            using var outputFileStream = File.OpenWrite(compressedPath);
            using var inputFileStream = File.OpenRead(filePath);
            using var writeStream = new GZipOutputStream(outputFileStream);
            
            var buffer = new byte[1024 * 10000];
            int bytesRead;
            int totalRead = 0;
            while ((bytesRead = await inputFileStream.ReadAsync(buffer)) > 0)
            {
                totalRead += bytesRead;
                writeStream.Write(buffer);
                
                _progressSubject.OnNext(100.0 * (double) totalRead / (double) inputFileStream.Length);
            }

            return compressedPath;
        }
        
        private void BackupOnExportProgressChanged(object sender, ExportProgressArgs e)
        {
            var current = (double) e.CurrentRowIndexInAllTables;
            var all = (double) e.TotalRowsInAllTables;
            _progressSubject.OnNext(100.0 * current / all);
        }
    }
}