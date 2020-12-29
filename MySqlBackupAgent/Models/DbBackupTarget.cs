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
    /// <summary>
    /// A DbBackupTarget is a single database (1:1 relationship with db/credentials) that is a target for being backed
    /// up. DbBackupTargets are built directly from entries in the application's configuration file, and are accessible
    /// to consumers through the BackupTargetService, which maintains a thread-safe list of targets.
    ///
    /// There should only be a single DbBackupTarget for each database target in the entire application scope.
    /// Properties of the object, like progress, message, next scheduled time, can and do change as the application goes
    /// through its various tasks. These changes result in OnNext events being published through IObservable interfaces,
    /// which are intended to be consumed by other objects interested in the current DbBackupTarget state.
    ///
    /// For view purposes, a DbTargetView handles these subscriptions and gives an object which can be instantiated
    /// as many times and in as many places as needed, and disposed of when no longer being used. This allows for
    /// synchronization across the UI based on changes happening in the DbBackupTarget.
    /// </summary>
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
        /// Gets the name of the backup target
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// Gets a key associated with the backup target. This should be a filesystem and url safe name which can be
        /// used for associated purposes, and is defined in the appsettings.json file.
        /// </summary>
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
        
        /// <summary>
        /// Gets the actual cron text associated with this backup target. 
        /// </summary>
        public string CronText { get; }

        /// <summary>
        /// Gets a flag that indicates whether or not this target should use MySQL's metadata tables to see if the db
        /// has been updated since the last backup was run.  If it has not been the scheduled backup will not occur,
        /// but a manual backup will still run.
        /// </summary>
        public bool CheckForUpdate { get; }
        
        public CronExpression Expression { get; }

        public DateTime? NextTime => Expression.GetNextOccurrence(DateTime.UtcNow);

        public TimeSpan? RunsIn => NextTime - DateTime.UtcNow;

        /// <summary>
        /// Gets an IObservable which publishes a double when the current progress indicator for the backup changes. The
        /// progress value is generic and used independently by the dump, compression, and restore processes.
        /// </summary>
        public IObservable<double> Progress => _progressSubject.AsObservable();

        /// <summary>
        /// Gets an IObservable which publishes a TargetState enum when the State property changes.
        /// </summary>
        public IObservable<TargetState> StateChange => _stateSubject.AsObservable();

        /// <summary>
        /// Gets an IObservable which publishes a DateTime when the next scheduled time for the backup to run occurs
        /// </summary>
        public IObservable<DateTime> ScheduledChange => _nextTimeSubject.AsObservable();

        /// <summary>
        /// Gets an IObservable which publishes string information messages as they occur.
        /// </summary>
        public IObservable<string> InfoMessages => _infoMessageSubject.AsObservable();
        
        /// <summary>
        /// Gets a BackupCollection object which owns the actual backups taken for this target.
        /// </summary>
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
            _subscription = Observable.Timer(offset.Value) .Subscribe(l => RunBackup());
            if (NextTime != null) _nextTimeSubject.OnNext(NextTime.Value);
        }

        
        /// <summary>
        /// Kicks off the backup task and reschedules the next observable. Is effectively an event handler, no caller
        /// ever needs to be waiting on this.
        /// </summary>
        public async void RunBackup(bool force=false)
        {
            if (State != TargetState.Scheduled) return;
            
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
            finally
            {
                ScheduleNext();
            }
        }

        /// <summary>
        /// Kick off a restore task.  Cancels the next backup subscription and then re-subscribes at the end, in order
        /// to prevent a backup from trying to start while the restore operation is happening.
        /// </summary>
        /// <param name="backup">The DbBackup object to restore. Must be a member of the Backups collection.</param>
        public async void RunRestore(DbBackup backup)
        {
            if (State != TargetState.Scheduled) return;
            
            _subscription?.Dispose();
            _infoMessageSubject.OnNext("Running restore");

            try
            {
                // Build the full connection string with the extra parameters recommended
                // by the MySqlBackup.NET author
                var connectionString = _connectionString + (_connectionString.EndsWith(";") ? string.Empty : ";") +
                                       "charset=utf8;convertzerodatetime=true;";

                // Establish the connection to the database
                await using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();

                await RestoreTo(connection, backup);
            }
            catch (Exception e)
            {
                // TODO: Logging
                Console.WriteLine(e);
                _infoMessageSubject.OnNext("Error occurred while trying to perform backup");
            }
            finally
            {
                ScheduleNext();
            }
        }

        /// <summary>
        /// Performs a backup of the database target.
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
        

        private async Task RestoreTo(MySqlConnection connect, DbBackup backup)
        {
            State = TargetState.DownloadingFromStorage;
            
            // Prepare the scratch directory and temporary file
            if (!Directory.Exists(_scratchPath))
            {
                Directory.CreateDirectory(_scratchPath);
            }
            
            // Construct the file name and path. The filename will simply be a uniquely generated string, since the
            // backup collection is responsible for handling the final naming and organization
            var scratchName = Guid.NewGuid().ToString().Replace("-", "") + ".sql";
            var scratchPath = Path.Combine(_scratchPath, scratchName);
            var compressedPath = scratchPath + ".gz";
            
            try
            {
                // Download and decompress the file
                await Backups.RetrieveBackup(backup, compressedPath);
                await DecompressFile(compressedPath, scratchPath);
                
                // Restore the backup
                await RestoreToMySql(connect, scratchPath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            finally
            {
                if (File.Exists(scratchPath))
                {
                    File.Delete(scratchPath);
                }
                
                if (File.Exists(compressedPath))
                {
                    File.Delete(compressedPath);
                }

                State = TargetState.Scheduled;
                _progressSubject.OnNext(100);
            }
        }

        /// <summary>
        /// Perform a MySQL dump to the filepath given in the method parameters.  This will change the DbTargetState,
        /// create a MySqlBackup object, and connect the progress changed event handler to the progress IObservable.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="filePath"></param>
        /// <returns>An awaitable task that completes when the dump to file is finished.</returns>
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

        /// <summary>
        /// Perform a MySQL restore on the given connection using the specified dump file.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="filePath"></param>
        /// <returns>An awaitable task that completes when the restore is finished.</returns>
        private async Task RestoreToMySql(MySqlConnection connection, string filePath)
        {
            State = TargetState.Restoring;
            _progressSubject.OnNext(0);
            
            await using var cmd = new MySqlCommand();
            using var restore = new MySqlBackup(cmd);
            
            restore.ImportProgressChanged += RestoreOnImportProgressChanged;
            cmd.Connection = connection;
            restore.ImportFromFile(filePath);
            restore.ImportProgressChanged -= RestoreOnImportProgressChanged;
            await connection.CloseAsync();
        }

        /// <summary>
        /// Perform file compression on a given filename. This will change the DbTargetState, and will write out to the
        /// same file name with ".gz" appended to the file extension.  This method will publish progress changes to the
        /// progress IObservable.
        /// </summary>
        /// <param name="filePath">The path of the file to compress. The output file will be this value plus ".gz"
        /// appended to the extension.</param>
        /// <returns>An awaitable task that completes when the compression is finished.</returns>
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
        
        /// <summary>
        /// Perform file decompression on a given filename. This will change the DbTargetState and will publish
        /// progress changes to the Progress IObservable.
        /// </summary>
        /// <param name="compressedPath">Path to the compressed file</param>
        /// <param name="destPath">File path to write the decompressed file to</param>
        /// <returns>An awaitable which completes when the decompression is finished</returns>
        private async Task DecompressFile(string compressedPath, string destPath)
        {
            State = TargetState.Decompressing;
            await using var outputFileStream = File.OpenWrite(destPath);
            await using var inputFileStream = File.OpenRead(compressedPath);
            await using var readStream = new GZipInputStream(inputFileStream);
            
            var buffer = new byte[1024 * 2000];
            while (await readStream.ReadAsync(buffer) > 0)
            {
                outputFileStream.Write(buffer);
                _progressSubject.OnNext(100.0 * inputFileStream.Position / inputFileStream.Length);
            }
        }
        
        /// <summary>
        /// A wrapper method to convert a ExportProgressChanged event from the MySqlBackup object to something that
        /// pushes out messages on the Progress IObservable.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BackupOnExportProgressChanged(object sender, ExportProgressArgs e)
        {
            var current = (double) e.CurrentRowIndexInAllTables;
            var all = (double) e.TotalRowsInAllTables;
            _progressSubject.OnNext(100.0 * current / all);
        }
        
        /// <summary>
        /// A wrapper method to convert an ImportProgressChanged event from the MySqlBackup object to something that
        /// pushes out messages on the Progress IObservable
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RestoreOnImportProgressChanged(object sender, ImportProgressArgs e)
        {
            _progressSubject.OnNext(e.PercentageCompleted);
        }

        /// <summary>
        /// A helper method to convert a query which returns a single MySQL datetime response to a C# DateTime type.
        /// This is used for both getting the current database time (for the backup timestamp) and for getting the
        /// last updated timestamp from the metadata tables. Returns a null value if nothing is returned.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="query"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Gets the current database time as a DateTime, or returns null if the query doesn't return anything. Use this
        /// to determine what the database time is, which should be used by the backup system to timestamp the backups
        /// themselves.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        private Task<DateTime?> CurrentTimestamp(MySqlConnection connection)
        {
            return FromQuery(connection, "SELECT CURRENT_TIMESTAMP()");
        }
    }
}