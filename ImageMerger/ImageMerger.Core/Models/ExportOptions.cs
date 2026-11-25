namespace ImageMerger.Models;

/// <summary>
/// Tuỳ chọn xuất PDF đã "giải" xong preset: exporter chỉ đọc các giá trị cụ thể ở đây,
/// không cần biết preset là gì.
/// </summary>
public sealed record ExportOptions
{
    /// <summary>Preset đã chọn, chỉ để hiển thị/ghi nhớ. Exporter không đọc trường này.</summary>
    public CompressionPreset Preset { get; init; } = CompressionPreset.Balanced;

    /// <summary>Quality 1–100 của ảnh JPEG bên trong PDF.</summary>
    public int Quality { get; init; } = 85;

    /// <summary>Chiều rộng tối đa (px); ảnh rộng hơn sẽ được thu nhỏ. <c>null</c> = giữ nguyên.</summary>
    public int? MaxWidth { get; init; }
}
