namespace ImageMerger.Models;

/// <summary>
/// Các giá trị nén cụ thể của một preset, cho cả ba định dạng.
/// Preset "Tuỳ chỉnh" không có trong bảng: ExportViewModel tự tạo PresetSettings từ các control.
/// </summary>
/// <param name="WebpQuality">Quality WebP 1–100 (bỏ qua khi <paramref name="WebpLossless"/>).</param>
/// <param name="WebpLossless">WebP không mất dữ liệu.</param>
/// <param name="PdfQuality">Quality JPEG bên trong PDF, 1–100.</param>
/// <param name="PngReduceColors">Giảm còn 256 màu (PNG-8).</param>
/// <param name="MaxWidth">Chiều rộng tối đa; <c>null</c> = giữ nguyên.</param>
public sealed record PresetSettings(
    int WebpQuality,
    bool WebpLossless,
    int PdfQuality,
    bool PngReduceColors,
    int? MaxWidth)
{
    public static PresetSettings Original { get; } = new(100, WebpLossless: true, 95, PngReduceColors: false, MaxWidth: null);

    public static PresetSettings Balanced { get; } = new(80, WebpLossless: false, 85, PngReduceColors: false, MaxWidth: 2048);

    public static PresetSettings Compact { get; } = new(60, WebpLossless: false, 70, PngReduceColors: true, MaxWidth: 1280);

    /// <summary>Giá trị của preset có sẵn. Không dùng cho <see cref="CompressionPreset.Custom"/>.</summary>
    public static PresetSettings For(CompressionPreset preset) => preset switch
    {
        CompressionPreset.Original => Original,
        CompressionPreset.Balanced => Balanced,
        CompressionPreset.Compact => Compact,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), "Preset Tuỳ chỉnh không có giá trị cố định."),
    };
}
