using FileSite.Data;
using StackExchange.Redis;

namespace FileSite.Services;

public class FileCleanup : BackgroundService
{
    private readonly ILogger<FileCleanup> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;

    public FileCleanup(ILogger<FileCleanup> logger, IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _redis = redis;
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
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var db = _redis.GetDatabase();

        // Get all files with score (expiration time) <= now
        var expiredFileIds = await db.SortedSetRangeByScoreAsync("file_expirations", double.NegativeInfinity, now);

        if (expiredFileIds.Length == 0)
        {
            return;
        }

        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var idsToRemoveFromRedis = new List<RedisValue>();

            foreach (var redisValue in expiredFileIds)
            {
                if (int.TryParse(redisValue.ToString(), out int fileId))
                {
                    var fileData = await context.FileDatas.FindAsync(fileId);
                    if (fileData != null)
                    {
                        if (File.Exists(fileData.Location))
                        {
                            try
                            {
                                File.Delete(fileData.Location);
                                _logger.LogInformation("Deleted file: {Location}", fileData.Location);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to delete file: {Location}", fileData.Location);
                            }
                        }
                        context.Remove(fileData);
                        idsToRemoveFromRedis.Add(redisValue);
                    }
                    else
                    {
                        // File not found in DB, remove from Redis to keep it clean
                        idsToRemoveFromRedis.Add(redisValue);
                    }
                }
            }

            if (idsToRemoveFromRedis.Count > 0)
            {
                await context.SaveChangesAsync();
                await db.SortedSetRemoveAsync("file_expirations", idsToRemoveFromRedis.ToArray());
            }
        }
    }
}
