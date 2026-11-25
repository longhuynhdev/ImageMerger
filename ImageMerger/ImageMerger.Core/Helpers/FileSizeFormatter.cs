namespace ImageMerger.Helpers;

public static class FileSizeFormatter
{
    /// <summary>Ví dụ: 3.1 MB, 240 KB, 512 B.</summary>
    public static string Format(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}
