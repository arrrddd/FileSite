using FileSite.Data;
using FileSite.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics;
using FileSite.Data.Enums;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using Serilog;

namespace FileSite.Services;

public class FileCleanup : BackgroundService
{
    private readonly ILogger<FileCleanup> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // Inject IServiceScopeFactory to create DbContexts on the fly
    public FileCleanup(ILogger<FileCleanup> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File Cleanup is running.");
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromHours(6));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CleanUpFilesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during file cleanup.");
            }
        }

        _logger.LogInformation("File Cleanup has stopped.");
    }

    public async Task CleanUpFilesAsync()
    {
        // Create a scope to get the DbContext
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Define thresholds
            long oneDayAgo = now - 86400;
            long oneWeekAgo = now - 604800;
            long oneMonthAgo = now - 2629743;
            long oneYearAgo = now - 31556926;

            // Filter strictly in the database
            var toBeDeleted = await context.FileDatas
                .Where(f =>
                    (f.LifeTime == FileFileTimeEnum.oneDay && f.CreationDate < oneDayAgo) ||
                    (f.LifeTime == FileFileTimeEnum.oneWeek && f.CreationDate < oneWeekAgo) ||
                    (f.LifeTime == FileFileTimeEnum.oneMonth && f.CreationDate < oneMonthAgo) ||
                    (f.LifeTime == FileFileTimeEnum.oneYear && f.CreationDate < oneYearAgo)
                )
                .ToListAsync();

            if (!toBeDeleted.Any()) return;

            foreach (var fileData in toBeDeleted)
            {
                if (File.Exists(fileData.Location))
                {
                    File.Delete(fileData.Location);
                    _logger.LogInformation("Deleted file: {Location}", fileData.Location);
                }
                context.Remove(fileData);
            }

            await context.SaveChangesAsync();
        }
    }
}
