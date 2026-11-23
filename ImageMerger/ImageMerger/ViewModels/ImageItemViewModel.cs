using CommunityToolkit.Mvvm.ComponentModel;
using ImageMerger.Models;
using ImageMerger.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ImageMerger.ViewModels;

/// <summary>
/// Một dòng trong danh sách ảnh. Chỉ giữ thumbnail nhỏ, không giữ ảnh gốc.
/// </summary>
public sealed partial class ImageItemViewModel : ObservableObject
{
    private readonly long _fileSizeBytes;

    public ImageItemViewModel(string path)
    {
        Path = path;
        DisplayName = System.IO.Path.GetFileName(path);
        _fileSizeBytes = new FileInfo(path).Length;
        SizeText = FormatFileSize(_fileSizeBytes);
    }

    public string Path { get; }

    public string DisplayName { get; }

    /// <summary>Thông tin ảnh nguồn, có sau khi đọc xong header. Dùng cho Export ở Phase 3.</summary>
    public ImageSourceInfo? Source { get; private set; }

    // [ObservableProperty] trên partial property: CommunityToolkit.Mvvm tự sinh phần thân
    // property và gọi PropertyChanged, nên x:Bind Mode=OneWay tự cập nhật UI.

    /// <summary>Ví dụ "4032×3024 · 3.1 MB". Trước khi đọc xong ảnh chỉ có dung lượng.</summary>
    [ObservableProperty]
    public partial string SizeText { get; set; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    /// <summary>
    /// Tạo thumbnail. Phải được gọi từ UI thread: phần decode chạy nền trong <c>Task.Run</c>,
    /// còn sau <c>await</c> code tự quay lại UI thread (nhờ SynchronizationContext của WinUI),
    /// nên tạo <see cref="BitmapImage"/> ở đây là an toàn.
    /// </summary>
    public async Task LoadThumbnailAsync(ThumbnailService thumbnailService, CancellationToken cancellationToken)
    {
        try
        {
            ThumbnailResult result = await Task.Run(
                () => thumbnailService.CreateThumbnail(Path, ThumbnailService.DefaultMaxSize, cancellationToken),
                cancellationToken);

            Source = new ImageSourceInfo(Path, null, result.PixelWidth, result.PixelHeight);
            SizeText = $"{result.PixelWidth}×{result.PixelHeight} · {FormatFileSize(_fileSizeBytes)}";
            Thumbnail = await CreateBitmapAsync(result.EncodedPng);
        }
        catch (OperationCanceledException)
        {
            // Bị huỷ (ví dụ đóng app): không làm gì.
        }
        catch (Exception ex) when (ex is ImageMagick.MagickException or IOException)
        {
            SizeText = "Không đọc được ảnh";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// <see cref="BitmapImage"/> là ImageSource của WinUI, đọc được PNG/JPEG từ một stream.
    /// <c>SetSourceAsync</c> cần <see cref="IRandomAccessStream"/> của WinRT nên ta chép byte[] vào
    /// <see cref="InMemoryRandomAccessStream"/>.
    /// </summary>
    private static async Task<BitmapImage> CreateBitmapAsync(byte[] encodedImage)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(encodedImage);
            await writer.StoreAsync();
            writer.DetachStream(); // để dispose writer mà không đóng stream
        }
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}
