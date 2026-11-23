namespace ImageMerger.Models;

/// <summary>
/// Tuỳ chọn xuất file. Phase 3 chỉ có định dạng và quality.
/// Phase 4–5 sẽ thêm Preset, MaxWidth, Spacing, Background.
/// </summary>
public sealed record ExportOptions
{
    public ExportFormat Format { get; init; } = ExportFormat.Pdf;

    /// <summary>Quality JPEG (1–100) cho ảnh nhúng trong PDF.</summary>
    public int Quality { get; init; } = 85;
}
