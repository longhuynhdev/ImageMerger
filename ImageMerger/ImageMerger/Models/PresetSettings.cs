namespace ImageMerger.Models;

/// <summary>
/// Các giá trị nén cụ thể của một preset.
/// Preset "Tuỳ chỉnh" không có trong bảng: ExportViewModel tự tạo PresetSettings từ các control.
/// </summary>
/// <param name="PdfQuality">Quality JPEG bên trong PDF, 1–100.</param>
/// <param name="MaxWidth">Chiều rộng tối đa; <c>null</c> = giữ nguyên.</param>
public sealed record PresetSettings(int PdfQuality, int? MaxWidth)
{
    public static PresetSettings Original { get; } = new(95, MaxWidth: null);

    public static PresetSettings Balanced { get; } = new(85, MaxWidth: 2048);

    public static PresetSettings Compact { get; } = new(70, MaxWidth: 1280);

    /// <summary>Giá trị của preset có sẵn. Không dùng cho <see cref="CompressionPreset.Custom"/>.</summary>
    public static PresetSettings For(CompressionPreset preset) => preset switch
    {
        CompressionPreset.Original => Original,
        CompressionPreset.Balanced => Balanced,
        CompressionPreset.Compact => Compact,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), "Preset Tuỳ chỉnh không có giá trị cố định."),
    };
}
