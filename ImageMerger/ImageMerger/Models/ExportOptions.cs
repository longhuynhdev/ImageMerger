namespace ImageMerger.Models;

/// <summary>
/// Tuỳ chọn xuất file đã "giải" xong preset: exporter chỉ đọc các giá trị cụ thể ở đây,
/// không cần biết preset là gì.
/// </summary>
public sealed record ExportOptions
{
    public ExportFormat Format { get; init; } = ExportFormat.Pdf;

    /// <summary>Preset đã chọn, chỉ để hiển thị/ghi nhớ. Exporter không đọc trường này.</summary>
    public CompressionPreset Preset { get; init; } = CompressionPreset.Balanced;

    /// <summary>Quality 1–100: JPEG trong PDF, hoặc WebP. PNG không dùng.</summary>
    public int Quality { get; init; } = 85;

    /// <summary>Chỉ WebP: nén không mất dữ liệu.</summary>
    public bool Lossless { get; init; }

    /// <summary>Chỉ PNG: giảm còn 256 màu (PNG-8).</summary>
    public bool ReduceColors { get; init; }

    /// <summary>Chiều rộng tối đa (px); ảnh rộng hơn sẽ được thu nhỏ. <c>null</c> = giữ nguyên.</summary>
    public int? MaxWidth { get; init; }

    /// <summary>Chỉ PNG/WebP: khoảng cách giữa các ảnh khi ghép (px).</summary>
    public int Spacing { get; init; }

    /// <summary>Chỉ PNG/WebP: màu nền (thấy ở khoảng cách và vùng trong suốt).</summary>
    public ArgbColor Background { get; init; } = ArgbColor.White;
}
