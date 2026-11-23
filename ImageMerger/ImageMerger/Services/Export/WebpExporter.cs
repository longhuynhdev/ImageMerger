using ImageMagick;
using ImageMagick.Formats;
using ImageMerger.Models;

namespace ImageMerger.Services.Export;

public sealed class WebpExporter : VerticalMergeExporter
{
    /// <summary>Giới hạn của định dạng WebP: mỗi chiều tối đa 16383 px.</summary>
    public const int MaxDimension = 16383;

    public override ExportFormat Format => ExportFormat.Webp;

    protected override void Validate(VerticalMergeLayout layout)
    {
        if (layout.Width <= MaxDimension && layout.Height <= MaxDimension) return;

        int suggestedWidth = Math.Min(layout.MaxWidthToFitHeight(MaxDimension), MaxDimension);
        string suggestion = suggestedWidth > 0
            ? $"Hãy chọn mức nén Tuỳ chỉnh và đặt chiều rộng tối đa ≤ {suggestedWidth} px, hoặc xuất PNG."
            : "Hãy giảm khoảng cách giữa các ảnh, bớt ảnh, hoặc xuất PNG.";

        throw new ExportValidationException(
            $"Ảnh ghép sẽ có kích thước {layout.Width}×{layout.Height} px, " +
            $"nhưng WebP chỉ hỗ trợ tối đa {MaxDimension} px mỗi chiều. {suggestion}");
    }

    protected override void Encode(MagickImage canvas, ExportOptions options, string path)
    {
        canvas.Quality = (uint)options.Quality;
        canvas.Write(path, new WebPWriteDefines { Lossless = options.Lossless });
    }
}
