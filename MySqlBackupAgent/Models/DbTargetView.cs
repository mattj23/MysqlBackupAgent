using System;
using System.Collections.Generic;
using System.Linq;

namespace MySqlBackupAgent.Models
{
    /// <summary>
    /// A simplified representation of a backup target, used as a display model
    /// </summary>
    public class DbTargetView
    {
        public DbTargetView(string name, string safeName, bool checkForUpdate)
        {
            Name = name;
            SafeName = safeName;
            CheckForUpdate = checkForUpdate;
            Progress = 0;
        }
        
        public double Progress { get; set; }

        public string Name { get; }
        
        public string CronText { get; set; }
        
        public string SafeName { get; }
        
        public bool CheckForUpdate { get; }
        
        public TargetState State { get; set; }
        
        public DateTime? NextTime { get; set; }
        
        public string InfoMessage { get; set; }
        
        public List<DbBackup> Backups { get; set; }
        
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
    }

    public static class Extensions
    {
        public static DbTargetView ToRepr(this DbBackupTarget target)
        {
            var temp = new DbTargetView(target.Name, target.SafeName, target.CheckForUpdate);
            temp.State = target.State;
            temp.NextTime = target.NextTime;
            temp.Backups = target.Backups.ToList();
            temp.CronText = target.CronText;
            return temp;
        }
    }
}