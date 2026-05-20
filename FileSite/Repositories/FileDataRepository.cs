using FileSite.Data;
using FileSite.Data.Interfaces;
using FileSite.Data.ViewModels;
using FileSite.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using StackExchange.Redis;
using FileSite.Data.Enums;

namespace FileSite.Repositories
{
    public class FileDataRepository : IFileDataRepository
    {
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly UserManager<AppUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IConnectionMultiplexer _redis;
        private readonly IDiskStorageRepository _diskStorage;

        public FileDataRepository(ApplicationDbContext context, IHttpContextAccessor contextAccessor, UserManager<AppUser> userManager, IConnectionMultiplexer redis, IDiskStorageRepository diskStorage)
        {
            _context = context;
            _userManager = userManager;
            _contextAccessor = contextAccessor;
            _redis = redis;
            _diskStorage = diskStorage;
        }

        public async Task<string> Add(FileDataVM fileView, string path)
        {
            string ownerId = null;
            var userPrincipal = _contextAccessor.HttpContext?.User;
            if (userPrincipal != null)
                ownerId = _userManager.GetUserId(userPrincipal);

            var filePath = System.IO.Path.Combine(path, fileView.File.FileName);
            
            var (hash, size) = await _diskStorage.SaveFileAndGetHashAsync(fileView.File.OpenReadStream(), filePath);

            if (await ValidatDistinct(hash))
            {
                _diskStorage.DeleteFile(filePath);
                return null;
            }

            long creationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long expirationTime = -1;

            switch (fileView.LifeTime)
            {
                case FileFileTimeEnum.oneDay:
                    expirationTime = creationTime + 86400;
                    break;
                case FileFileTimeEnum.oneWeek:
                    expirationTime = creationTime + 604800;
                    break;
                case FileFileTimeEnum.oneMonth:
                    expirationTime = creationTime + 2629743;
                    break;
                case FileFileTimeEnum.oneYear:
                    expirationTime = creationTime + 31556926;
                    break;
                case FileFileTimeEnum.Permanent:
                    expirationTime = -1;
                    break;
            }

            FileData newEntry = new FileData()
            {
                Name = fileView.File.FileName,
                Location = filePath,
                hash = hash,
                LifeTime = fileView.LifeTime,
                OwnerId = ownerId,
                CreationDate = creationTime,
                Size = size
            };
            await _context.AddAsync(newEntry);
            SaveChanges();

            if (expirationTime != -1)
            {
                var savedEntry = await _context.FileDatas.FirstOrDefaultAsync(f => f.hash == hash);
                if (savedEntry != null)
                {
                    var db = _redis.GetDatabase();
                    await db.SortedSetAddAsync("file_expirations", savedEntry.Id, expirationTime);
                }
            }

            return hash;
        }

        public void SaveChanges()
        {
            try
            {
                _context.SaveChanges();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public async Task<bool> ValidatDistinct(string hash)
        {
            return await _context.FileDatas.AnyAsync(f => f.hash == hash);
        }

        public async Task<FileData> RequestFileData(string hash)
        {
            var file = await _context.FileDatas.FirstOrDefaultAsync(f => f.hash == hash);
            return file;
        }
    }
}