using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlBackupAgent.Services;

namespace MySqlBackupAgent.Models
{
    /// <summary>
    /// A BackupCollection owns a collection of database backups.  It abstracts the actual storage mechanism to the
    /// IStorageService, but itself is responsible for the naming and organization of backups as well as maintaining
    /// knowledge of the entire collection of backups.  When a backup is added or removed, it goes through this object,
    /// and this object informs all of its subscribers as to the change in the collection state.  This allows for a
    /// single centralized handler of all backups.
    /// </summary>
    public class BackupCollection
    {
        private static readonly string _dateTimeFormat = "yyyy-MM-dd-HH-mm-ss";
        private readonly ILogger _logger;
        
        private readonly ConcurrentDictionary<string, DbBackup> _list;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _keyName;

        private readonly Subject<DbBackup> _addSubject;
        private readonly Subject<DbBackup> _removeSubject;

        public BackupCollection(string keyName, IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _list = new ConcurrentDictionary<string, DbBackup>();
            _keyName = keyName;
            
            _addSubject = new Subject<DbBackup>();
            _removeSubject = new Subject<DbBackup>();
        }

        public IObservable<DbBackup> Added => _addSubject.AsObservable();
        public IObservable<DbBackup> Removed => _removeSubject.AsObservable();
        
        /// <summary>
        /// Gets the total number of backups in the collection
        /// </summary>
        public int Count => _list.Count;

        /// <summary>
        /// Get an array of copied backups. Useful for when a new observer is trying to pre-populate the currently
        /// known backups.
        /// </summary>
        /// <returns></returns>
        public DbBackup[] CopyValues() => _list.Values.Select(v => v.Clone()).ToArray();

        /// <summary>
        /// Retrieves the list of backups in the storage provider and populates the collection. Clears any existing
        /// elements in the collection.
        /// </summary>
        public async Task GetExistingBackups()
        {
            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetService<IStorageService>();
            var existing = await storage.GetExistingFiles();
            
            _list.Clear();

            var startChar = _keyName.Length + 1;
            foreach (var file in existing.Where(f => f.Item1.StartsWith(_keyName)))
            {
                try
                {
                    var parseText = file.Item1.Substring(startChar).Split('.')[0];
                    var timeStamp = DateTime.ParseExact(parseText, _dateTimeFormat, CultureInfo.InvariantCulture);
                    var backup = new DbBackup(file.Item1, timeStamp, file.Item2);
                    _list[file.Item1]  = backup;
                    
                }
                catch (Exception)
                {
                    // Exceptions here are ignored. Because we are reading directly from the storage backend we're 
                    // going to have every parse error from every file show up as an exception.
                }
            }
        }

        /// <summary>
        /// Add a backup file to the collection. This should be in the form of a dumped file with a unique but
        /// irrelevant name that has already been compressed. The collection will manage the file's final name in the
        /// storage backend.
        /// </summary>
        /// <param name="file">A FileInfo object pointing to the file to be added to the collection</param>
        /// <param name="timeStamp">The timestamp from the database when the backup was taken</param>
        /// <returns></returns>
        public async Task AddBackup(FileInfo file, DateTime timeStamp)
        {
            var timeText = timeStamp.ToString(_dateTimeFormat);
            var name = $"{_keyName}-{timeText}.sql.gz";

            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetService<IStorageService>();
            try
            {
                await storage.UploadFile(file.FullName, name);
                var newBackup = new DbBackup(name, timeStamp, (ulong) file.Length);
                _list[name] = newBackup;
                _addSubject.OnNext(newBackup);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);
                throw;
            }
        }

        public async Task RetrieveBackup(DbBackup backup, string destinationPath)
        {
            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetService<IStorageService>();
            try
            {
                await storage.DownloadFile(backup.FileName, destinationPath);
            }
            catch (Exception e)
            {
                _logger.Log(LogLevel.Error, e.Message);
                throw;
            }
        }

        /// <summary>
        /// Checks to see if the backup collection has any backup with a more recent timestamp than the one passed in
        /// as an argument.
        /// </summary>
        /// <param name="timeStamp">The timestamp to check against</param>
        /// <returns>true if there is a backup with a more recent timestamp, false if not</returns>
        public bool HasMoreRecentThan(DateTime timeStamp) => _list.Values.Any(b => b.TimeStamp > timeStamp);
        
    }
}