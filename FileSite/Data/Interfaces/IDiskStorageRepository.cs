using System.IO;
using System.Threading.Tasks;

namespace FileSite.Data.Interfaces
{
    public interface IDiskStorageRepository
    {
        Task<(string hash, long size)> SaveFileAndGetHashAsync(Stream stream, string filePath);
        Stream GetFileStream(string filePath);
        void DeleteFile(string filePath);
    }
}