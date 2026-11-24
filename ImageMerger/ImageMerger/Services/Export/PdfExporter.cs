using ImageMagick;
using ImageMerger.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace ImageMerger.Services.Export;

/// <summary>
/// Xuất PDF bằng PdfPig. Ảnh: mỗi ảnh một trang, kích thước trang theo ảnh, nén JPEG bên trong.
/// File PDF đầu vào: chép nguyên các trang (giữ chữ, không nén).
/// </summary>
public sealed class PdfExporter
{
    /// <summary>Quy đổi pixel → point theo 96 DPI (màn hình), nên ảnh 1920 px rộng 20 inch.</summary>
    private const double PointsPerPixel = 72.0 / 96.0;

    /// <summary>Acrobat không mở được trang lớn hơn 200 inch (14400 pt), ví dụ ảnh chụp màn hình rất dài.</summary>
    private const double MaxPageSizePoints = 14400;

    /// <summary>
    /// Gộp các nguồn theo đúng thứ tự trong <paramref name="sources"/> và ghi ra <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="progress">Nhận giá trị 0..1 sau mỗi nguồn (một ảnh hoặc một file PDF).</param>
    /// <returns>Số trang của file PDF đã xuất.</returns>
    public Task<int> ExportAsync(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // Encode ảnh tốn CPU, chạy nền để UI không bị đơ.
        return Task.Run(() => Export(sources, outputPath, options, progress, cancellationToken), cancellationToken);
    }

    private static int Export(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        int pageCount = 0;
        ExportFile.WriteAtomically(outputPath, tempPath =>
        {
            pageCount = 0;
            using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // Mỗi file PDF nguồn chỉ mở một lần, và giữ mở tới khi ghi xong file đầu ra cho chắc chắn.
            var openPdfs = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Builder ghi từng trang thẳng xuống stream, nên RAM chỉ giữ một ảnh tại một thời điểm.
                // Dispose() ghi phần kết thúc file (bảng xref, trailer).
                // KHÔNG gọi Build(): nó đọc lại cả file vào một byte[] (đo thử: file 153 MB thì RAM lên 679 MB).
                using var builder = new PdfDocumentBuilder(stream, false, PdfWriterType.Default, 1.7, null);

                for (int i = 0; i < sources.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ImageSourceInfo source = sources[i];
                    if (SupportedFormats.IsPdf(source.Path))
                    {
                        PdfDocument pdf = OpenPdf(openPdfs, source.Path);
                        pageCount += AddPdfPages(builder, pdf, source.PageIndex, cancellationToken);
                    }
                    else
                    {
                        AddImagePage(builder, source, options);
                        pageCount++;
                    }

                    progress?.Report((i + 1) / (double)sources.Count);
                }
            }
            finally
            {
                foreach (PdfDocument pdf in openPdfs.Values) pdf.Dispose();
            }
        });
        return pageCount;
    }

    // --- File PDF nguồn: chép nguyên trang ---

    private static PdfDocument OpenPdf(Dictionary<string, PdfDocument> openPdfs, string path)
    {
        if (openPdfs.TryGetValue(path, out PdfDocument? pdf)) return pdf;
        try
        {
            pdf = PdfDocument.Open(path);
        }
        catch (Exception ex)
        {
            // PdfPig báo lỗi bằng nhiều kiểu exception (file hỏng, có mật khẩu...): gom lại thành message có tên file.
            throw new InvalidDataException($"Không đọc được file PDF \"{Path.GetFileName(path)}\".", ex);
        }
        openPdfs.Add(path, pdf);
        return pdf;
    }

    /// <summary>
    /// Chép trang <paramref name="pageIndex"/>, hoặc cả file nếu <c>null</c>.
    /// PdfPig tự chép mọi thứ trang cần (font, ảnh, tài nguyên), nên chữ vẫn bôi đen được.
    /// </summary>
    /// <returns>Số trang đã chép.</returns>
    private static int AddPdfPages(PdfDocumentBuilder builder, PdfDocument pdf, int? pageIndex, CancellationToken cancellationToken)
    {
        if (pageIndex is int index)
        {
            builder.AddPage(pdf, index + 1); // PdfPig đánh số trang từ 1
            return 1;
        }

        for (int pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AddPage(pdf, pageNumber);
        }
        return pdf.NumberOfPages;
    }

    // --- Ảnh: nén JPEG, một trang ---

    private static void AddImagePage(PdfDocumentBuilder builder, ImageSourceInfo source, ExportOptions options)
    {
        EncodedPage page = EncodePage(source, options);
        (double width, double height) = GetPageSize(page.PixelWidth, page.PixelHeight);

        // AddJpeg nhúng nguyên byte JPEG (bộ lọc /DCTDecode), không nén lại; ảnh được kéo phủ kín trang.
        // PdfPig tự đọc header JPEG để biết ảnh xám hay màu.
        PdfPageBuilder pdfPage = builder.AddPage(width, height);
        pdfPage.AddJpeg(page.Jpeg, new PdfRectangle(0, 0, width, height));
    }

    private sealed record EncodedPage(byte[] Jpeg, int PixelWidth, int PixelHeight);

    /// <summary>Đọc một ảnh gốc, chuẩn hoá và nén JPEG. Ảnh gốc được Dispose ngay khi xong.</summary>
    private static EncodedPage EncodePage(ImageSourceInfo source, ExportOptions options)
    {
        using MagickImage image = ExportImageLoader.Load(source, options.MaxWidth);
        ExportImageLoader.PrepareForJpeg(image);
        image.Quality = (uint)options.Quality;
        byte[] jpeg = image.ToByteArray(MagickFormat.Jpeg);
        return new EncodedPage(jpeg, (int)image.Width, (int)image.Height);
    }

    private static (double Width, double Height) GetPageSize(int pixelWidth, int pixelHeight)
    {
        double width = pixelWidth * PointsPerPixel;
        double height = pixelHeight * PointsPerPixel;

        double scale = Math.Min(1.0, MaxPageSizePoints / Math.Max(width, height));
        return (width * scale, height * scale);
    }
}
