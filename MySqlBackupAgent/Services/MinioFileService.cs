using System;
using System.IO;
using System.Threading.Tasks;
using Minio;
using Minio.DataModel;

namespace MySqlBackupAgent.Services
{
    public class MinioFileService : IStorageService
    {
        private readonly MinioSettings _settings;

        public MinioFileService(MinioSettings settings)
        {
            _settings = settings;
        }

        public async Task UploadFile(string filePath)
        {
            var client = GetClient();

            var hasBucket = await client.BucketExistsAsync(_settings.Bucket);
            if (!hasBucket) await client.MakeBucketAsync(_settings.Bucket);

            string fileName = Path.GetFileName(filePath);
            if (!string.IsNullOrEmpty(_settings.Prefix))
            {
                var p = _settings.Prefix.Trim('/');
                fileName = $"{p}/{fileName}";
            }

            await client.PutObjectAsync(_settings.Bucket, fileName, filePath);
        }

        private MinioClient GetClient()
        {
            var client = new MinioClient(_settings.Endpoint, _settings.AccessKey, _settings.SecretKey);
            if (_settings.Secure)
                client.WithSSL();
            return client;
        }
    }

    public class MinioSettings
    {
        public string Endpoint { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string Bucket { get; set; }
        public string Prefix { get; set; }
        public string Location { get; set; }
        public bool Secure { get; set; }
    }
}