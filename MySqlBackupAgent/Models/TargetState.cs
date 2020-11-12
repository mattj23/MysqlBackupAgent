namespace MySqlBackupAgent.Models
{
    /// <summary>
    /// Represents the different possible states of a backup target 
    /// </summary>
    public enum TargetState
    {
        /// <summary>
        /// Target is scheduled, but not currently queued or running
        /// </summary>
        Scheduled,
        
        /// <summary>
        /// Target is queued to begin a process, but that process must wait for another to complete
        /// before it can begin
        /// </summary>
        Queued,
        
        /// <summary>
        /// Target is being checked for the last update time
        /// </summary>
        Checking,
        
        /// <summary>
        /// Target is currently being dumped to local storage
        /// </summary>
        BackingUp,
        
        /// <summary>
        /// Backup is being compressed
        /// </summary>
        Compressing,
        
        /// <summary>
        /// Dumped file is currently being uploaded to a storage service 
        /// </summary>
        UploadingToStorage,
        
        DownloadingFromStorage,
        
        Restoring
    }
}