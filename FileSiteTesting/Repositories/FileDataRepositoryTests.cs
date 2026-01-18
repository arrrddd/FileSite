using System.Security.Claims;
using System.Text;
using FakeItEasy;
using FileSite.Data;
using FileSite.Data.Enums;
using FileSite.Data.ViewModels;
using FileSite.Models;
using FileSite.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace FileSiteTesting.Repositories;


public class FileDataRepositoryTests : IDisposable
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<AppUser> _userManager;
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _redisDb;
    private readonly string _tempTestPath;

    public FileDataRepositoryTests()
    {
        _tempTestPath = Path.Combine(Path.GetTempPath(), "TestRun_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempTestPath);

        _httpContextAccessor = A.Fake<IHttpContextAccessor>();
        var store = A.Fake<IUserStore<AppUser>>();
        _userManager = A.Fake<UserManager<AppUser>>(o => 
            o.WithArgumentsForConstructor(new object[] { store, null!, null!, null!, null!, null!, null!, null!, null! }));
        
        _redis = A.Fake<IConnectionMultiplexer>();
        _redisDb = A.Fake<IDatabase>();
        A.CallTo(() => _redis.GetDatabase(A<int>._, A<object>._)).Returns(_redisDb);
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
    [InlineData(true, "user-999", "MyFile.txt")] // Case 1: Logged In
    [InlineData(false, null, "Anon.txt")]        // Case 2: Anonymous
    public async Task Add_ValidRequest_ShouldSaveFile(bool isAuthenticated, string? expectedOwnerId, string fileName)
    {
        // --- ARRANGE ---
        using var context = GetInMemoryDatabase();
        var fakeContext = A.Fake<HttpContext>();

        if (isAuthenticated && expectedOwnerId != null)
        {
            // Setup User Claims
            var fakePrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, expectedOwnerId) }));
            A.CallTo(() => fakeContext.User).Returns(fakePrincipal);
            // Setup UserManager to return the ID
            A.CallTo(() => _userManager.GetUserId(fakePrincipal)).Returns(expectedOwnerId);
        }
        else
        {
            A.CallTo(() => fakeContext.User).Returns(null);
        }

        A.CallTo(() => _httpContextAccessor.HttpContext).Returns(fakeContext);

        var repo = new FileDataRepository(context, _httpContextAccessor, _userManager, _redis);
        var fileVm = new FileDataVM 
        { 
            File = CreateFakeFormFile("Test Content", fileName), 
            LifeTime = FileFileTimeEnum.Permanent 
        };

        // --- ACT ---
        var resultHash = await repo.Add(fileVm, _tempTestPath);

        // --- ASSERT ---
        Assert.NotNull(resultHash);

        var dbEntry = await context.FileDatas.FirstAsync();
        Assert.Equal(expectedOwnerId, dbEntry.OwnerId); // Checks if ID matches or is null
        Assert.Equal(fileName, dbEntry.Name);

        Assert.True(File.Exists(Path.Combine(_tempTestPath, fileName)));
    }

    [Fact]
    public async Task Add_DuplicateFile_ShouldFail()
    {
        // Arrange
        using var context = GetInMemoryDatabase();
        var repo = new FileDataRepository(context, _httpContextAccessor, _userManager, _redis);
        
        var content = "Duplicate Content";
        
        // Pre-calculate hash
        string hash;
        using(var md5 = System.Security.Cryptography.MD5.Create())
        {
            hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(content)))
                           .Replace("-", "").ToLower();
        }

        // Seed the DB (This is why we can't easily put this in the Theory above)
        context.FileDatas.Add(new FileData { hash = hash, Name = "existing.txt", LifeTime = FileFileTimeEnum.Permanent,Location = _tempTestPath });
        await context.SaveChangesAsync();

        var fileVm = new FileDataVM { File = CreateFakeFormFile(content, "new.txt"), LifeTime = FileFileTimeEnum.Permanent };

        // Act
        var result = await repo.Add(fileVm, _tempTestPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Add_FileWithExpiration_ShouldAddToRedis()
    {
        // Arrange
        using var context = GetInMemoryDatabase();
        var fakeContext = A.Fake<HttpContext>();
        A.CallTo(() => fakeContext.User).Returns(null);
        A.CallTo(() => _httpContextAccessor.HttpContext).Returns(fakeContext);

        var repo = new FileDataRepository(context, _httpContextAccessor, _userManager, _redis);
        var fileVm = new FileDataVM 
        { 
            File = CreateFakeFormFile("Expiring Content", "expiring.txt"), 
            LifeTime = FileFileTimeEnum.oneDay 
        };

        // Act
        var resultHash = await repo.Add(fileVm, _tempTestPath);

        // Assert
        Assert.NotNull(resultHash);
        
        // Verify Redis was called
        A.CallTo(() => _redisDb.SortedSetAddAsync("file_expirations", A<RedisValue>._, A<double>._, A<When>._, A<CommandFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTestPath)) Directory.Delete(_tempTestPath, true); 
    }
}