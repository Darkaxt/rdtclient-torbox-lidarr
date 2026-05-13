using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using RdtClient.Data.Models.QBittorrent;
using RdtClient.Service.Services;
using RdtClient.Web.Controllers;

namespace RdtClient.Web.Test.Controllers;

public class QBittorrentControllerTest
{
    private readonly QBittorrentController _controller;
    private readonly Mock<QBittorrent> _qBittorrentMock;

    public QBittorrentControllerTest()
    {
        _qBittorrentMock = new(new Mock<ILogger<QBittorrent>>().Object,
                               null!,
                               null!,
                               null!,
                               null!,
                               new DownloadableFileFilter(new Mock<ILogger<DownloadableFileFilter>>().Object));

        _controller = new(
            new Mock<ILogger<QBittorrentController>>().Object,
            _qBittorrentMock.Object,
            new Mock<IHttpClientFactory>().Object);

        _controller.ControllerContext = new()
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    [Fact]
    public async Task TorrentsInfo_FilterAll_DoesNotFilterOutResults()
    {
        // Arrange
        _qBittorrentMock.Setup(q => q.TorrentInfo()).ReturnsAsync(new List<TorrentInfo>
        {
            new()
            {
                Hash = "hash1",
                State = "pausedUP",
                Progress = 1f
            }
        });

        // Act
        var result = await _controller.TorrentsInfo(new()
        {
            Filter = "all",
            Hashes = "hash1"
        });

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IList<TorrentInfo>>(okResult.Value);
        Assert.Single(payload);
        Assert.Equal("hash1", payload[0].Hash);
    }

    [Fact]
    public async Task TorrentsInfo_FilterCompleted_MatchesPausedUploadTorrents()
    {
        // Arrange
        _qBittorrentMock.Setup(q => q.TorrentInfo()).ReturnsAsync(new List<TorrentInfo>
        {
            new()
            {
                Hash = "hash1",
                State = "pausedUP",
                Progress = 1f
            },
            new()
            {
                Hash = "hash2",
                State = "downloading",
                Progress = 0.4f
            }
        });

        // Act
        var result = await _controller.TorrentsInfo(new()
        {
            Filter = "completed"
        });

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsAssignableFrom<IList<TorrentInfo>>(okResult.Value);
        Assert.Single(payload);
        Assert.Equal("hash1", payload[0].Hash);
    }

    [Fact]
    public async Task TorrentsDelete_ContinuesAfterOneHashFails()
    {
        // Arrange
        _qBittorrentMock.Setup(q => q.TorrentsDelete("slow", true)).ThrowsAsync(new TimeoutException("provider delete timeout"));
        _qBittorrentMock.Setup(q => q.TorrentsDelete("fast", true)).Returns(Task.CompletedTask);

        // Act
        var result = await _controller.TorrentsDelete(new QBTorrentsDeleteRequest
        {
            Hashes = "slow|fast",
            DeleteFiles = true
        });

        // Assert
        Assert.IsType<OkResult>(result);
        _qBittorrentMock.Verify(q => q.TorrentsDelete("slow", true), Times.Once);
        _qBittorrentMock.Verify(q => q.TorrentsDelete("fast", true), Times.Once);
    }

    [Fact]
    public async Task TorrentsDelete_ContinuesAfterOneHashHangs()
    {
        // Arrange
        var previousTimeout = QBittorrentController.DeletePerHashTimeout;
        QBittorrentController.DeletePerHashTimeout = TimeSpan.FromMilliseconds(50);
        try
        {
            var neverCompletes = new TaskCompletionSource();
            _qBittorrentMock.Setup(q => q.TorrentsDelete("hung", true)).Returns(neverCompletes.Task);
            _qBittorrentMock.Setup(q => q.TorrentsDelete("next", true)).Returns(Task.CompletedTask);

            // Act
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _controller.TorrentsDelete(new QBTorrentsDeleteRequest
            {
                Hashes = "hung|next",
                DeleteFiles = true
            });
            sw.Stop();

            // Assert
            Assert.IsType<OkResult>(result);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"delete took {sw.Elapsed}");
            _qBittorrentMock.Verify(q => q.TorrentsDelete("hung", true), Times.Once);
            _qBittorrentMock.Verify(q => q.TorrentsDelete("next", true), Times.Once);
        }
        finally
        {
            QBittorrentController.DeletePerHashTimeout = previousTimeout;
        }
    }
}
