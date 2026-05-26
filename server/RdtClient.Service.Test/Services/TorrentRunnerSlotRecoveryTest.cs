using Moq;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services;
using DownloadClient = RdtClient.Service.Services.DownloadClient;

namespace RdtClient.Service.Test.Services;

public class TorrentRunnerSlotRecoveryTest : IDisposable
{
    public void Dispose()
    {
        TorrentRunner.ActiveDownloadClients.Clear();
        TorrentRunner.ActiveUnpackClients.Clear();
    }

    [Fact]
    public async Task RecoverStaleDownloads_WhenActiveBezzadDownloadHasNoProgress_RequeuesAndFreesSlot()
    {
        var now = DateTimeOffset.UtcNow;
        var downloadId = Guid.NewGuid();
        var torrent = new Torrent
        {
            TorrentId = Guid.NewGuid(),
            RdName = "stale audiobook",
            DownloadClient = RdtClient.Data.Enums.DownloadClient.Bezzad,
            DownloadRetryAttempts = 2,
            Downloads =
            [
                new()
                {
                    DownloadId = downloadId,
                    DownloadQueued = now.AddMinutes(-20),
                    DownloadStarted = now.AddMinutes(-16),
                    RetryCount = 0
                }
            ]
        };
        torrent.Downloads[0].Torrent = torrent;

        var activeClient = new DownloadClient(torrent.Downloads[0], torrent, "/downloads", "bindery");
        TorrentRunner.ActiveDownloadClients.TryAdd(downloadId, activeClient);

        var downloads = new Mock<IDownloads>();

        var recovered = await TorrentRunner.RecoverStaleDownloads([torrent], downloads.Object, now, TimeSpan.FromMinutes(15));

        Assert.Equal(1, recovered);
        Assert.False(TorrentRunner.ActiveDownloadClients.ContainsKey(downloadId));
        downloads.Verify(d => d.Reset(downloadId), Times.Once);
        downloads.Verify(d => d.UpdateRetryCount(downloadId, 1), Times.Once);
        downloads.Verify(d => d.UpdateError(It.IsAny<Guid>(), It.IsAny<String?>()), Times.Never);
        downloads.Verify(d => d.UpdateCompleted(It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
    }

    [Fact]
    public async Task RecoverStaleDownloads_WhenRetryBudgetIsExhausted_FailsDownloadAndFreesSlot()
    {
        var now = DateTimeOffset.UtcNow;
        var downloadId = Guid.NewGuid();
        var torrent = new Torrent
        {
            TorrentId = Guid.NewGuid(),
            RdName = "exhausted audiobook",
            DownloadClient = RdtClient.Data.Enums.DownloadClient.Bezzad,
            DownloadRetryAttempts = 1,
            Downloads =
            [
                new()
                {
                    DownloadId = downloadId,
                    DownloadQueued = now.AddMinutes(-20),
                    DownloadStarted = now.AddMinutes(-16),
                    RetryCount = 1
                }
            ]
        };
        torrent.Downloads[0].Torrent = torrent;

        var activeClient = new DownloadClient(torrent.Downloads[0], torrent, "/downloads", "bindery");
        TorrentRunner.ActiveDownloadClients.TryAdd(downloadId, activeClient);

        var downloads = new Mock<IDownloads>();

        var recovered = await TorrentRunner.RecoverStaleDownloads([torrent], downloads.Object, now, TimeSpan.FromMinutes(15));

        Assert.Equal(1, recovered);
        Assert.False(TorrentRunner.ActiveDownloadClients.ContainsKey(downloadId));
        downloads.Verify(d => d.Reset(It.IsAny<Guid>()), Times.Never);
        downloads.Verify(d => d.UpdateRetryCount(It.IsAny<Guid>(), It.IsAny<Int32>()), Times.Never);
        downloads.Verify(d => d.UpdateError(downloadId, It.Is<String>(s => s.Contains("stale Bezzad download", StringComparison.OrdinalIgnoreCase))), Times.Once);
        downloads.Verify(d => d.UpdateCompleted(downloadId, It.IsAny<DateTimeOffset?>()), Times.Once);
    }

    [Fact]
    public async Task TryRequeuePreStartUnrestrictFailure_WhenTorBoxRequestDownloadIsTransient_RequeuesDownloadAndClearsTorrentCompletion()
    {
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var now = DateTimeOffset.UtcNow;
            var downloadId = Guid.NewGuid();
            var torrentId = Guid.NewGuid();
            var torrent = new Torrent
            {
                TorrentId = torrentId,
                RdName = "selected audiobook",
                IncludeRegex = "book\\.m4b",
                DownloadRetryAttempts = 3,
                Completed = now,
                Error = "1/1 downloads failed with errors",
                Downloads =
                [
                    new()
                    {
                        DownloadId = downloadId,
                        TorrentId = torrentId,
                        Path = "https://torbox.app/fakedl/31761821/9",
                        RetryCount = 0,
                        Completed = now,
                        Error = "There was an error processing your request. Please try again later."
                    }
                ]
            };
            torrent.Downloads[0].Torrent = torrent;

            var downloads = new Mock<IDownloads>();
            var torrentData = new Mock<RdtClient.Data.Data.ITorrentData>();
            var torrents = new Torrents(null!,
                                        torrentData.Object,
                                        downloads.Object,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!);

            var handled = await TorrentRunner.TryRequeuePreStartUnrestrictFailure(torrent.Downloads[0],
                                                                                  downloads.Object,
                                                                                  torrents,
                                                                                  new Exception("There was an error processing your request. Please try again later."));

            Assert.True(handled);
            downloads.Verify(d => d.Reset(downloadId), Times.Once);
            downloads.Verify(d => d.UpdateRetryCount(downloadId, 1), Times.Once);
            downloads.Verify(d => d.UpdateError(It.IsAny<Guid>(), It.IsAny<String?>()), Times.Never);
            downloads.Verify(d => d.UpdateCompleted(It.IsAny<Guid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
            torrentData.Verify(t => t.UpdateComplete(torrentId, null, null, false), Times.Once);
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }

    [Fact]
    public async Task TryRequeuePreStartUnrestrictFailure_WhenRetryBudgetIsExhausted_DoesNotRequeue()
    {
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var downloadId = Guid.NewGuid();
            var torrent = new Torrent
            {
                TorrentId = Guid.NewGuid(),
                IncludeRegex = "book\\.m4b",
                DownloadRetryAttempts = 1,
                Downloads =
                [
                    new()
                    {
                        DownloadId = downloadId,
                        Path = "https://torbox.app/fakedl/31761821/9",
                        RetryCount = 1
                    }
                ]
            };
            torrent.Downloads[0].Torrent = torrent;

            var downloads = new Mock<IDownloads>();
            var torrentData = new Mock<RdtClient.Data.Data.ITorrentData>();
            var torrents = new Torrents(null!,
                                        torrentData.Object,
                                        downloads.Object,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!,
                                        null!);

            var handled = await TorrentRunner.TryRequeuePreStartUnrestrictFailure(torrent.Downloads[0],
                                                                                  downloads.Object,
                                                                                  torrents,
                                                                                  new Exception("There was an error processing your request. Please try again later."));

            Assert.False(handled);
            downloads.Verify(d => d.Reset(It.IsAny<Guid>()), Times.Never);
            downloads.Verify(d => d.UpdateRetryCount(It.IsAny<Guid>(), It.IsAny<Int32>()), Times.Never);
            torrentData.Verify(t => t.UpdateComplete(It.IsAny<Guid>(), It.IsAny<String?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<Boolean>()), Times.Never);
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }
}
