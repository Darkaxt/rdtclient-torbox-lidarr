using RdtClient.Data.Enums;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services;

namespace RdtClient.Service.Test.Services;

public class TorrentRunnerTest
{
    [Fact]
    public void CountsAgainstProviderDownloadLimit_IgnoresOldTorBoxStalledWithoutDownloads()
    {
        var now = DateTimeOffset.UtcNow;
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var torrent = new Torrent
            {
                Added = now.AddMinutes(-10),
                RdStatus = TorrentStatus.Downloading,
                RdStatusRaw = "stalled (no seeds)",
                RdSpeed = 0
            };

            Assert.False(TorrentRunner.CountsAgainstProviderDownloadLimit(torrent, now));
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }

    [Fact]
    public void CountsAgainstProviderDownloadLimit_KeepsFreshTorBoxStalledAsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var torrent = new Torrent
            {
                Added = now.AddMinutes(-1),
                RdStatus = TorrentStatus.Downloading,
                RdStatusRaw = "stalled (no seeds)",
                RdSpeed = 0
            };

            Assert.True(TorrentRunner.CountsAgainstProviderDownloadLimit(torrent, now));
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }

    [Fact]
    public void CountsAgainstProviderDownloadLimit_IgnoresOldTorBoxProviderQueuedWithoutDownloads()
    {
        var now = DateTimeOffset.UtcNow;
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var torrent = new Torrent
            {
                Added = now.AddMinutes(-20),
                RdAdded = now.AddMinutes(-10),
                RdStatus = TorrentStatus.Processing,
                RdStatusRaw = "queued",
                RdSpeed = 0
            };

            Assert.False(TorrentRunner.CountsAgainstProviderDownloadLimit(torrent, now));
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }

    [Fact]
    public void CountsAgainstProviderDownloadLimit_KeepsFreshTorBoxProviderQueuedAsActive()
    {
        var now = DateTimeOffset.UtcNow;
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var torrent = new Torrent
            {
                Added = now.AddMinutes(-20),
                RdAdded = now.AddMinutes(-1),
                RdStatus = TorrentStatus.Processing,
                RdStatusRaw = "queued",
                RdSpeed = 0
            };

            Assert.True(TorrentRunner.CountsAgainstProviderDownloadLimit(torrent, now));
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }

    [Fact]
    public void CountsAgainstProviderDownloadLimit_KeepsActiveTorBoxDownloadWithHostDownloads()
    {
        var now = DateTimeOffset.UtcNow;
        var previousProvider = Settings.Get.Provider.Provider;

        try
        {
            Settings.Get.Provider.Provider = Provider.TorBox;

            var torrent = new Torrent
            {
                Added = now.AddMinutes(-10),
                RdStatus = TorrentStatus.Downloading,
                RdStatusRaw = "stalled (no seeds)",
                Downloads =
                [
                    new()
                ]
            };

            Assert.True(TorrentRunner.CountsAgainstProviderDownloadLimit(torrent, now));
        }
        finally
        {
            Settings.Get.Provider.Provider = previousProvider;
        }
    }

    [Fact]
    public void CountsAgainstProviderDownloadLimit_DoesNotCountQueuedOrFinished()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(TorrentRunner.CountsAgainstProviderDownloadLimit(new() { RdStatus = TorrentStatus.Queued }, now));
        Assert.False(TorrentRunner.CountsAgainstProviderDownloadLimit(new() { RdStatus = TorrentStatus.Finished }, now));
    }
}
