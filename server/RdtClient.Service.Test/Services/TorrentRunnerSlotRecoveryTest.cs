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
}
