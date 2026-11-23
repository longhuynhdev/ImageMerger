using ImageMagick;
using ImageMagick.Formats;

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
        var info = new MagickImageInfo(path);
        bool isRotated = info.Orientation is Orientation.LeftTop or Orientation.RightTop
                                          or Orientation.RightBottom or Orientation.LeftBottom;
        int width = (int)(isRotated ? info.Height : info.Width);
        int height = (int)(isRotated ? info.Width : info.Height);

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
}
