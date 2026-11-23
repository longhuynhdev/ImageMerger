namespace ImageMerger.Services;

/// <summary>
/// Danh sách định dạng đầu vào, dùng chung cho File Picker và kéo thả.
/// Phase 7 sẽ thêm .webp, .heic, .heif, .pdf vào đây.
/// </summary>
public static class SupportedFormats
{
    public static IReadOnlyList<string> InputExtensions { get; } = (string[])[".png", ".jpg", ".jpeg"];

    public static bool IsSupportedInput(string path) =>
        InputExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
