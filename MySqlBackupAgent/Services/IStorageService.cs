using System.Threading.Tasks;

namespace MySqlBackupAgent.Services
{
    public interface IStorageService
    {
        /// <summary>
        /// Save a file to the storage service. The entire file path must be specified.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>an awaitable task</returns>
        public Task UploadFile(string filePath);
        
        /// <summary>
        /// Get an array of all existing files in the storage location.
        /// </summary>
        /// <returns>an awaitable task that returns an array of all file objects in the storage location</returns>
        public Task<string[]> GetExistingFiles();
    }
}