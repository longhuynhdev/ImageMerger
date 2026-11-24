using ImageMagick;
using ImageMagick.Formats;
using Windows.Storage.Streams;

namespace ImageMerger.Services;

/// <summary>Kết quả tạo thumbnail: ảnh nhỏ đã encode PNG và kích thước thật của ảnh gốc.</summary>
/// <param name="EncodedPng">Thumbnail dạng PNG, để UI tự decode thành <c>BitmapImage</c>.</param>
/// <param name="PixelWidth">Chiều rộng ảnh gốc sau khi xoay theo EXIF.</param>
/// <param name="PixelHeight">Chiều cao ảnh gốc sau khi xoay theo EXIF.</param>
public sealed record ThumbnailResult(byte[] EncodedPng, int PixelWidth, int PixelHeight);

/// <summary>
/// Tạo thumbnail bằng Magick.NET. Service này không biết gì về WinUI: nó chỉ trả về byte[],
/// việc biến byte[] thành ảnh hiển thị là của ViewModel trên UI thread.
/// Hàm chạy đồng bộ và tốn CPU, người gọi phải chạy nó trong <c>Task.Run</c>.
/// </summary>
public sealed class ThumbnailService
{
    public const uint DefaultMaxSize = 240;

    public ThumbnailResult CreateThumbnail(string path, uint maxSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Chỉ đọc header để lấy kích thước gốc, không decode pixel.
        (int width, int height) = ImageHeader.ReadOrientedSize(path);

        // Với JPEG: báo trước kích thước cần, libjpeg sẽ decode ở độ phân giải thấp hơn
        // (1/2, 1/4, 1/8), nhanh và tốn ít RAM hơn nhiều so với decode ảnh 12 MP rồi mới thu nhỏ.
        var settings = new MagickReadSettings();
        settings.SetDefines(new JpegReadDefines { Size = new MagickGeometry(maxSize * 2, maxSize * 2) });

        cancellationToken.ThrowIfCancellationRequested();
        using var image = new MagickImage(path, settings);
        image.AutoOrient();
        // Greater = true: chỉ thu nhỏ, không phóng to ảnh vốn đã nhỏ hơn maxSize.
        // Thumbnail() giữ tỉ lệ và bỏ metadata nên kết quả rất nhẹ.
        image.Thumbnail(new MagickGeometry(maxSize, maxSize) { Greater = true });

        return new ThumbnailResult(image.ToByteArray(MagickFormat.Png), width, height);
    }

    /// <summary>
    /// Thumbnail của file PDF: render trang 1 bằng <c>Windows.Data.Pdf</c> (có sẵn trong Windows,
    /// không cần Ghostscript như Magick.NET), kèm tổng số trang.
    /// Khác <see cref="CreateThumbnail"/>, hàm này là async thật: các API WinRT tự chạy việc nặng ở nền.
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

/// <summary>Kết quả thumbnail của file PDF.</summary>
/// <param name="EncodedPng">Trang 1 đã render thành PNG.</param>
/// <param name="PageCount">Tổng số trang của file.</param>
public sealed record PdfThumbnailResult(byte[] EncodedPng, int PageCount);
