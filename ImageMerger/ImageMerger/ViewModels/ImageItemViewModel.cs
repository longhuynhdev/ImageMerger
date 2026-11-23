using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMerger.Helpers;
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
    private readonly Action<ImageItemViewModel, MoveTarget> _moveCallback;
    private readonly Action<ImageItemViewModel> _removeCallback;

    /// <param name="path">Đường dẫn file ảnh.</param>
    /// <param name="moveCallback">Gọi về MainViewModel để di chuyển item này (item không tự biết vị trí của mình).</param>
    /// <param name="removeCallback">Gọi về MainViewModel để xoá item này khỏi danh sách.</param>
    public ImageItemViewModel(
        string path,
        Action<ImageItemViewModel, MoveTarget> moveCallback,
        Action<ImageItemViewModel> removeCallback)
    {
        Path = path;
        DisplayName = System.IO.Path.GetFileName(path);

        var fileInfo = new FileInfo(path);
        _fileSizeBytes = fileInfo.Length;
        LastWriteTime = fileInfo.LastWriteTime;
        SizeText = FileSizeFormatter.Format(_fileSizeBytes);

        _moveCallback = moveCallback;
        _removeCallback = removeCallback;
    }

    public string Path { get; }

    public string DisplayName { get; }

    /// <summary>Ngày sửa file, dùng cho "Sắp xếp theo ngày".</summary>
    public DateTime LastWriteTime { get; }

    /// <summary>
    /// MainViewModel cập nhật hai cờ này mỗi khi danh sách thay đổi.
    /// [NotifyCanExecuteChangedFor] báo cho command liên quan kiểm tra lại CanExecute,
    /// nhờ đó mục menu "Lên 1" tự mờ đi khi ảnh đã ở đầu.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveToTopCommand), nameof(MoveUpCommand))]
    public partial bool IsFirst { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand), nameof(MoveToBottomCommand))]
    public partial bool IsLast { get; set; }

    private bool CanMoveUp() => !IsFirst;

    private bool CanMoveDown() => !IsLast;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveToTop() => _moveCallback(this, MoveTarget.Top);

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _moveCallback(this, MoveTarget.Up);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _moveCallback(this, MoveTarget.Down);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveToBottom() => _moveCallback(this, MoveTarget.Bottom);

    [RelayCommand]
    private void Remove() => _removeCallback(this);

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
            SizeText = $"{result.PixelWidth}×{result.PixelHeight} · {FileSizeFormatter.Format(_fileSizeBytes)}";
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
}
