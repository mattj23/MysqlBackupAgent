using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Minio;
using Minio.DataModel;
using MySqlBackupAgent.Models;

namespace MySqlBackupAgent.Services
{
    public class MinioFileService : IStorageService
    {
        private readonly MinioSettings _settings;

        public MinioFileService(MinioSettings settings)
        {
            _settings = settings;
            _settings.Prefix.Trim('/');
        }

        /// <summary>
        /// Upload the a file to the S3 endpoint, bucket, and with the given prefix
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="storedName">An optional argument specifying the name to store the file as at the destination,
        /// if none is provided the given filename may be used</param>
        /// <returns></returns>
        public async Task UploadFile(string filePath, string storedName=null)
        {
            var client = GetClient();

            var hasBucket = await client.BucketExistsAsync(_settings.Bucket);
            if (!hasBucket) await client.MakeBucketAsync(_settings.Bucket);

            var fileName = string.IsNullOrWhiteSpace(storedName) ? Path.GetFileName(filePath) : storedName;
            if (!string.IsNullOrEmpty(_settings.Prefix))
            {
                fileName = $"{_settings.Prefix}/{fileName}";
            }

            await client.PutObjectAsync(_settings.Bucket, fileName, filePath);
        }

        /// <summary>
        /// Retrieve all existing files from the backup storage location. If the bucket is not found the returned
        /// array will be empty.
        /// </summary>
        /// <returns></returns>
        public async Task<Tuple<string, ulong>[]> GetExistingFiles()
        {
            try
            {
                var client = GetClient();
                var observable = client.ListObjectsAsync(_settings.Bucket, _settings.Prefix, true);
                var items = await observable.ToList();
                return items.Select(i => Tuple.Create(i.Key.Replace(_settings.Prefix, string.Empty).Trim('/'), i.Size))
                    .ToArray();
            }
            catch (Minio.Exceptions.BucketNotFoundException e)
            {
                return Array.Empty<Tuple<string, ulong>>();
            }
        }

        /// <summary>
        /// Helper method to build the client from the settings
        /// </summary>
        /// <returns></returns>
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