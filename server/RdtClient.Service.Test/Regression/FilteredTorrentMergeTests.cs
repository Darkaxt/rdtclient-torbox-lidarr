using System.IO.Abstractions.TestingHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using RdtClient.Data.Data;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.DebridClient;
using RdtClient.Service.Helpers;
using RdtClient.Service.Services;
using RdtClient.Service.Services.DebridClients;
using RdtClient.Service.Wrappers;
using TorBoxNET;
using DownloadClient = RdtClient.Data.Enums.DownloadClient;
using TorrentsService = RdtClient.Service.Services.Torrents;

namespace RdtClient.Service.Test.Regression;

public class FilteredTorrentMergeTests : IAsyncLifetime
{
    private const String Hash = "abcdef0123456789abcdef0123456789abcdef01";
    private readonly String _databasePath = Path.Combine(Path.GetTempPath(), $"rdt-client-filtered-merge-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public async Task AddMagnetToDebridQueue_WhenSameHashHasDifferentFilter_MergesFilterAndReopensTorrent()
    {
        var torrentId = Guid.NewGuid();
        const String existingFilter = "(?i)^Book One/.*\\.mp3$";
        const String nextFilter = "(?i)^Book Two/.*\\.mp3$";

        await SeedTorrentAsync(torrentId, existingFilter, completed: true, retryCount: 3);

        var service = CreateService();
        await service.AddMagnetToDebridQueue(Magnet(), new Torrent
        {
            IncludeRegex = nextFilter,
            HostDownloadAction = TorrentHostDownloadAction.DownloadAll
        });

        await using var context = CreateContext();
        var torrent = await context.Torrents.SingleAsync(m => m.TorrentId == torrentId);

        Assert.Contains("Book One", torrent.IncludeRegex);
        Assert.Contains("Book Two", torrent.IncludeRegex);
        Assert.Contains("|", torrent.IncludeRegex);
        Assert.Null(torrent.Completed);
        Assert.Null(torrent.FilesSelected);
        Assert.Null(torrent.Error);
        Assert.Equal(0, torrent.RetryCount);
    }

    [Fact]
    public async Task AddMagnetToDebridQueue_WhenSameHashAlreadyHasSameFilter_IsIdempotent()
    {
        var torrentId = Guid.NewGuid();
        const String filter = "(?i)^Book One/.*\\.mp3$";
        await SeedTorrentAsync(torrentId, filter, completed: true);

        var service = CreateService();
        await service.AddMagnetToDebridQueue(Magnet(), new Torrent
        {
            IncludeRegex = filter,
            HostDownloadAction = TorrentHostDownloadAction.DownloadAll
        });

        await using var context = CreateContext();
        var torrent = await context.Torrents.SingleAsync(m => m.TorrentId == torrentId);

        Assert.Equal(filter, torrent.IncludeRegex);
        Assert.NotNull(torrent.Completed);
        Assert.NotNull(torrent.FilesSelected);
    }

    [Fact]
    public async Task AddMagnetToDebridQueue_WhenExistingHashIsUnfiltered_ThrowsCleanupRequired()
    {
        await SeedTorrentAsync(Guid.NewGuid(), includeRegex: null, completed: false);

        var service = CreateService();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddMagnetToDebridQueue(Magnet(), new Torrent
        {
            IncludeRegex = "(?i)^Book One/.*\\.mp3$",
            HostDownloadAction = TorrentHostDownloadAction.DownloadAll
        }));

        Assert.Contains("cleanup", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Hash, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateComplete_WhenSelectedFilePreStartFailureWasSupersededBySuccessfulDuplicate_DoesNotPoisonParent()
    {
        var torrentId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Torrents.Add(new Torrent
            {
                TorrentId = torrentId,
                Hash = Hash,
                Added = DateTimeOffset.UtcNow.AddMinutes(-10),
                RdId = Hash,
                RdName = "Collection",
                RdStatus = TorrentStatus.Finished,
                RdStatusRaw = "completed",
                RdProgress = 100,
                HostDownloadAction = TorrentHostDownloadAction.DownloadAll,
                Type = DownloadType.Torrent,
                DownloadClient = DownloadClient.Bezzad,
                IncludeRegex = "(?i)^El Hobbit/El Hobbit\\.m4b$",
                Downloads =
                [
                    new()
                    {
                        DownloadId = Guid.NewGuid(),
                        Path = "https://torbox.app/fakedl/31761821/9",
                        FileName = "El Hobbit.m4b",
                        Added = DateTimeOffset.UtcNow.AddMinutes(-3),
                        DownloadQueued = DateTimeOffset.UtcNow.AddMinutes(-3),
                        Completed = DateTimeOffset.UtcNow.AddMinutes(-2),
                        RetryCount = 3,
                        Error = "There was an error processing your request. Please try again later."
                    },
                    new()
                    {
                        DownloadId = Guid.NewGuid(),
                        Path = "https://nexus.example/dld/token",
                        Link = "https://nexus.example/dld/token",
                        FileName = "El Hobbit.m4b",
                        Added = DateTimeOffset.UtcNow.AddMinutes(-1),
                        DownloadQueued = DateTimeOffset.UtcNow.AddMinutes(-1),
                        DownloadStarted = DateTimeOffset.UtcNow.AddSeconds(-50),
                        DownloadFinished = DateTimeOffset.UtcNow.AddSeconds(-5),
                        Completed = DateTimeOffset.UtcNow.AddSeconds(-5)
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var torrentData = new TorrentData(context);
            await torrentData.UpdateComplete(torrentId, null, DateTimeOffset.UtcNow, true);
        }

        await using (var context = CreateContext())
        {
            var torrent = await context.Torrents.SingleAsync(m => m.TorrentId == torrentId);
            Assert.NotNull(torrent.Completed);
            Assert.Null(torrent.Error);
        }
    }

    [Fact]
    public async Task CreateDownloads_WhenFilterExpanded_AddsMissingSelectedDownloadsWithoutDuplicatingExistingRows()
    {
        var torrentId = Guid.NewGuid();
        var files = new List<DebridClientFile>
        {
            new() { Id = 7, Path = "Book One/part01.mp3", Bytes = 100, Selected = true },
            new() { Id = 8, Path = "Book Two/part01.mp3", Bytes = 200, Selected = true }
        };

        await using (var context = CreateContext())
        {
            context.Torrents.Add(new Torrent
            {
                TorrentId = torrentId,
                Hash = Hash,
                Added = DateTimeOffset.UtcNow.AddMinutes(-10),
                RdId = Hash,
                RdName = "Collection",
                RdStatus = TorrentStatus.Finished,
                RdStatusRaw = "completed",
                RdProgress = 100,
                HostDownloadAction = TorrentHostDownloadAction.DownloadAll,
                Type = DownloadType.Torrent,
                DownloadClient = DownloadClient.Bezzad,
                IncludeRegex = "(?i)^(Book One|Book Two)/.*\\.mp3$",
                RdFiles = JsonConvert.SerializeObject(files),
                Downloads =
                [
                    new()
                    {
                        DownloadId = Guid.NewGuid(),
                        Path = "https://torbox.app/fakedl/12345/7",
                        FileName = "part01.mp3",
                        Added = DateTimeOffset.UtcNow,
                        DownloadQueued = DateTimeOffset.UtcNow
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        var previousProvider = Settings.Get.Provider.Provider;
        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;
            var torrents = CreateService(BuildTorBoxClient());

            await torrents.CreateDownloads(torrentId);
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }

        await using var verifyContext = CreateContext();
        var torrent = await verifyContext.Torrents.Include(m => m.Downloads).SingleAsync(m => m.TorrentId == torrentId);
        Assert.Equal(2, torrent.Downloads.Count);
        Assert.Contains(torrent.Downloads, d => d.Path == "https://torbox.app/fakedl/12345/7");
        Assert.Contains(torrent.Downloads, d => d.Path == "https://torbox.app/fakedl/12345/8");
    }

    [Fact]
    public async Task Tick_WhenFilterExpandedOnFinishedTorrentWithExistingDownloads_CreatesMissingDownloads()
    {
        var torrentId = Guid.NewGuid();
        var files = new List<DebridClientFile>
        {
            new() { Id = 7, Path = "Book One/part01.mp3", Bytes = 100, Selected = true },
            new() { Id = 8, Path = "Book Two/part01.mp3", Bytes = 200, Selected = true }
        };

        await using (var context = CreateContext())
        {
            context.Torrents.Add(new Torrent
            {
                TorrentId = torrentId,
                Hash = Hash,
                Added = DateTimeOffset.UtcNow.AddMinutes(-10),
                RdId = Hash,
                RdName = "Collection",
                RdStatus = TorrentStatus.Finished,
                RdStatusRaw = "completed",
                RdProgress = 100,
                HostDownloadAction = TorrentHostDownloadAction.DownloadAll,
                Type = DownloadType.Torrent,
                DownloadClient = DownloadClient.Bezzad,
                IncludeRegex = "(?i)^(Book One|Book Two)/.*\\.mp3$",
                FilesSelected = DateTimeOffset.UtcNow.AddMinutes(-5),
                RdFiles = JsonConvert.SerializeObject(files),
                Downloads =
                [
                    new()
                    {
                        DownloadId = Guid.NewGuid(),
                        Path = "https://torbox.app/fakedl/12345/7",
                        FileName = "part01.mp3",
                        Added = DateTimeOffset.UtcNow,
                        DownloadQueued = DateTimeOffset.UtcNow,
                        Completed = DateTimeOffset.UtcNow
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        var previousProvider = Settings.Get.Provider.Provider;
        var previousApiKey = Settings.Get.Provider.ApiKey;
        var previousDownloadPath = Settings.Get.DownloadClient.DownloadPath;
        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;
            Settings.Get.Provider.ApiKey = "test-api-key";
            Settings.Get.DownloadClient.DownloadPath = "/downloads";
            var torrents = CreateService(BuildTorBoxClient());
            var downloads = new Downloads(new DownloadData(CreateContext()));
            var coordinatorMock = new Mock<IRateLimitCoordinator>();
            coordinatorMock.Setup(m => m.GetMaxNextAllowedAt()).Returns((DateTimeOffset?)null);
            var runner = new TorrentRunner(Mock.Of<ILogger<TorrentRunner>>(),
                                           torrents,
                                           downloads,
                                           null!,
                                           Mock.Of<IHttpClientFactory>(),
                                           coordinatorMock.Object);

            await runner.Tick();
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
            Settings.Get.Provider.ApiKey = previousApiKey;
            Settings.Get.DownloadClient.DownloadPath = previousDownloadPath;
            TorrentRunner.ActiveDownloadClients.Clear();
            TorrentRunner.ActiveUnpackClients.Clear();
        }

        await using var verifyContext = CreateContext();
        var torrent = await verifyContext.Torrents.Include(m => m.Downloads).SingleAsync(m => m.TorrentId == torrentId);
        Assert.Equal(2, torrent.Downloads.Count);
        Assert.Null(torrent.Completed);
        Assert.Contains(torrent.Downloads, d => d.Path == "https://torbox.app/fakedl/12345/8");
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    private async Task SeedTorrentAsync(Guid torrentId, String? includeRegex, Boolean completed, Int32 retryCount = 0)
    {
        await using var context = CreateContext();
        context.Torrents.Add(new Torrent
        {
            TorrentId = torrentId,
            Hash = Hash,
            Added = DateTimeOffset.UtcNow.AddMinutes(-10),
            RdId = Hash,
            RdName = "Collection",
            RdStatus = TorrentStatus.Finished,
            RdStatusRaw = "completed",
            RdProgress = 100,
            HostDownloadAction = TorrentHostDownloadAction.DownloadAll,
            Type = DownloadType.Torrent,
            DownloadClient = DownloadClient.Bezzad,
            IncludeRegex = includeRegex,
            Completed = completed ? DateTimeOffset.UtcNow : null,
            FilesSelected = completed ? DateTimeOffset.UtcNow : null,
            RetryCount = retryCount
        });
        await context.SaveChangesAsync();
    }

    private DataContext CreateContext()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
        }.ToString();

        var options = new DbContextOptionsBuilder<DataContext>()
                      .UseSqlite(connectionString)
                      .Options;

        return new(options);
    }

    private TorrentsService CreateService(TorBoxDebridClient? torBoxDebridClient = null)
    {
        var context = CreateContext();
        var torrentData = new TorrentData(context);
        var downloadData = new DownloadData(context);
        var enricherMock = new Mock<IEnricher>();
        enricherMock.Setup(m => m.EnrichMagnetLink(It.IsAny<String>())).ReturnsAsync((String value) => value);
        enricherMock.Setup(m => m.EnrichTorrentBytes(It.IsAny<Byte[]>())).ReturnsAsync((Byte[] value) => value);

        return new(Mock.Of<ILogger<TorrentsService>>(),
                   torrentData,
                   new Downloads(downloadData),
                   Mock.Of<IProcessFactory>(),
                   new MockFileSystem(),
                   enricherMock.Object,
                   null!,
                   null!,
                   null!,
                   null!,
                   torBoxDebridClient!);
    }

    private static String Magnet()
    {
        return $"magnet:?xt=urn:btih:{Hash}&dn=Collection";
    }

    private TorBoxDebridClient BuildTorBoxClient()
    {
        var loggerMock = new Mock<ILogger<TorBoxDebridClient>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(m => m.CreateClient(It.IsAny<String>())).Returns(new HttpClient());

        var fileFilterMock = new Mock<IDownloadableFileFilter>();
        fileFilterMock.Setup(m => m.IsDownloadable(It.IsAny<Torrent>(), It.IsAny<String>(), It.IsAny<Int64>())).Returns(true);

        var coordinatorMock = new Mock<IRateLimitCoordinator>();
        var torrentsApiMock = new Mock<ITorrentsApi>();
        var torBoxNetClientMock = new Mock<ITorBoxNetClient>();

        torBoxNetClientMock.Setup(m => m.Torrents).Returns(torrentsApiMock.Object);
        torrentsApiMock.Setup(m => m.GetHashInfoAsync(It.Is<String>(value => value == Hash), true, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new TorrentInfoResult { Id = 12345 });

        var clientMock = new Mock<TorBoxDebridClient>(loggerMock.Object, httpClientFactoryMock.Object, fileFilterMock.Object, coordinatorMock.Object)
        {
            CallBase = true
        };
        clientMock.Protected().Setup<ITorBoxNetClient>("GetClient", ItExpr.IsAny<String>()).Returns(torBoxNetClientMock.Object);

        return clientMock.Object;
    }
}
