using FileSite.Data;
using FileSite.Data.Interfaces;
using FileSite.Data.ViewModels;
using FileSite.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
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

        public FileDataRepository(ApplicationDbContext context, IHttpContextAccessor contextAccessor, UserManager<AppUser> User, IConnectionMultiplexer redis)
        {
            _context = context;
            _userManager = User;
            _contextAccessor = contextAccessor;
            _redis = redis;
        }
        

        public async Task<string> Add(FileDataVM fileView, string Path)
        { 
            string ownerId = null;
            var userPrincipal = _contextAccessor.HttpContext?.User;
            if (userPrincipal != null)
                ownerId = _userManager.GetUserId(userPrincipal);
            
            string hash = BitConverter.ToString(MD5.Create().ComputeHash(fileView.File.OpenReadStream())).Replace("-", "").ToLower();
            if (await ValidatDistinct(hash)) return null;
            
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

            #region Streaming
            using (Stream str = new FileStream($@"{Path}/{fileView.File.FileName}", FileMode.CreateNew))
            {
                await fileView.File.CopyToAsync(str);
            };
            using (Stream streaam = System.IO.File.OpenRead($@"{Path}/{fileView.File.FileName}"))
            {
                FileData newEntry = new FileData()
                {
                    Name = fileView.File.FileName,
                    Location = $"{Path}/{fileView.File.FileName}",
                    hash = hash,
                    LifeTime = fileView.LifeTime,
                    OwnerId = ownerId,
                    CreationDate=creationTime,
                    Size= streaam.Length
                };
                await _context.AddAsync(newEntry);
            }
            #endregion

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
                {_context.SaveChanges();}
            catch (Exception ex)
                {Console.WriteLine(ex);}
        }

        public async Task<bool> ValidatDistinct(string hash)
        {
            return await _context.FileDatas.AnyAsync(f => f.hash ==hash);
        }

        public async Task<FileData> RequestFileData(string hash)
        {
            var file =await  _context.FileDatas.FirstOrDefaultAsync(f => f.hash == hash);
            return file;
        }
    }
}