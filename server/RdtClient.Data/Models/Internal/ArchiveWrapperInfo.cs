namespace RdtClient.Data.Models.Internal;

public class ArchiveWrapperInfo
{
    public required String Hash { get; set; }

    public required String FilePath { get; set; }

    public required String FileName { get; set; }

    public Int64 Size { get; set; }

    public required String DownloadUrl { get; set; }
}
