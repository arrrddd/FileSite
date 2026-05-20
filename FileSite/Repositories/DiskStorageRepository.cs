using FileSite.Data.Interfaces;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FileSite.Repositories
{
    public class DiskStorageRepository : IDiskStorageRepository
    {
        public async Task<(string hash, long size)> SaveFileAndGetHashAsync(Stream stream, string filePath)
        {
            string hashString;
            long streamLength;

            using (var fileStream = new FileStream(filePath, FileMode.CreateNew))
            using (var md5 = MD5.Create())
            using (var cryptoStream = new CryptoStream(fileStream, md5, CryptoStreamMode.Write))
            {
                await stream.CopyToAsync(cryptoStream);
                await cryptoStream.FlushFinalBlockAsync();
                hashString = BitConverter.ToString(md5.Hash).Replace("-", "").ToLower();
                streamLength = fileStream.Length;
            }

            return (hashString, streamLength);
        }

        public Stream GetFileStream(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public void DeleteFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}