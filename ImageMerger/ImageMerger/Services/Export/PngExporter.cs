using ImageMagick;
using ImageMerger.Models;

namespace ImageMerger.Services.Export;

/// <summary>
/// PNG là định dạng lossless: không có "quality". Cách duy nhất để file nhỏ đi đáng kể là
/// giảm chiều rộng (MaxWidth) hoặc giảm số màu xuống 256 (PNG-8).
/// </summary>
public sealed class PngExporter : VerticalMergeExporter
{
    public override ExportFormat Format => ExportFormat.Png;

    protected override void Encode(MagickImage canvas, ExportOptions options, string path)
    {
        if (options.ReduceColors)
        {
            // Quantize: chọn ra 256 màu đại diện. Dithering Floyd–Steinberg rải nhiễu nhẹ
            // để chuyển màu (gradient) không bị thành từng dải.
            canvas.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
            canvas.Write(path, MagickFormat.Png8);
        }
        else
        {
            canvas.Write(path, MagickFormat.Png);
        }
    }
}
