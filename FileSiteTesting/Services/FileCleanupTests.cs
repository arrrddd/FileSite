using FileSite.Data;
using FileSite.Data.Enums;
using FileSite.Models;
using FileSite.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FileSiteTesting.Services;

public class FileCleanupTests:IDisposable
{

   private readonly string _tempPath;
    
    public FileCleanupTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
    }

    [Theory]
    [InlineData("soonEnough",FileFileTimeEnum.oneDay,-2,true)]
    [InlineData("Live",FileFileTimeEnum.oneYear,-100,false)]
    private async Task FileCleanup_ShouldDeleteOldFiles(string fileName, FileFileTimeEnum life, long creationDate, bool shouldDelete)
    {
        var services = new ServiceCollection();
        string dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o=> o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        services.AddHostedService<FileCleanup>();
        var provider = services.BuildServiceProvider();
        string path = Path.Combine(_tempPath, fileName);
        File.WriteAllText(path, "data");

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetService<ApplicationDbContext>();
            context.FileDatas.Add(new FileData
            {
                Location =  path,
                CreationDate = DateTimeOffset.UtcNow.AddDays(creationDate).ToUnixTimeSeconds(),//days ago
                LifeTime =  life,
                hash = "",
                Name =  fileName
            });
            await context.SaveChangesAsync();
        }
        var service = provider.GetServices<IHostedService>()
            .OfType<FileCleanup>()
            .First();        await service.CleanUpFilesAsync();
        
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fileInDataBase = await context.FileDatas.FirstOrDefaultAsync(f => f.Location == path);

            if (shouldDelete)
            {
                Assert.Null(fileInDataBase); // Should be gone from DB
                Assert.False(File.Exists(path)); // Should be gone from Disk
            }
            else
            {
                Assert.NotNull(fileInDataBase); // Should still be in DB
                Assert.True(File.Exists(path)); // Should still be on Disk
            }
        }
    }
    public void Dispose()
    {
        // Cleanup the temp folder after test finishes
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }
}