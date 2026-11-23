using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMerger.Helpers;
using ImageMerger.Services;

namespace ImageMerger.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IFilePickerService _filePickerService;
    private readonly ThumbnailService _thumbnailService;

    public MainViewModel(IFilePickerService filePickerService, ThumbnailService thumbnailService)
    {
        _filePickerService = filePickerService;
        _thumbnailService = thumbnailService;

        // ObservableCollection báo cho ListView mỗi khi thêm/xoá phần tử,
        // nhưng không báo cho IsEmpty/HasItems. Ta tự báo khi collection thay đổi.
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public bool HasItems => !IsEmpty;

    /// <summary>
    /// [RelayCommand] sinh ra property <c>AddImagesCommand</c> (kiểu IAsyncRelayCommand).
    /// Với method async, command tự chặn bấm lặp trong lúc đang chạy.
    /// </summary>
    [RelayCommand]
    private async Task AddImagesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> paths = await _filePickerService.PickImagesAsync();
        if (paths.Count == 0) return;

        // Picker không trả file theo thứ tự click, nên sắp xếp theo tên (natural sort).
        var newItems = paths
            .OrderBy(System.IO.Path.GetFileName, NaturalStringComparer.Instance)
            .Select(path => new ImageItemViewModel(path))
            .ToList();

        // Thêm ngay vào danh sách để người dùng thấy, thumbnail sẽ hiện dần.
        foreach (var item in newItems)
        {
            Items.Add(item);
        }

        // Tạo thumbnail lần lượt từng ảnh: mỗi lúc chỉ một ảnh gốc nằm trong RAM.
        foreach (var item in newItems)
        {
            await item.LoadThumbnailAsync(_thumbnailService, cancellationToken);
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasItems));
    }
}
