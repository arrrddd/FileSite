using FileSite.Data;
using FileSite.Data.Interfaces;
using FileSite.Data.ViewModels;
using FileSite.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace FileSite.Repositories
{
    public class FileDataRepository : IFileDataRepository
    {   
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly UserManager<AppUser> _userManager;
        private readonly ApplicationDbContext _context;
        public FileDataRepository(ApplicationDbContext context, IHttpContextAccessor contextAccessor, UserManager<AppUser> User)
        {
            _context = context;
            _userManager = User;
            _contextAccessor = contextAccessor;
        }
        

        public async Task<string> Add(FileDataVM fileView, string Path)
        { 
            string ownerId = null;
            var userPrincipal = _contextAccessor.HttpContext?.User;
            if (userPrincipal != null)
                ownerId = _userManager.GetUserId(userPrincipal);
            
            string hash = BitConverter.ToString(MD5.Create().ComputeHash(fileView.File.OpenReadStream())).Replace("-", "").ToLower();
            if (await ValidatDistinct(hash)) return null;
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
                    CreationDate=DateTimeOffset.Now.ToUnixTimeSeconds(),
                    Size= streaam.Length
                };
                await _context.AddAsync(newEntry);
            }
            #endregion

            SaveChanges();
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