using System;

namespace MySqlBackupAgent.Models
{
    /// <summary>
    /// A DbBackup represents a single backup taken of a DbBackupTarget and maps 1:1 to a physical file with the
    /// database contents.  DbBackups for a single DbBackupTarget are all managed by a BackupCollection object, which
    /// handles their greater context (such as how to store them, what to name them, etc).  This object is a
    /// lightweight record used primarily to manage information about backups.
    /// </summary>
    public class DbBackup : IEquatable<DbBackup>
    {
        public DbBackup(string fileName, DateTime timeStamp, ulong size)
        {
            FileName = fileName;
            TimeStamp = timeStamp;
            Size = size;
        }

        /// <summary>
        /// Gets the filename associated with this backup. The filename only will make sense within the context of a
        /// BackupCollection with an IStorageService.
        /// </summary>
        public string FileName { get; }
        
        /// <summary>
        /// Gets the timestamp associated with the backup. The 
        /// </summary>
        public DateTime TimeStamp { get; }
        
        public ulong Size { get; }

        /// <summary>
        /// Creates a memberwise clone of this backup object. This DbBackup object is a simple class with fixed value
        /// type members. Use clones to prevent concurrency problems.
        /// </summary>
        /// <returns>A memberwise clone of the backup object</returns>
        public DbBackup Clone()
        {
            return (DbBackup) this.MemberwiseClone();
        }

        public bool Equals(DbBackup other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return FileName == other.FileName;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DbBackup) obj);
        }

        public override int GetHashCode()
        {
            return (FileName != null ? FileName.GetHashCode() : 0);
        }
    }
}