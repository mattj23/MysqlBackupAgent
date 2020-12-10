using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace MySqlBackupAgent.Models
{
    /// <summary>
    /// A simplified representation of a backup target, used as a display model. A DbTargetView connects itself to the
    /// underlying DbBackupTarget by subscribing to IObservables in order to keep its state up to date without running
    /// into concurrency issues.
    /// </summary>
    public class DbTargetView
    {
        private readonly Subject<Unit> _changeSubject;
        
        public DbTargetView(string key)
        {
            Key = key;
            Progress = 0;
            _changeSubject = new Subject<Unit>();
            Backups = new Dictionary<string, DbBackup>();
        }

        public IObservable<Unit> PropertyChanged => _changeSubject.AsObservable();
        
        public double Progress { get; private set; }

        public string Name { get; private set; }
        
        public string CronText { get; private set; }
        
        public string Key { get; }
        
        public bool CheckForUpdate { get; private set; }
        
        public TargetState State { get; private set; }
        
        public DateTime? NextTime { get; private set; }
        
        public string InfoMessage { get; private set; }
        
        public Dictionary<string, DbBackup> Backups { get; }
        
        public TimeSpan? RunsIn => NextTime - DateTime.UtcNow;

        /// <summary>
        /// Returns a formatted string showing the number of hours, minutes, and seconds until the backup task
        /// runs next
        /// </summary>
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

        public string StatusText => State switch
                {
                    TargetState.BackingUp => "Backing Up",
                    TargetState.Compressing => "Compressing",
                    TargetState.UploadingToStorage => "Uploading to storage",
                    TargetState.Scheduled =>
                        RunsIn.HasValue ? $"Runs in {RunsIn:hh\\:mm\\:ss}" : "No scheduled time",
                    _ => "Unknown state"
                };

        public void Subscribe(DbBackupTarget target)
        {
            Name = target.Name;
            CronText = target.CronText;
            CheckForUpdate = target.CheckForUpdate;
            State = target.State;

            foreach (var backup in target.Backups.CopyValues())
            {
                Backups[backup.FileName] = backup;
            }

            target.Progress.Subscribe(d =>
            {
                Progress = d;
                _changeSubject.OnNext(default);
            });

            target.StateChange.Subscribe(s =>
            {
                State = s;
                _changeSubject.OnNext(default);
            });

            target.ScheduledChange.Subscribe(t =>
            {
                NextTime = t;
                _changeSubject.OnNext(default);
            });

            target.InfoMessages.Subscribe(s =>
            {
                InfoMessage = s;
                _changeSubject.OnNext(default);
            });

            target.Backups.Added.Subscribe(b =>
            {
                Backups[b.FileName] = b.Clone();
                _changeSubject.OnNext(default);
            });
            
            target.Backups.Added.Subscribe(b =>
            {
                if (Backups.ContainsKey(b.FileName))
                    Backups.Remove(b.FileName);
                
                _changeSubject.OnNext(default);
            });
        }
    }

    public static class Extensions
    {
        /// <summary>
        /// Creates a 
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static DbTargetView CreateView(this DbBackupTarget target)
        {
            var temp = new DbTargetView(target.Key);
            temp.Subscribe(target);
            return temp;
        }
    }
}