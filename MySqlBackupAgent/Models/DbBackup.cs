using System;

namespace MySqlBackupAgent.Models
{
    public class DbBackup : IEquatable<DbBackup>
    {
        public DbBackup(string fileName, DateTime timeStamp, ulong size)
        {
            FileName = fileName;
            TimeStamp = timeStamp;
            Size = size;
        }

        public string FileName { get; }
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