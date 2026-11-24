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
public sealed class ImageItemViewModel : BindableBase
{
    private readonly long _fileSizeBytes;
    private int _pageCount = 1;
    private string _pageNumberText = "";
    private bool _isFirst;
    private bool _isLast;
    private string _sizeText;
    private ImageSource? _thumbnail;
    private bool _isLoading = true;
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
        IsPdf = SupportedFormats.IsPdf(path);

        var fileInfo = new FileInfo(path);
        _fileSizeBytes = fileInfo.Length;
        LastWriteTime = fileInfo.LastWriteTime;
        _sizeText = FileSizeFormatter.Format(_fileSizeBytes);

        _moveCallback = moveCallback;
        _removeCallback = removeCallback;

        // Mỗi command là một object ICommand; XAML bind vào các property command bên dưới.
        MoveToTopCommand = new RelayCommand(() => _moveCallback(this, MoveTarget.Top), () => !IsFirst);
        MoveUpCommand = new RelayCommand(() => _moveCallback(this, MoveTarget.Up), () => !IsFirst);
        MoveDownCommand = new RelayCommand(() => _moveCallback(this, MoveTarget.Down), () => !IsLast);
        MoveToBottomCommand = new RelayCommand(() => _moveCallback(this, MoveTarget.Bottom), () => !IsLast);
        RemoveCommand = new RelayCommand(() => _removeCallback(this));
    }

    public string Path { get; }

    public string DisplayName { get; }

    /// <summary>File PDF: cả file là một item, các trang được chép nguyên khi xuất.</summary>
    public bool IsPdf { get; }

    /// <summary>
    /// Số trang item này chiếm trong file xuất: ảnh là 1, PDF là số trang của file.
    /// Với PDF, chỉ biết sau khi tạo xong thumbnail; trước đó tạm tính 1.
    /// </summary>
    public int PageCount
    {
        get => _pageCount;
        private set => SetProperty(ref _pageCount, value);
    }

    /// <summary>Dung lượng file gốc, để so sánh với dung lượng PDF ước tính.</summary>
    public long FileSizeBytes => _fileSizeBytes;

    /// <summary>Ngày sửa file, dùng cho "Sắp xếp theo ngày".</summary>
    public DateTime LastWriteTime { get; }

    /// <summary>Vị trí trong file xuất, ví dụ "3" (ảnh) hoặc "3–22" (PDF 20 trang).</summary>
    public string PageNumberText
    {
        get => _pageNumberText;
        private set => SetProperty(ref _pageNumberText, value);
    }

    /// <summary>MainViewModel gọi mỗi khi danh sách hoặc số trang đổi.</summary>
    /// <param name="firstPage">Số trang (bắt đầu từ 1) của trang đầu tiên của item này.</param>
    public void SetFirstPage(int firstPage) =>
        PageNumberText = PageCount > 1 ? $"{firstPage}–{firstPage + PageCount - 1}" : firstPage.ToString();

    /// <summary>
    /// MainViewModel cập nhật hai cờ này mỗi khi danh sách thay đổi.
    /// Khi cờ đổi, báo cho các command liên quan kiểm tra lại CanExecute,
    /// nhờ đó mục menu "Lên 1" tự mờ đi khi ảnh đã ở đầu.
    /// </summary>
    public bool IsFirst
    {
        get => _isFirst;
        set
        {
            if (SetProperty(ref _isFirst, value))
            {
                MoveToTopCommand.NotifyCanExecuteChanged();
                MoveUpCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLast
    {
        get => _isLast;
        set
        {
            if (SetProperty(ref _isLast, value))
            {
                MoveDownCommand.NotifyCanExecuteChanged();
                MoveToBottomCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RelayCommand MoveToTopCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public RelayCommand MoveToBottomCommand { get; }

    public RelayCommand RemoveCommand { get; }

    /// <summary>Thông tin ảnh nguồn, có sau khi đọc xong header. Dùng cho Export ở Phase 3.</summary>
    public ImageSourceInfo? Source { get; private set; }

    // Mỗi property có field riêng; setter gọi SetProperty để raise PropertyChanged,
    // nhờ đó x:Bind Mode=OneWay tự cập nhật UI.

    /// <summary>Ví dụ "4032×3024 · 3.1 MB". Trước khi đọc xong ảnh chỉ có dung lượng.</summary>
    public string SizeText
    {
        get => _sizeText;
        set => SetProperty(ref _sizeText, value);
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    /// <summary>
    /// Tạo thumbnail. Phải được gọi từ UI thread: phần decode chạy nền trong <c>Task.Run</c>,
    /// còn sau <c>await</c> code tự quay lại UI thread (nhờ SynchronizationContext của WinUI),
    /// nên tạo <see cref="BitmapImage"/> ở đây là an toàn.
    /// </summary>
    public async Task LoadThumbnailAsync(ThumbnailService thumbnailService, CancellationToken cancellationToken)
    {
        try
        {
            if (IsPdf)
            {
                PdfThumbnailResult pdf = await thumbnailService.CreatePdfThumbnailAsync(
                    Path, ThumbnailService.DefaultMaxSize, cancellationToken);
                PageCount = pdf.PageCount;
                SizeText = $"PDF · {pdf.PageCount} trang · {FileSizeFormatter.Format(_fileSizeBytes)}";
                Thumbnail = await CreateBitmapAsync(pdf.EncodedPng);
                return;
            }

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
        catch (Exception ex) when (ex is ImageMagick.MagickException or IOException || IsPdf)
        {
            // Windows.Data.Pdf báo lỗi (PDF có mật khẩu, file hỏng) bằng Exception chung kèm HResult,
            // nên với PDF bắt mọi lỗi. Lúc xuất, PdfExporter sẽ báo lỗi chi tiết hơn.
            SizeText = IsPdf ? "Không đọc được file PDF" : "Không đọc được ảnh";
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
