using System.Threading.Tasks;

namespace MySqlBackupAgent.Services
{
    public interface IStorageService
    {
        public Task UploadFile(string filePath);
    }
}