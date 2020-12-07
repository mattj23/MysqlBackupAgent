using System;

namespace MySqlBackupAgent.Models
{
    public class DbBackup : IEquatable<DbBackup>
    {
        public DbBackup(string fileName, DateTime timeStamp)
        {
            FileName = fileName;
            TimeStamp = timeStamp;
        }

        public string FileName { get; }
        public DateTime TimeStamp { get; }

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