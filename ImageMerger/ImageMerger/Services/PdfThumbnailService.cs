using Windows.Storage.Streams;

namespace ImageMerger.Services;

/// <summary>Kết quả thumbnail của file PDF.</summary>
/// <param name="EncodedPng">Trang 1 đã render thành PNG.</param>
/// <param name="PageCount">Tổng số trang của file.</param>
public sealed record PdfThumbnailResult(byte[] EncodedPng, int PageCount);

/// <summary>
/// Thumbnail của file PDF. Nằm ở project app thay vì ImageMerger.Core vì dùng <c>Windows.Data.Pdf</c>,
/// API chỉ có trên Windows.
/// </summary>
public sealed class PdfThumbnailService
{
    /// <summary>
    /// Render trang 1 bằng <c>Windows.Data.Pdf</c> (có sẵn trong Windows,
    /// không cần Ghostscript như Magick.NET), kèm tổng số trang.
    /// Khác <see cref="ThumbnailService.CreateThumbnail"/>, hàm này là async thật: các API WinRT tự chạy việc nặng ở nền.
    /// </summary>
    /// <exception cref="InvalidDataException">PDF không có trang nào.</exception>
    public async Task<PdfThumbnailResult> CreatePdfThumbnailAsync(string path, uint maxSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Windows.Data.Pdf đọc từ IRandomAccessStream của WinRT; AsRandomAccessStream() bọc FileStream của .NET.
        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Windows.Data.Pdf.PdfDocument pdf =
            await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(fileStream.AsRandomAccessStream()).AsTask(cancellationToken);
        if (pdf.PageCount == 0) throw new InvalidDataException("File PDF không có trang nào.");

        using Windows.Data.Pdf.PdfPage page = pdf.GetPage(0);

        // Size tính theo DIP (1/96 inch). Thu nhỏ để cạnh dài bằng maxSize, giữ tỉ lệ trang.
        double scale = maxSize / Math.Max(page.Size.Width, page.Size.Height);
        var options = new Windows.Data.Pdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
        };

        // Mặc định RenderToStreamAsync ghi ảnh PNG, nền trắng.
        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, options).AsTask(cancellationToken);

        using var png = new MemoryStream();
        await output.AsStreamForRead().CopyToAsync(png, cancellationToken);
        return new PdfThumbnailResult(png.ToArray(), (int)pdf.PageCount);
    }
}
