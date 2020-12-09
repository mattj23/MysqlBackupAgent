using System;
using System.Collections;
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
    public class BackupCollection : IReadOnlyList<DbBackup>
    {
        private static readonly string _dateTimeFormat = "yyyy-MM-dd-HH-mm";
        private readonly ILogger _logger;
        
        private readonly List<DbBackup> _list;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _keyName;

        private readonly Subject<Unit> _collectionChange;

        public BackupCollection(string keyName, IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _list = new List<DbBackup>();
            _keyName = keyName;
            
            _collectionChange = new Subject<Unit>();
        }

        public IObservable<Unit> Changed => _collectionChange.AsObservable();
        
        /// <summary>
        /// Gets the total number of backups in the collection
        /// </summary>
        public int Count => _list.Count;
        
        /// <summary>
        /// Gets the backup at a given index within the collection
        /// </summary>
        /// <param name="index">Index of the backup to return, starting from 0</param>
        public DbBackup this[int index] => _list[index];

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
                    _list.Add(backup);
                    
                }
                catch (Exception)
                {
                    // Exceptions here are ignored. Because we are reading directly from the storage backend we're 
                    // going to have every parse error from every file show up as an exception.
                }
            }
            
            _list.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
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
                _list.Add(new DbBackup(name, timeStamp, (ulong) file.Length));
                _collectionChange.OnNext(default);
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
        public bool HasMoreRecentThan(DateTime timeStamp) => _list.Any(b => b.TimeStamp > timeStamp);
        
        
        /// <summary>
        /// Returns the IReadOnlyList enumerator
        /// </summary>
        public IEnumerator<DbBackup> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable) _list).GetEnumerator();
        }


    }
}