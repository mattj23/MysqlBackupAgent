using System;

namespace MySqlBackupAgent.Models
{
    /// <summary>
    /// A simplified representation of a backup target, used as a display model
    /// </summary>
    public class TargetRepr
    {
        public TargetRepr(string name, bool checkForUpdate)
        {
            Name = name;
            CheckForUpdate = checkForUpdate;
            Progress = 0;
        }
        
        public double Progress { get; set; }

        public string Name { get; }
        
        public bool CheckForUpdate { get; }
        
        public bool IsRunning { get; set; }
        
        public DateTime? NextTime { get; set; }
        
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
    }

    public static class Extensions
    {
        public static TargetRepr ToRepr(this DbBackupTarget target)
        {
            return new TargetRepr(target.Name, target.CheckForUpdate);
        }
    }
}