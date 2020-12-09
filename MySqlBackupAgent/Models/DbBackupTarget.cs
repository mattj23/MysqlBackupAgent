using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Cronos;
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
        /// A behavior subject used to emit information messages regarding the target
        /// </summary>
        private readonly BehaviorSubject<string> _infoMessageSubject;

        /// <summary>
        /// A subject used to emit updates when the next scheduled time changes
        /// </summary>
        private readonly BehaviorSubject<DateTime> _nextTimeSubject;
        
        /// <summary>
        /// The subscription for the scheduled backup job
        /// </summary>
        private IDisposable _subscription;

        public DbBackupTarget(string key, IConfiguration configuration, string scratchPath, BackupCollection backups)
        {
            Key = key;
            _scratchPath = scratchPath;
            _connectionString = configuration["ConnectionString"];
            _progressSubject = new BehaviorSubject<double>(0);
            _nextTimeSubject = new BehaviorSubject<DateTime>(default);
            _infoMessageSubject = new BehaviorSubject<string>(string.Empty);
            _stateSubject = new Subject<TargetState>();

            Backups = backups;
            
            Name = configuration["Name"];
            
            var invalids = Path.GetInvalidFileNameChars();
            
            CheckForUpdate = configuration.GetValue<bool>("CheckForUpdate");
            CronText = configuration["Cron"];
            Expression = CronExpression.Parse(CronText);
        }
        
        /// <summary>
        /// The name of the backup target
        /// </summary>
        public string Name { get; }
        
        public string Key { get; }
        
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
        
        public string CronText { get; }

        public bool CheckForUpdate { get; }
        public CronExpression Expression { get; }

        public DateTime? NextTime => Expression.GetNextOccurrence(DateTime.UtcNow);

        public TimeSpan? RunsIn => NextTime - DateTime.UtcNow;

        public IObservable<double> Progress => _progressSubject.AsObservable();

        public IObservable<TargetState> StateChange => _stateSubject.AsObservable();

        public IObservable<DateTime> ScheduledChange => _nextTimeSubject.AsObservable();

        public IObservable<string> InfoMessages => _infoMessageSubject.AsObservable();
        
        public BackupCollection Backups { get; }
        
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
        public async void RunJob(bool force=false)
        {
            _subscription?.Dispose();
            _infoMessageSubject.OnNext(string.Empty);
            
            try
            {
                await PerformBackup(force).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // TODO: Logging
                Console.WriteLine(e);
                _infoMessageSubject.OnNext("Error occurred while trying to perform backup"); 
            }

            ScheduleNext();
        }

        /// <summary>
        /// Performs a backup
        /// </summary>
        /// <param name="force">If true, this will ignore the changed check</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private async Task PerformBackup(bool force)
        {
            // Prepare the scratch directory and temporary file
            if (!Directory.Exists(_scratchPath))
            {
                Directory.CreateDirectory(_scratchPath);
            }

            // Build the full connection string with the extra parameters recommended
            // by the MySqlBackup.NET author
            var connectionString = _connectionString + (_connectionString.EndsWith(";") ? string.Empty : ";") + 
                                   "charset=utf8;convertzerodatetime=true;";

            // Establish the connection to the database
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Get the current database timestamp
            var timeStamp = await CurrentTimestamp(connection);
            if (!timeStamp.HasValue)
            {
                throw new Exception($"Could not get the timestamp for the database target '{Name}'");
            }

            // Construct the file name and path. The filename will simply be a uniquely generated string, since the
            // backup collection is responsible for handling the final naming and organization
            var scratchName = Guid.NewGuid().ToString().Replace("-", "") + ".sql";
            var filePath = Path.Combine(_scratchPath, scratchName);
            string compressedPath = null;
            
            // There are two cases which, if both are true, we might skip this backup. The first is if the user has to
            // have enabled the "CheckForUpdate" option on this target, in which case we can see if the database has 
            // not been updated more recently than the last backup.  However, the "force" option needs to be off, 
            // otherwise we will disregard this check.
            if (CheckForUpdate && !force)
            {
                // Get the timestamp of the last update to any of the database's tables
                string query = $"select max(update_time) from information_schema.tables where TABLE_SCHEMA='{connection.Database}'";
                var lastUpdate = await FromQuery(connection, query);
                if (!lastUpdate.HasValue)
                {
                    throw new Exception($"Could not get the last update time for the database target '{Name}'");
                }

                // Check to see if the current update timestamp in the database (lastUpdate) is older than the 
                // most recent backup.  If it is, the database hasn't been updated and we can abort the backup.
                if (Backups.HasMoreRecentThan(lastUpdate.Value))
                {
                    _infoMessageSubject.OnNext("Database has not been updated since last backup."); 
                    State = TargetState.Scheduled;
                    return;
                }
            }

            try
            {
                // Dump to the file
                await DumpFromMySql(connection, filePath);

                // Compress file
                compressedPath = await CompressFile(filePath);

                // Upload
                State = TargetState.UploadingToStorage;
                await Backups.AddBackup(new FileInfo(compressedPath), timeStamp.Value);

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                // Clean up after the files
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                if (File.Exists(compressedPath))
                {
                    File.Delete(compressedPath);
                }
            }

            State = TargetState.Scheduled;
            _progressSubject.OnNext(100);
        }

        private async Task DumpFromMySql(MySqlConnection connection, string filePath)
        {
            // Push out the state information for anyone who's watching
            State = TargetState.BackingUp;
            _progressSubject.OnNext(0);
            
            await using var cmd = new MySqlCommand();
            using var backup = new MySqlBackup(cmd);

            backup.ExportProgressChanged += BackupOnExportProgressChanged;
            cmd.Connection = connection;
            backup.ExportToFile(filePath);
            backup.ExportProgressChanged -= BackupOnExportProgressChanged;
            await connection.CloseAsync();
        }

        private async Task<string> CompressFile(string filePath)
        {
            State = TargetState.Compressing;
            var compressedPath = filePath + ".gz";
            await using var outputFileStream = File.OpenWrite(compressedPath);
            await using var inputFileStream = File.OpenRead(filePath);
            await using var writeStream = new GZipOutputStream(outputFileStream);
            
            var buffer = new byte[1024 * 2000];
            int bytesRead;
            var totalRead = 0;
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
        
        private async Task<DateTime?> FromQuery(MySqlConnection connection, string query)
        {
            var command = new MySqlCommand(query, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (reader[0] is DateTime result) return result;
            }
            
            return null;
        }

        private Task<DateTime?> CurrentTimestamp(MySqlConnection connection)
        {
            return FromQuery(connection, "SELECT CURRENT_TIMESTAMP()");
        }
    }
}