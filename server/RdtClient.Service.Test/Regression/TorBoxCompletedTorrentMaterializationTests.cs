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

public class TorBoxCompletedTorrentMaterializationTests : IAsyncLifetime
{
    private readonly String _databasePath = Path.Combine(Path.GetTempPath(), $"rdt-client-torbox-materialization-{Guid.NewGuid():N}.sqlite");

    [Fact]
    public async Task Tick_WhenTorBoxTorrentIsFinishedAndFilesAreVisible_CreatesHostDownloadRows()
    {
        var torrentId = Guid.NewGuid();
        const String hash = "abcdef0123456789abcdef0123456789abcdef01";

        await SeedFinishedTorBoxTorrentAsync(torrentId, hash);

        var previousProvider = Settings.Get.Provider.Provider;
        var previousApiKey = Settings.Get.Provider.ApiKey;
        var previousDownloadPath = Settings.Get.DownloadClient.DownloadPath;
        var previousDownloadLimit = Settings.Get.General.DownloadLimit;
        var previousUnpackLimit = Settings.Get.General.UnpackLimit;
        var previousPreferZippedDownloads = Settings.Get.Provider.PreferZippedDownloads;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;
            Settings.Get.Provider.ApiKey = "test-api-key";
            Settings.Get.Provider.PreferZippedDownloads = false;
            Settings.Get.DownloadClient.DownloadPath = "/downloads";
            Settings.Get.General.DownloadLimit = 1;
            Settings.Get.General.UnpackLimit = 0;

            await using var context = CreateContext();
            var torrentData = new TorrentData(context);
            var downloadData = new DownloadData(context);
            var downloads = new Downloads(downloadData);

            var fileFilterMock = new Mock<IDownloadableFileFilter>();
            fileFilterMock.Setup(m => m.IsDownloadable(It.IsAny<Torrent>(), It.IsAny<String>(), It.IsAny<Int64>())).Returns(true);

            var torBoxClient = BuildTorBoxClient(hash, fileFilterMock.Object);
            var seededTorrent = await context.Torrents.SingleAsync(m => m.TorrentId == torrentId);
            var downloadInfos = await torBoxClient.GetDownloadInfos(seededTorrent);
            var downloadInfo = Assert.Single(downloadInfos!);
            Assert.Equal("https://torbox.app/fakedl/12345/7", downloadInfo.RestrictedLink);
            Assert.Equal("book.mp3", downloadInfo.FileName);

            var torrents = new TorrentsService(Mock.Of<ILogger<TorrentsService>>(),
                                               torrentData,
                                               downloads,
                                               Mock.Of<IProcessFactory>(),
                                               new MockFileSystem(),
                                               Mock.Of<IEnricher>(),
                                               null!,
                                               null!,
                                               null!,
                                               null!,
                                               torBoxClient);

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
            Settings.Get.Provider.PreferZippedDownloads = previousPreferZippedDownloads;
            Settings.Get.DownloadClient.DownloadPath = previousDownloadPath;
            Settings.Get.General.DownloadLimit = previousDownloadLimit;
            Settings.Get.General.UnpackLimit = previousUnpackLimit;
            TorrentRunner.ActiveDownloadClients.Clear();
            TorrentRunner.ActiveUnpackClients.Clear();
        }

        await using var verifyContext = CreateContext();
        var torrent = await verifyContext.Torrents.Include(m => m.Downloads).SingleAsync(m => m.TorrentId == torrentId);
        Assert.Null(torrent.Error);
        Assert.NotNull(torrent.FilesSelected);
        var download = Assert.Single(torrent.Downloads);
        Assert.Equal("book.mp3", download.FileName);
        Assert.Equal("https://torbox.app/fakedl/12345/7", download.Path);
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

    private async Task SeedFinishedTorBoxTorrentAsync(Guid torrentId, String hash)
    {
        var files = new List<DebridClientFile>
        {
            new()
            {
                Id = 7,
                Path = "book.mp3",
                Bytes = 1024,
                Selected = true
            }
        };

        await using var context = CreateContext();
        context.Torrents.Add(new Torrent
        {
            TorrentId = torrentId,
            Hash = hash,
            Added = DateTimeOffset.UtcNow.AddMinutes(-10),
            RdId = hash,
            RdName = "Book",
            RdStatus = TorrentStatus.Finished,
            RdStatusRaw = "completed",
            RdProgress = 100,
            HostDownloadAction = TorrentHostDownloadAction.DownloadAll,
            Type = DownloadType.Torrent,
            DownloadClient = DownloadClient.Bezzad,
            RdFiles = JsonConvert.SerializeObject(files)
        });

        await context.SaveChangesAsync();
    }

    private TorBoxDebridClient BuildTorBoxClient(String hash, IDownloadableFileFilter fileFilter)
    {
        var loggerMock = new Mock<ILogger<TorBoxDebridClient>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(m => m.CreateClient(It.IsAny<String>())).Returns(new HttpClient());

        var coordinatorMock = new Mock<IRateLimitCoordinator>();
        var torrentsApiMock = new Mock<ITorrentsApi>();
        var torBoxNetClientMock = new Mock<ITorBoxNetClient>();

        torBoxNetClientMock.Setup(m => m.Torrents).Returns(torrentsApiMock.Object);
        torrentsApiMock.Setup(m => m.GetHashInfoAsync(It.Is<String>(value => value == hash), true, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(new TorrentInfoResult
                       {
                           Id = 12345
                       });

        var clientMock = new Mock<TorBoxDebridClient>(loggerMock.Object, httpClientFactoryMock.Object, fileFilter, coordinatorMock.Object)
        {
            CallBase = true
        };

        clientMock.Protected().Setup<ITorBoxNetClient>("GetClient", ItExpr.IsAny<String>()).Returns(torBoxNetClientMock.Object);

        return clientMock.Object;
    }
}
