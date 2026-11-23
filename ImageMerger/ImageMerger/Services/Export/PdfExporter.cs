using ImageMagick;
using ImageMerger.Models;

namespace ImageMerger.Services.Export;

/// <summary>
/// Xuất PDF: mỗi ảnh một trang, kích thước trang theo kích thước ảnh, ảnh nén JPEG bên trong.
/// </summary>
public sealed class PdfExporter : IExporter
{
    /// <summary>Quy đổi pixel → point theo 96 DPI (màn hình), nên ảnh 1920 px rộng 20 inch.</summary>
    private const double PointsPerPixel = 72.0 / 96.0;

    /// <summary>Acrobat không mở được trang lớn hơn 200 inch (14400 pt), ví dụ ảnh chụp màn hình rất dài.</summary>
    private const double MaxPageSizePoints = 14400;

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
        // Ghi vào file tạm rồi mới đổi tên: nếu huỷ hoặc lỗi giữa chừng thì không để lại file PDF hỏng.
        string tempPath = outputPath + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var writer = new JpegPdfWriter(stream);
                for (int i = 0; i < sources.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    EncodedPage page = EncodePage(sources[i], options.Quality);
                    (double width, double height) = GetPageSize(page.PixelWidth, page.PixelHeight);
                    writer.AddPage(page.Jpeg, page.PixelWidth, page.PixelHeight, page.IsGray, width, height);

                    progress?.Report((i + 1) / (double)sources.Count);
                }
                writer.Finish();
            }
            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath); // không báo lỗi nếu file không tồn tại
            throw;
        }
    }

    private sealed record EncodedPage(byte[] Jpeg, int PixelWidth, int PixelHeight, bool IsGray);

    /// <summary>Đọc một ảnh gốc, chuẩn hoá và nén JPEG. Ảnh gốc được Dispose ngay khi xong.</summary>
    private static EncodedPage EncodePage(ImageSourceInfo source, int quality)
    {
        try
        {
            using var image = new MagickImage(source.Path);
            image.AutoOrient();

            // JPEG không có kênh trong suốt: phủ nền trắng thay vì để vùng trong suốt thành đen.
            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }

            // PDF ở đây chỉ khai báo RGB hoặc Gray, nên đưa ảnh CMYK... về sRGB.
            if (image.ColorSpace is not ColorSpace.sRGB and not ColorSpace.Gray)
            {
                image.ColorSpace = ColorSpace.sRGB;
            }

            image.Strip(); // bỏ EXIF/metadata: ảnh đã được xoay đúng, không cần nữa
            image.Quality = (uint)quality;
            byte[] jpeg = image.ToByteArray(MagickFormat.Jpeg);

            // Hỏi lại chính file JPEG vừa tạo xem là 1 kênh (Gray) hay 3 kênh (RGB),
            // vì ImageMagick có thể tự ghi ảnh trắng đen thành JPEG xám.
            bool isGray = new MagickImageInfo(jpeg).ColorSpace == ColorSpace.Gray;

            return new EncodedPage(jpeg, (int)image.Width, (int)image.Height, isGray);
        }
        catch (MagickException ex)
        {
            throw new InvalidDataException($"Không đọc được ảnh \"{Path.GetFileName(source.Path)}\".", ex);
        }
    }

    private static (double Width, double Height) GetPageSize(int pixelWidth, int pixelHeight)
    {
        double width = pixelWidth * PointsPerPixel;
        double height = pixelHeight * PointsPerPixel;

        double scale = Math.Min(1.0, MaxPageSizePoints / Math.Max(width, height));
        return (width * scale, height * scale);
    }
}
