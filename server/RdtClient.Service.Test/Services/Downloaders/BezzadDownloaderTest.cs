using RdtClient.Service.Services.Downloaders;

namespace RdtClient.Service.Test.Services.Downloaders;

public class BezzadDownloaderTest
{
    [Fact]
    public void FinalizeCompletedDownload_RenamesDownloaderTempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            var finalPath = Path.Combine(directory, "book.epub");
            var tempPath = $"{finalPath}.download";

            File.WriteAllText(tempPath, "content");

            var error = BezzadDownloader.FinalizeCompletedDownload(finalPath);

            Assert.Null(error);
            Assert.True(File.Exists(finalPath));
            Assert.False(File.Exists(tempPath));
            Assert.Equal("content", File.ReadAllText(finalPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FinalizeCompletedDownload_LeavesExistingFinalFileUntouched()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            var finalPath = Path.Combine(directory, "book.epub");
            var tempPath = $"{finalPath}.download";

            File.WriteAllText(finalPath, "final");
            File.WriteAllText(tempPath, "temp");

            var error = BezzadDownloader.FinalizeCompletedDownload(finalPath);

            Assert.Null(error);
            Assert.Equal("final", File.ReadAllText(finalPath));
            Assert.True(File.Exists(tempPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void FinalizeCompletedDownload_ReturnsErrorWhenFinalAndTempFilesAreMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            var finalPath = Path.Combine(directory, "book.epub");

            var error = BezzadDownloader.FinalizeCompletedDownload(finalPath);

            Assert.NotNull(error);
            Assert.Contains("no materialized file was found", error);
            Assert.Contains(finalPath, error);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
