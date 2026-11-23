using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMerger.Helpers;
using ImageMerger.Models;
using ImageMerger.Services;
using ImageMerger.Services.Export;
using Microsoft.UI.Xaml.Controls;

namespace ImageMerger.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFilePickerService _filePickerService;
    private readonly ThumbnailService _thumbnailService;
    private readonly IExporter _pdfExporter;

    /// <summary>Các ảnh đang chờ tạo thumbnail. Chỉ truy cập trên UI thread nên không cần lock.</summary>
    private readonly Queue<ImageItemViewModel> _thumbnailQueue = new();
    private bool _isProcessingThumbnails;

    public MainViewModel(IFilePickerService filePickerService, ThumbnailService thumbnailService, IExporter pdfExporter)
    {
        _filePickerService = filePickerService;
        _thumbnailService = thumbnailService;
        _pdfExporter = pdfExporter;

        // ObservableCollection báo cho ListView mỗi khi thêm/xoá phần tử,
        // nhưng không báo cho IsEmpty/HasItems. Ta tự báo khi collection thay đổi.
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool HasItems => !IsEmpty;

    // --- Thông báo (InfoBar) ---

    /// <summary>TwoWay: InfoBar tự đặt lại false khi người dùng bấm nút đóng.</summary>
    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = "";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; }

    // --- Thêm ảnh ---

    /// <summary>
    /// [RelayCommand] sinh ra property <c>AddImagesCommand</c> (kiểu IAsyncRelayCommand).
    /// Với method async, command tự chặn bấm lặp trong lúc đang chạy.
    /// </summary>
    [RelayCommand]
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

    [ObservableProperty]
    public partial bool IsExporting { get; set; }

    /// <summary>Tiến độ xuất, 0–100, bind vào ProgressBar.</summary>
    [ObservableProperty]
    public partial double ExportProgress { get; set; }

    /// <summary>
    /// IncludeCancelCommand = true: toolkit sinh thêm <c>ExportCancelCommand</c>.
    /// Gọi command đó sẽ huỷ <paramref name="cancellationToken"/> của lần xuất đang chạy,
    /// nên nút "Huỷ" chỉ cần bind vào nó, không phải tự quản lý CancellationTokenSource.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(HasItems))]
    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        string? outputPath = await _filePickerService.PickSaveFileAsync("Ảnh đã gộp", "Tài liệu PDF", ".pdf");
        if (outputPath is null) return;

        // Chụp lại danh sách tại thời điểm bấm Xuất. Kích thước có thể chưa biết (thumbnail chưa xong),
        // exporter không cần vì nó tự đọc lại ảnh gốc.
        var sources = Items
            .Select(item => item.Source ?? new ImageSourceInfo(item.Path, null, 0, 0))
            .ToList();

        // Progress<T> tạo trên UI thread sẽ gọi callback trên UI thread,
        // nên exporter báo tiến độ từ luồng nền vẫn cập nhật property an toàn.
        var progress = new Progress<double>(value => ExportProgress = value * 100);

        IsExporting = true;
        ExportProgress = 0;
        IsStatusOpen = false;
        try
        {
            await _pdfExporter.ExportAsync(sources, outputPath, new ExportOptions(), progress, cancellationToken);

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
        catch (Exception ex)
        {
            // Ranh giới với UI: mọi lỗi (file hỏng, ổ đầy, không có quyền ghi...) đều báo cho người dùng.
            ShowStatus(InfoBarSeverity.Error, "Xuất file thất bại", ex.Message);
        }
        finally
        {
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

    [RelayCommand(CanExecute = nameof(HasItems))]
    private void SortByName() =>
        ApplyOrder(Items.OrderBy(i => i.DisplayName, NaturalStringComparer.Instance));

    [RelayCommand(CanExecute = nameof(HasItems))]
    private void SortByDate() =>
        ApplyOrder(Items.OrderBy(i => i.LastWriteTime));

    [RelayCommand(CanExecute = nameof(HasItems))]
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
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasItems));
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
