using FakeItEasy;
using FileSite.Data;
using FileSite.Data.Interfaces;
using FileSite.Models;
using FileSite.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileSite.Data.Enums;
using Xunit;

namespace FileSiteTesting.Services
{
    public class FileCleanupTests
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _redisDb;
        private readonly IDiskStorageRepository _diskStorage;
        private readonly ApplicationDbContext _context;

        public FileCleanupTests()
        {
            _diskStorage = A.Fake<IDiskStorageRepository>();
            _redis = A.Fake<IConnectionMultiplexer>();
            _redisDb = A.Fake<IDatabase>();
            A.CallTo(() => _redis.GetDatabase(A<int>._, A<object>._)).Returns(_redisDb);

            var services = new ServiceCollection();
            var dbName = System.Guid.NewGuid().ToString();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddSingleton(_diskStorage);
            var provider = services.BuildServiceProvider();

            _context = provider.GetRequiredService<ApplicationDbContext>();

            var scope = A.Fake<IServiceScope>();
            A.CallTo(() => scope.ServiceProvider).Returns(provider);
            _scopeFactory = A.Fake<IServiceScopeFactory>();
            A.CallTo(() => _scopeFactory.CreateScope()).Returns(scope);
        }

        [Fact]
        public async Task CleanUpFilesAsync_ShouldDeleteExpiredFiles()
        {
            // Arrange
            var logger = A.Fake<ILogger<FileCleanup>>();
            var service = new FileCleanup(logger, _scopeFactory, _redis);

            var expiredFile = new FileData { Id = 1, Location = "/path/expired.txt", Name = "expired.txt", LifeTime = FileFileTimeEnum.oneDay };
            var validFile = new FileData { Id = 2, Location = "/path/valid.txt", Name = "valid.txt", LifeTime = FileFileTimeEnum.Permanent };
            
            await _context.FileDatas.AddRangeAsync(expiredFile, validFile);
            await _context.SaveChangesAsync();

            var expiredFileIds = new RedisValue[] { "1" };
            A.CallTo(() => _redisDb.SortedSetRangeByScoreAsync("file_expirations", double.NegativeInfinity, A<double>._, A<Exclude>._, A<Order>._, A<long>._, A<long>._, A<CommandFlags>._))
                .Returns(expiredFileIds);

            // Act
            await service.CleanUpFilesAsync();

            // Assert
            A.CallTo(() => _diskStorage.DeleteFile(expiredFile.Location)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _diskStorage.DeleteFile(validFile.Location)).MustNotHaveHappened();

            var dbFiles = await _context.FileDatas.ToListAsync();
            Assert.DoesNotContain(dbFiles, f => f.Id == expiredFile.Id);
            Assert.Contains(dbFiles, f => f.Id == validFile.Id);

            A.CallTo(() => _redisDb.SortedSetRemoveAsync("file_expirations", A<RedisValue[]>.That.IsSameSequenceAs(expiredFileIds), A<CommandFlags>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task CleanUpFilesAsync_ShouldNotRun_WhenNoExpiredFiles()
        {
            // Arrange
            var logger = A.Fake<ILogger<FileCleanup>>();
            var service = new FileCleanup(logger, _scopeFactory, _redis);

            A.CallTo(() => _redisDb.SortedSetRangeByScoreAsync(A<RedisKey>._, A<double>._, A<double>._, A<Exclude>._, A<Order>._, A<long>._, A<long>._, A<CommandFlags>._))
                .Returns(new RedisValue[0]);

            // Act
            await service.CleanUpFilesAsync();

            // Assert
            A.CallTo(() => _diskStorage.DeleteFile(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => _context.SaveChangesAsync(A<System.Threading.CancellationToken>._)).MustNotHaveHappened();
        }
    }
}