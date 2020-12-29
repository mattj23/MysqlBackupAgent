using System;
using System.Threading.Tasks;

namespace MySqlBackupAgent.Services
{
    public interface IStorageService
    {
        /// <summary>
        /// Save a file to the storage service. The entire file path must be specified.
        /// </summary>
        /// <param name="filePath">The full file path of the file to be uploaded</param>
        /// <param name="storedName">An optional name for the file to be stored at its destination. If no name is
        /// specified, the name of the provided file may be used instead</param>
        /// <returns>an awaitable task</returns>
        public Task UploadFile(string filePath, string storedName=null);

        /// <summary>
        /// Retrieve a file from the storage service.  The destination filename must be specified, but does not need
        /// to be the same as the stored name.
        /// </summary>
        /// <param name="storedName">The name/path in the service by which the file is identified</param>
        /// <param name="destinationPath">A path to save the file to</param>
        /// <returns>An awaitable task which completes when the download is finished</returns>
        public Task DownloadFile(string storedName, string destinationPath);
        
        /// <summary>
        /// Get an array of all existing files in the storage location.
        /// </summary>
        /// <returns>an awaitable task that returns an array of all file names and sizes in the storage location</returns>
        public Task<Tuple<string, ulong>[]> GetExistingFiles();
    }
}