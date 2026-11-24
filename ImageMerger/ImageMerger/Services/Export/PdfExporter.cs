using ImageMagick;
using ImageMerger.Models;

namespace ImageMerger.Services.Export;

/// <summary>
/// Xuất PDF: mỗi ảnh một trang, kích thước trang theo kích thước ảnh, ảnh nén JPEG bên trong.
/// </summary>
public sealed class PdfExporter
{
    /// <summary>Quy đổi pixel → point theo 96 DPI (màn hình), nên ảnh 1920 px rộng 20 inch.</summary>
    private const double PointsPerPixel = 72.0 / 96.0;

    /// <summary>Acrobat không mở được trang lớn hơn 200 inch (14400 pt), ví dụ ảnh chụp màn hình rất dài.</summary>
    private const double MaxPageSizePoints = 14400;

    /// <summary>
    /// Gộp các ảnh theo đúng thứ tự trong <paramref name="sources"/> và ghi ra <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="progress">Nhận giá trị 0..1 sau mỗi ảnh.</param>
    public Task ExportAsync(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // Encode ảnh tốn CPU, chạy nền để UI không bị đơ.
        return Task.Run(() => Export(sources, outputPath, options, progress, cancellationToken), cancellationToken);
    }

    private static void Export(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ExportFile.WriteAtomically(outputPath, tempPath =>
        {
            using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var writer = new JpegPdfWriter(stream);
            for (int i = 0; i < sources.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                EncodedPage page = EncodePage(sources[i], options);
                (double width, double height) = GetPageSize(page.PixelWidth, page.PixelHeight);
                writer.AddPage(page.Jpeg, page.PixelWidth, page.PixelHeight, page.IsGray, width, height);

                progress?.Report((i + 1) / (double)sources.Count);
            }
            writer.Finish();
        });
    }

    private sealed record EncodedPage(byte[] Jpeg, int PixelWidth, int PixelHeight, bool IsGray);

    /// <summary>Đọc một ảnh gốc, chuẩn hoá và nén JPEG. Ảnh gốc được Dispose ngay khi xong.</summary>
    private static EncodedPage EncodePage(ImageSourceInfo source, ExportOptions options)
    {
        using MagickImage image = ExportImageLoader.Load(source, options.MaxWidth);
        ExportImageLoader.PrepareForJpeg(image);
        image.Quality = (uint)options.Quality;
        byte[] jpeg = image.ToByteArray(MagickFormat.Jpeg);

        // Hỏi lại chính file JPEG vừa tạo xem là 1 kênh (Gray) hay 3 kênh (RGB),
        // vì ImageMagick có thể tự ghi ảnh trắng đen thành JPEG xám.
        bool isGray = new MagickImageInfo(jpeg).ColorSpace == ColorSpace.Gray;

        return new EncodedPage(jpeg, (int)image.Width, (int)image.Height, isGray);
    }

    private static (double Width, double Height) GetPageSize(int pixelWidth, int pixelHeight)
    {
        double width = pixelWidth * PointsPerPixel;
        double height = pixelHeight * PointsPerPixel;

        double scale = Math.Min(1.0, MaxPageSizePoints / Math.Max(width, height));
        return (width * scale, height * scale);
    }
}
