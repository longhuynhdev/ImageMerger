using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ImageMerger.Helpers;
using ImageMerger.Models;
using ImageMerger.Services;
using ImageMerger.Services.Compression;
using ImageMerger.Services.Export;
using Microsoft.UI.Xaml.Controls;

namespace ImageMerger.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private bool _isStatusOpen;
    private string _statusTitle = "";
    private string _statusMessage = "";
    private InfoBarSeverity _statusSeverity;

    private readonly IFilePickerService _filePickerService;
    private readonly ThumbnailService _thumbnailService;
    private readonly PdfThumbnailService _pdfThumbnailService;

    /// <summary>Các ảnh đang chờ tạo thumbnail. Chỉ truy cập trên UI thread nên không cần lock.</summary>
    private readonly Queue<ImageItemViewModel> _thumbnailQueue = new();
    private bool _isProcessingThumbnails;

    public MainViewModel(
        IFilePickerService filePickerService,
        ThumbnailService thumbnailService,
        PdfThumbnailService pdfThumbnailService,
        PdfExporter pdfExporter,
        ISizeEstimator sizeEstimator)
    {
        _filePickerService = filePickerService;
        _thumbnailService = thumbnailService;
        _pdfThumbnailService = pdfThumbnailService;

        // Kích thước có thể chưa biết (thumbnail chưa xong), exporter và bộ ước tính không cần vì chúng tự đọc lại ảnh gốc.
        Export = new ExportViewModel(filePickerService, pdfExporter, sizeEstimator, () => Items
            .SelectMany(item => item.GetSources())
            .ToList());

        AddImagesCommand = new AsyncRelayCommand(AddImagesAsync);
        SortByNameCommand = new RelayCommand(SortByName, () => HasItems);
        SortByDateCommand = new RelayCommand(SortByDate, () => HasItems);
        ClearAllCommand = new RelayCommand(ClearAll, () => HasItems);

        // ObservableCollection báo cho ListView mỗi khi thêm/xoá phần tử,
        // nhưng không báo cho IsEmpty/HasItems. Ta tự báo khi collection thay đổi.
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool HasItems => !IsEmpty;

    /// <summary>Ví dụ "12 trang", hiện cạnh tiêu đề danh sách. Một file PDF đóng góp nhiều trang.</summary>
    public string PageCountText => $"{Items.Sum(i => i.PageCount)} trang";

    // --- Thông báo (InfoBar) ---

    /// <summary>TwoWay: InfoBar tự đặt lại false khi người dùng bấm nút đóng.</summary>
    public bool IsStatusOpen
    {
        get => _isStatusOpen;
        set => SetProperty(ref _isStatusOpen, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public InfoBarSeverity StatusSeverity
    {
        get => _statusSeverity;
        set => SetProperty(ref _statusSeverity, value);
    }

    // --- Command ---
    // Các command được tạo trong constructor. AsyncRelayCommand tự chặn bấm lặp trong lúc đang chạy.

    public AsyncRelayCommand AddImagesCommand { get; }

    public RelayCommand SortByNameCommand { get; }

    public RelayCommand SortByDateCommand { get; }

    public RelayCommand ClearAllCommand { get; }

    // --- Thêm ảnh ---

    private async Task AddImagesAsync()
    {
        IReadOnlyList<string> paths = await _filePickerService.PickImagesAsync();
        AddFiles(paths);
    }

    /// <summary>
    /// Thêm file vào cuối danh sách. Dùng chung cho File Picker và kéo thả từ Explorer.
    /// File không hỗ trợ bị bỏ qua và được báo trong InfoBar.
    /// </summary>
    public void AddFiles(IReadOnlyList<string> paths)
    {
        var unsupported = paths.Where(p => !SupportedFormats.IsSupportedInput(p)).ToList();
        if (unsupported.Count > 0)
        {
            ShowStatus(
                InfoBarSeverity.Warning,
                $"Đã bỏ qua {unsupported.Count} file không được hỗ trợ",
                string.Join(", ", unsupported.Select(Path.GetFileName)));
        }

        // Picker và Explorer không trả file theo thứ tự click, nên sắp xếp theo tên (natural sort).
        var newItems = paths
            .Where(SupportedFormats.IsSupportedInput)
            .OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
            .Select(path => new ImageItemViewModel(path, MoveItem, RemoveItem, OnItemPagesChanged))
            .ToList();

        // Thêm ngay vào danh sách để người dùng thấy, thumbnail sẽ hiện dần.
        foreach (var item in newItems)
        {
            Items.Add(item);
            _thumbnailQueue.Enqueue(item);
        }

        // Không await: command "Thêm ảnh" kết thúc ngay, người dùng thêm tiếp được
        // trong khi thumbnail vẫn đang được tạo.
        _ = ProcessThumbnailQueueAsync();
    }

    /// <summary>
    /// Tạo thumbnail lần lượt từng ảnh: mỗi lúc chỉ một ảnh gốc nằm trong RAM.
    /// Nếu vòng lặp đang chạy thì lần gọi mới chỉ cần thêm vào hàng đợi.
    /// </summary>
    private async Task ProcessThumbnailQueueAsync()
    {
        if (_isProcessingThumbnails) return;
        _isProcessingThumbnails = true;
        try
        {
            while (_thumbnailQueue.TryDequeue(out var item))
            {
                if (!Items.Contains(item)) continue; // đã bị xoá trong lúc chờ
                await item.LoadThumbnailAsync(_thumbnailService, _pdfThumbnailService, CancellationToken.None);
            }
        }
        finally
        {
            _isProcessingThumbnails = false;
        }
    }

    // --- Xuất file ---

    /// <summary>Thanh xuất PDF ở đáy trang. Tạo một lần và giữ lựa chọn suốt phiên làm việc.</summary>
    public ExportViewModel Export { get; }

    // --- Sắp xếp, xoá ---

    /// <summary>Callback mà mỗi ImageItemViewModel gọi khi người dùng chọn Lên/Xuống trong menu.</summary>
    private void MoveItem(ImageItemViewModel item, MoveTarget target)
    {
        int oldIndex = Items.IndexOf(item);
        if (oldIndex < 0) return;

        int newIndex = target switch
        {
            MoveTarget.Top => 0,
            MoveTarget.Up => oldIndex - 1,
            MoveTarget.Down => oldIndex + 1,
            MoveTarget.Bottom => Items.Count - 1,
            _ => oldIndex,
        };
        newIndex = Math.Clamp(newIndex, 0, Items.Count - 1);

        // Move() phát một sự kiện "Move" duy nhất, ListView chỉ dời đúng một dòng.
        if (newIndex != oldIndex) Items.Move(oldIndex, newIndex);
    }

    private void RemoveItem(ImageItemViewModel item) => Items.Remove(item);

    /// <summary>
    /// Callback khi một file PDF vừa đọc xong (biết số trang) hoặc người dùng sửa ô chọn trang:
    /// đánh số lại các dòng, và bật/tắt nút Xuất theo việc còn ô chọn trang nào sai không.
    /// </summary>
    private void OnItemPagesChanged(ImageItemViewModel item)
    {
        UpdatePageNumbers();
        Export.SetPageRangeErrors(Items.Any(i => i.HasPageRangeError));
    }

    private void SortByName() =>
        ApplyOrder(Items.OrderBy(i => i.DisplayName, NaturalStringComparer.Instance));

    private void SortByDate() =>
        ApplyOrder(Items.OrderBy(i => i.LastWriteTime));

    private void ClearAll()
    {
        Items.Clear();
        _thumbnailQueue.Clear();
    }

    /// <summary>Đưa Items về đúng thứ tự mới bằng Move(), không xoá rồi thêm lại (tránh tạo lại thumbnail).</summary>
    private void ApplyOrder(IEnumerable<ImageItemViewModel> ordered)
    {
        var target = ordered.ToList(); // chụp lại thứ tự trước khi bắt đầu Move
        for (int i = 0; i < target.Count; i++)
        {
            int current = Items.IndexOf(target[i]);
            if (current != i) Items.Move(current, i);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasItems));
        SortByNameCommand.NotifyCanExecuteChanged();
        SortByDateCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
        // Xoá một item đang nhập sai thì nút Xuất phải bật lại.
        Export.SetPageRangeErrors(Items.Any(i => i.HasPageRangeError));
        Export.SetItems(Items.Count, Items.FirstOrDefault()?.Path, Items.Sum(i => i.FileSizeBytes));

        // Cập nhật cờ đầu/cuối để menu Lên/Xuống của từng item bật/tắt đúng.
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].IsFirst = i == 0;
            Items[i].IsLast = i == Items.Count - 1;
        }
        UpdatePageNumbers();
    }

    /// <summary>Đánh số trang trong file xuất cho từng dòng. Một file PDF chiếm nhiều trang liên tiếp.</summary>
    private void UpdatePageNumbers()
    {
        int nextPage = 1;
        foreach (ImageItemViewModel item in Items)
        {
            item.SetFirstPage(nextPage);
            nextPage += item.PageCount;
        }
        OnPropertyChanged(nameof(PageCountText));
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }
}
