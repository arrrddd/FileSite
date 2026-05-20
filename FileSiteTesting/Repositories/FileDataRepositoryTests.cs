using System.Security.Claims;
using System.Text;
using FakeItEasy;
using FileSite.Data;
using FileSite.Data.Enums;
using FileSite.Data.Interfaces;
using FileSite.Data.ViewModels;
using FileSite.Models;
using FileSite.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace FileSiteTesting.Repositories;

public class FileDataRepositoryTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<AppUser> _userManager;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _redisDb;
    private readonly IDiskStorageRepository _diskStorage;

    public FileDataRepositoryTests()
    {
        _httpContextAccessor = A.Fake<IHttpContextAccessor>();
        var store = A.Fake<IUserStore<AppUser>>();
        _userManager = A.Fake<UserManager<AppUser>>(o =>
            o.WithArgumentsForConstructor(new object[] { store, null!, null!, null!, null!, null!, null!, null!, null! }));
        
        _redis = A.Fake<IConnectionMultiplexer>();
        _redisDb = A.Fake<IDatabase>();
        A.CallTo(() => _redis.GetDatabase(A<int>._, A<object>._)).Returns(_redisDb);

        _diskStorage = A.Fake<IDiskStorageRepository>();
    }

    private ApplicationDbContext GetInMemoryDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private IFormFile CreateFakeFormFile(string content, string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, stream.Length, "data", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
    }

    [Theory]
    [InlineData(true, "user-999", "MyFile.txt")]
    [InlineData(false, null, "Anon.txt")]
    public async Task Add_ValidRequest_ShouldSaveFile(bool isAuthenticated, string? expectedOwnerId, string fileName)
    {
        // --- ARRANGE ---
        using var context = GetInMemoryDatabase();
        var fakeContext = A.Fake<HttpContext>();

        if (isAuthenticated && expectedOwnerId != null)
        {
            var fakePrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, expectedOwnerId) }));
            A.CallTo(() => fakeContext.User).Returns(fakePrincipal);
            A.CallTo(() => _userManager.GetUserId(fakePrincipal)).Returns(expectedOwnerId);
        }
        else
        {
            A.CallTo(() => fakeContext.User).Returns(null);
        }
        A.CallTo(() => _httpContextAccessor.HttpContext).Returns(fakeContext);

        var repo = new FileDataRepository(context, _httpContextAccessor, _userManager, _redis, _diskStorage);
        var fileVm = new FileDataVM 
        { 
            File = CreateFakeFormFile("Test Content", fileName), 
            LifeTime = FileFileTimeEnum.Permanent 
        };
        
        var expectedHash = "testhash123";
        var expectedSize = 1234L;
        A.CallTo(() => _diskStorage.SaveFileAndGetHashAsync(A<Stream>._, A<string>._))
            .Returns((expectedHash, expectedSize));

        // --- ACT ---
        var resultHash = await repo.Add(fileVm, "/fake/path");

        // --- ASSERT ---
        Assert.Equal(expectedHash, resultHash);

        var dbEntry = await context.FileDatas.FirstAsync();
        Assert.Equal(expectedOwnerId, dbEntry.OwnerId);
        Assert.Equal(fileName, dbEntry.Name);
        Assert.Equal(expectedSize, dbEntry.Size);
        Assert.Equal(Path.Combine("/fake/path", fileName), dbEntry.Location);

        A.CallTo(() => _diskStorage.SaveFileAndGetHashAsync(A<Stream>._, Path.Combine("/fake/path", fileName)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Add_DuplicateFile_ShouldFailAndCleanup()
    {
        // Arrange
        using var context = GetInMemoryDatabase();
        var repo = new FileDataRepository(context, _httpContextAccessor, _userManager, _redis, _diskStorage);
        
        var content = "Duplicate Content";
        var duplicateHash = "duplicatehash";
        
        context.FileDatas.Add(new FileData { hash = duplicateHash, Name = "existing.txt", LifeTime = FileFileTimeEnum.Permanent, Location = "/fake/path/existing.txt" });
        await context.SaveChangesAsync();

        var fileVm = new FileDataVM { File = CreateFakeFormFile(content, "new.txt"), LifeTime = FileFileTimeEnum.Permanent };

        A.CallTo(() => _diskStorage.SaveFileAndGetHashAsync(A<Stream>._, A<string>._))
            .Returns((duplicateHash, 5678L));

        // Act
        var result = await repo.Add(fileVm, "/fake/path");

        // Assert
        Assert.Null(result);
        A.CallTo(() => _diskStorage.DeleteFile(Path.Combine("/fake/path", "new.txt")))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Add_FileWithExpiration_ShouldAddToRedis()
    {
        // Arrange
        using var context = GetInMemoryDatabase();
        var fakeContext = A.Fake<HttpContext>();
        A.CallTo(() => fakeContext.User).Returns(null);
        A.CallTo(() => _httpContextAccessor.HttpContext).Returns(fakeContext);

        var repo = new FileDataRepository(context, _httpContextAccessor, _userManager, _redis, _diskStorage);
        var fileVm = new FileDataVM 
        { 
            File = CreateFakeFormFile("Expiring Content", "expiring.txt"), 
            LifeTime = FileFileTimeEnum.oneDay 
        };
        
        A.CallTo(() => _diskStorage.SaveFileAndGetHashAsync(A<Stream>._, A<string>._))
            .Returns(("expiringhash", 99L));

        // Act
        var resultHash = await repo.Add(fileVm, "/fake/path");

        // Assert
        Assert.NotNull(resultHash);
        
        A.CallTo(() => _redisDb.SortedSetAddAsync("file_expirations", A<RedisValue>._, A<double>._, A<When>._, A<CommandFlags>._))
            .MustHaveHappenedOnceExactly();
    }
}