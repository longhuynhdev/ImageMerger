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
    private int _pdfPageCount; // 0 = chưa đọc xong file PDF (hoặc không phải PDF)
    private string _pageRangeText = "";
    private string _pageRangeError = "";
    private IReadOnlyList<int>? _selectedPages; // null = tất cả trang
    private bool _isFirst;
    private bool _isLast;
    private string _sizeText;
    private ImageSource? _thumbnail;
    private bool _isLoading = true;
    private readonly Action<ImageItemViewModel, MoveTarget> _moveCallback;
    private readonly Action<ImageItemViewModel> _removeCallback;
    private readonly Action<ImageItemViewModel> _pagesChangedCallback;

    /// <param name="path">Đường dẫn file ảnh.</param>
    /// <param name="moveCallback">Gọi về MainViewModel để di chuyển item này (item không tự biết vị trí của mình).</param>
    /// <param name="removeCallback">Gọi về MainViewModel để xoá item này khỏi danh sách.</param>
    /// <param name="pagesChangedCallback">
    /// Gọi về MainViewModel khi số trang hoặc ô chọn trang của file PDF đổi, để đánh số lại và bật/tắt nút Xuất.
    /// </param>
    public ImageItemViewModel(
        string path,
        Action<ImageItemViewModel, MoveTarget> moveCallback,
        Action<ImageItemViewModel> removeCallback,
        Action<ImageItemViewModel> pagesChangedCallback)
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
        _pagesChangedCallback = pagesChangedCallback;

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
    /// Số trang item này chiếm trong file xuất: ảnh là 1, PDF là số trang đã chọn (mặc định cả file).
    /// Với PDF, chỉ biết sau khi tạo xong thumbnail; trước đó tạm tính 1.
    /// Khi ô chọn trang đang sai, giữ số của lần nhập đúng gần nhất để cột vị trí không nhảy lung tung.
    /// </summary>
    public int PageCount
    {
        get => _pageCount;
        private set => SetProperty(ref _pageCount, value);
    }

    // --- Chọn trang (chỉ với PDF) ---

    /// <summary>
    /// Chữ người dùng gõ vào ô "Trang", ví dụ "1-3, 5". Để trống = tất cả trang.
    /// Bind với UpdateSourceTrigger=PropertyChanged nên setter chạy sau mỗi phím gõ, lỗi hiện ngay.
    /// </summary>
    public string PageRangeText
    {
        get => _pageRangeText;
        set
        {
            if (SetProperty(ref _pageRangeText, value)) ApplyPageRange();
        }
    }

    /// <summary>Chuỗi rỗng nghĩa là ô chọn trang hợp lệ.</summary>
    public string PageRangeError
    {
        get => _pageRangeError;
        private set
        {
            if (SetProperty(ref _pageRangeError, value)) OnPropertyChanged(nameof(HasPageRangeError));
        }
    }

    /// <summary>Hiện dòng chữ đỏ dưới ô chọn trang.</summary>
    public bool HasPageRangeError => PageRangeError.Length > 0;

    /// <summary>Khoá ô chọn trang cho tới khi đọc xong PDF, vì chưa biết file có bao nhiêu trang để kiểm tra.</summary>
    public bool IsPageRangeEnabled => _pdfPageCount > 0;

    /// <summary>Kiểm tra ô chọn trang, cập nhật số trang và báo cho MainViewModel.</summary>
    private void ApplyPageRange()
    {
        if (_pdfPageCount == 0) return;

        if (PageRangeParser.TryParse(_pageRangeText, _pdfPageCount, out IReadOnlyList<int>? pages, out string error))
        {
            _selectedPages = pages;
            PageCount = pages?.Count ?? _pdfPageCount;
        }
        PageRangeError = error;
        _pagesChangedCallback(this);
    }

    /// <summary>
    /// Nguồn cho exporter và bộ ước tính, theo đúng thứ tự trang trong file xuất.
    /// PDF để trống ô chọn trang thì trả về một nguồn "cả file" (PageIndex = null), dùng được cả khi chưa đọc xong file.
    /// </summary>
    public IEnumerable<ImageSourceInfo> GetSources()
    {
        if (!IsPdf) return [Source ?? new ImageSourceInfo(Path, null, 0, 0)];

        int? pdfPageCount = _pdfPageCount > 0 ? _pdfPageCount : null;
        if (_selectedPages is null) return [new ImageSourceInfo(Path, null, 0, 0, pdfPageCount)];
        return _selectedPages.Select(index => new ImageSourceInfo(Path, index, 0, 0, pdfPageCount));
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
    public async Task LoadThumbnailAsync(
        ThumbnailService thumbnailService,
        PdfThumbnailService pdfThumbnailService,
        CancellationToken cancellationToken)
    {
        try
        {
            if (IsPdf)
            {
                PdfThumbnailResult pdf = await pdfThumbnailService.CreatePdfThumbnailAsync(
                    Path, ThumbnailService.DefaultMaxSize, cancellationToken);
                _pdfPageCount = pdf.PageCount;
                OnPropertyChanged(nameof(IsPageRangeEnabled));
                ApplyPageRange(); // cập nhật PageCount và báo MainViewModel đánh số lại
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
