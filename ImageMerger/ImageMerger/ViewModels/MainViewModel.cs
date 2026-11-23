using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ImageMerger.Helpers;
using ImageMerger.Models;
using ImageMerger.Services;
using ImageMerger.Services.Export;
using Microsoft.UI.Xaml.Controls;

namespace ImageMerger.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private bool _isStatusOpen;
    private string _statusTitle = "";
    private string _statusMessage = "";
    private InfoBarSeverity _statusSeverity;
    private bool _isExporting;
    private string _exportingText = "";
    private double _exportProgress;

    /// <summary>Huỷ lần xuất đang chạy. Chỉ khác null trong lúc đang xuất.</summary>
    private CancellationTokenSource? _exportCts;

    private readonly IFilePickerService _filePickerService;
    private readonly ThumbnailService _thumbnailService;
    private readonly IReadOnlyList<IExporter> _exporters;

    /// <summary>Các ảnh đang chờ tạo thumbnail. Chỉ truy cập trên UI thread nên không cần lock.</summary>
    private readonly Queue<ImageItemViewModel> _thumbnailQueue = new();
    private bool _isProcessingThumbnails;

    /// <param name="exporters">Mỗi định dạng một exporter (PDF, PNG, WebP).</param>
    public MainViewModel(IFilePickerService filePickerService, ThumbnailService thumbnailService, IReadOnlyList<IExporter> exporters)
    {
        _filePickerService = filePickerService;
        _thumbnailService = thumbnailService;
        _exporters = exporters;

        AddImagesCommand = new AsyncRelayCommand(AddImagesAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => HasItems);
        ExportCancelCommand = new RelayCommand(() => _exportCts?.Cancel(), () => IsExporting);
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

    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>Nút "Huỷ" cạnh thanh tiến độ: huỷ <see cref="_exportCts"/> của lần xuất đang chạy.</summary>
    public RelayCommand ExportCancelCommand { get; }

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
            .Select(path => new ImageItemViewModel(path, MoveItem, RemoveItem))
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
                await item.LoadThumbnailAsync(_thumbnailService, CancellationToken.None);
            }
        }
        finally
        {
            _isProcessingThumbnails = false;
        }
    }

    // --- Xuất file ---

    /// <summary>Tuỳ chọn của hộp thoại Xuất. Giữ lại giữa các lần xuất.</summary>
    public ExportViewModel ExportSettings { get; } = new();

    /// <summary>Khi IsExporting đổi thì CanExport và nút Huỷ cũng đổi theo, nên báo luôn cho chúng.</summary>
    public bool IsExporting
    {
        get => _isExporting;
        set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
                ExportCancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Nút "Xuất ảnh" mở hộp thoại từ code-behind, nên cần một property để bật/tắt nó.</summary>
    public bool CanExport => HasItems && !IsExporting;

    /// <summary>Ví dụ "Đang xuất WebP…", hiện cạnh thanh tiến độ.</summary>
    public string ExportingText
    {
        get => _exportingText;
        set => SetProperty(ref _exportingText, value);
    }

    /// <summary>Tiến độ xuất, 0–100, bind vào ProgressBar.</summary>
    public double ExportProgress
    {
        get => _exportProgress;
        set => SetProperty(ref _exportProgress, value);
    }

    /// <summary>
    /// Được gọi sau khi người dùng bấm "Xuất" trong ExportDialog, dùng tuỳ chọn trong <see cref="ExportSettings"/>.
    /// Mỗi lần xuất tạo một <see cref="CancellationTokenSource"/> mới; <see cref="ExportCancelCommand"/> gọi
    /// <c>Cancel()</c> trên nó, và exporter dừng ở lần kiểm tra token kế tiếp.
    /// </summary>
    private async Task ExportAsync()
    {
        ExportOptions options = ExportSettings.ToOptions();
        IExporter exporter = _exporters.Single(e => e.Format == options.Format);
        string formatName = options.Format switch
        {
            ExportFormat.Png => "PNG",
            ExportFormat.Webp => "WebP",
            _ => "PDF",
        };

        string? outputPath = await _filePickerService.PickSaveFileAsync(
            "Ảnh đã gộp", $"Tệp {formatName}", ExportSettings.FileExtension);
        if (outputPath is null) return;

        // Chụp lại danh sách tại thời điểm bấm Xuất. Kích thước có thể chưa biết (thumbnail chưa xong),
        // exporter không cần vì nó tự đọc lại ảnh gốc.
        var sources = Items
            .Select(item => item.Source ?? new ImageSourceInfo(item.Path, null, 0, 0))
            .ToList();

        // Progress<T> tạo trên UI thread sẽ gọi callback trên UI thread,
        // nên exporter báo tiến độ từ luồng nền vẫn cập nhật property an toàn.
        var progress = new Progress<double>(value => ExportProgress = value * 100);

        _exportCts = new CancellationTokenSource();
        IsExporting = true;
        ExportingText = $"Đang xuất {formatName}…";
        ExportProgress = 0;
        IsStatusOpen = false;
        try
        {
            await exporter.ExportAsync(sources, outputPath, options, progress, _exportCts.Token);

            long size = new FileInfo(outputPath).Length;
            ShowStatus(
                InfoBarSeverity.Success,
                $"Đã xuất {sources.Count} ảnh ({FileSizeFormatter.Format(size)})",
                outputPath);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Informational, "Đã huỷ xuất file", "");
        }
        catch (ExportValidationException ex)
        {
            // Lỗi biết trước (ví dụ vượt giới hạn WebP): message đã kèm gợi ý cách sửa.
            ShowStatus(InfoBarSeverity.Warning, $"Không thể xuất {formatName}", ex.Message);
        }
        catch (Exception ex)
        {
            // Ranh giới với UI: mọi lỗi (file hỏng, ổ đầy, không có quyền ghi...) đều báo cho người dùng.
            ShowStatus(InfoBarSeverity.Error, "Xuất file thất bại", ex.Message);
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
            IsExporting = false;
        }
    }

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
        OnPropertiesChanged(nameof(IsEmpty), nameof(HasItems), nameof(CanExport));
        SortByNameCommand.NotifyCanExecuteChanged();
        SortByDateCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();

        // Cập nhật cờ đầu/cuối để menu Lên/Xuống của từng item bật/tắt đúng.
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].IsFirst = i == 0;
            Items[i].IsLast = i == Items.Count - 1;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }
}
