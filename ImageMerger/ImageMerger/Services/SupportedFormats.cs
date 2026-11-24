namespace ImageMerger.Services;

/// <summary>
/// Danh sách định dạng đầu vào, dùng chung cho File Picker và kéo thả.
/// Phase 7 sẽ thêm .webp, .heic, .heif vào đây.
/// </summary>
public static class SupportedFormats
{
    /// <summary>Phần mở rộng của file PDF đầu vào.</summary>
    public const string PdfExtension = ".pdf";

    public static IReadOnlyList<string> InputExtensions { get; } = (string[])[".png", ".jpg", ".jpeg", PdfExtension];

    public static bool IsSupportedInput(string path) =>
        InputExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>File PDF được chép nguyên trang khi xuất, không đi qua Magick.NET.</summary>
    public static bool IsPdf(string path) =>
        string.Equals(Path.GetExtension(path), PdfExtension, StringComparison.OrdinalIgnoreCase);
}
