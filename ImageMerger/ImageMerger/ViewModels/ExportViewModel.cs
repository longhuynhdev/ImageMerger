using System.Diagnostics;
using ImageMerger.Helpers;
using ImageMerger.Models;
using ImageMerger.Services;
using ImageMerger.Services.Export;
using Microsoft.UI.Xaml.Controls;

namespace ImageMerger.ViewModels;

/// <summary>
/// Thanh xuất PDF ở đáy trang: mức nén, dung lượng ước tính, nút Xuất, tiến độ và kết quả.
/// Sống cùng MainViewModel (property <c>Export</c>) nên lựa chọn được giữ suốt phiên làm việc.
///
/// Các control Tuỳ chỉnh (slider quality, chiều rộng tối đa) chỉ hiện khi <see cref="IsCustom"/>,
/// để View không phải tự suy luận.
/// </summary>
public sealed class ExportViewModel : BindableBase
{
    public const int MinMaxWidth = 64;
    public const int MaxMaxWidth = 20000;

    public const string FileExtension = ".pdf";

    private readonly IFilePickerService _filePickerService;
    private readonly PdfExporter _pdfExporter;
    private readonly Func<IReadOnlyList<ImageSourceInfo>> _getSources;

    private CompressionPreset _preset = CompressionPreset.Balanced;
    private double _customQuality = 85;
    private double _customMaxWidth = 2048;
    private int _itemCount;
    private string _fileName = "";
    private string _defaultFileName = FallbackFileName();

    private bool _isExporting;
    private double _progress;
    private string _progressText = "";

    private bool _hasResult;
    private InfoBarSeverity _resultSeverity;
    private string _resultTitle = "";
    private string _resultMessage = "";
    private string? _resultPath;

    /// <summary>Huỷ lần xuất đang chạy. Chỉ khác null trong lúc đang xuất.</summary>
    private CancellationTokenSource? _exportCts;

    /// <param name="getSources">Chụp danh sách ảnh hiện tại (theo đúng thứ tự trên màn hình) lúc bấm Xuất.</param>
    public ExportViewModel(
        IFilePickerService filePickerService,
        PdfExporter pdfExporter,
        Func<IReadOnlyList<ImageSourceInfo>> getSources)
    {
        _filePickerService = filePickerService;
        _pdfExporter = pdfExporter;
        _getSources = getSources;

        // Thứ tự khớp enum CompressionPreset, nên SelectedPresetIndex dùng được làm chỉ số.
        Presets = (PresetOptionViewModel[])
        [
            new PresetOptionViewModel("Gốc", Describe(PresetSettings.Original)),
            new PresetOptionViewModel("Cân bằng", Describe(PresetSettings.Balanced)),
            new PresetOptionViewModel("Nhỏ gọn", Describe(PresetSettings.Compact)),
            new PresetOptionViewModel("Tuỳ chỉnh", "Tự chọn chất lượng và chiều rộng"),
        ];

        ExportCommand = new AsyncRelayCommand(
            ExportAsync, () => _itemCount > 0 && IsValid && !HasFileNameError && !IsExporting);
        CancelCommand = new RelayCommand(() => _exportCts?.Cancel(), () => IsExporting);
        OpenResultCommand = new RelayCommand(OpenResult, () => CanOpenResult);
    }

    // --- Mức nén ---

    public IReadOnlyList<PresetOptionViewModel> Presets { get; }

    public CompressionPreset Preset
    {
        get => _preset;
        set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertiesChanged(nameof(SelectedPresetIndex), nameof(IsCustom),
                    nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid));
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>RadioButtons dùng SelectedIndex (số nguyên), thứ tự item khớp thứ tự enum.</summary>
    public int SelectedPresetIndex
    {
        get => (int)Preset;
        set
        {
            if (value < 0 || (CompressionPreset)value == Preset) return;

            // Chuyển sang Tuỳ chỉnh: lấy giá trị của preset đang chọn làm điểm xuất phát,
            // người dùng chỉ việc chỉnh thêm thay vì bắt đầu từ con số lạ.
            if ((CompressionPreset)value == CompressionPreset.Custom)
            {
                CopyToCustom(PresetSettings.For(Preset));
            }
            Preset = (CompressionPreset)value;
        }
    }

    public bool IsCustom => Preset == CompressionPreset.Custom;

    // --- Giá trị Tuỳ chỉnh ---

    /// <summary>Slider.Value là double, nên property cũng là double.</summary>
    public double CustomQuality
    {
        get => _customQuality;
        set => SetProperty(ref _customQuality, value);
    }

    /// <summary>
    /// NumberBox.Value là double; ô trống cho ra NaN, nghĩa là "giữ nguyên chiều rộng".
    /// Lưu ý: NaN != NaN, nhưng EqualityComparer&lt;double&gt; coi hai NaN là bằng nhau,
    /// nên SetProperty không báo thừa khi ô vẫn trống.
    /// </summary>
    public double CustomMaxWidth
    {
        get => _customMaxWidth;
        set
        {
            if (SetProperty(ref _customMaxWidth, value))
            {
                OnPropertiesChanged(nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid));
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    // --- Kiểm tra hợp lệ ---

    /// <summary>Chuỗi rỗng nghĩa là hợp lệ.</summary>
    public string ValidationMessage =>
        IsCustom && !double.IsNaN(CustomMaxWidth) && CustomMaxWidth is < MinMaxWidth or > MaxMaxWidth
            ? $"Chiều rộng tối đa phải từ {MinMaxWidth} đến {MaxMaxWidth} px (để trống nếu giữ nguyên)."
            : "";

    public bool HasValidationError => ValidationMessage.Length > 0;

    public bool IsValid => !HasValidationError;

    // --- Tên file ---

    /// <summary>
    /// Chữ người dùng gõ vào ô "Tên file" (không cần gõ ".pdf"). Để trống thì dùng <see cref="DefaultFileName"/>.
    /// Bind với UpdateSourceTrigger=PropertyChanged nên setter chạy sau mỗi phím gõ, lỗi hiện ngay.
    /// </summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetProperty(ref _fileName, value))
            {
                OnPropertiesChanged(nameof(FileNameError), nameof(HasFileNameError));
                ExportCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Tên gợi ý (PlaceholderText): tên ảnh đầu tiên, hoặc "ImageMerger_yyyy-MM-dd".</summary>
    public string DefaultFileName
    {
        get => _defaultFileName;
        private set => SetProperty(ref _defaultFileName, value);
    }

    /// <summary>Tên file sẽ gợi ý cho FileSavePicker: đã trim, bỏ ".pdf" người dùng tự gõ, trống thì lấy tên mặc định.</summary>
    public string ResolvedFileName
    {
        get
        {
            string name = FileName.Trim();
            if (name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^FileExtension.Length].TrimEnd();
            }
            return name.Length > 0 ? name : DefaultFileName;
        }
    }

    /// <summary>Chuỗi rỗng nghĩa là tên hợp lệ.</summary>
    public string FileNameError =>
        ResolvedFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? "Tên file không được chứa các ký tự \\ / : * ? \" < > |"
            : "";

    public bool HasFileNameError => FileNameError.Length > 0;

    // --- Dung lượng ước tính ---
    // Chưa có bộ ước tính (Phase 6), nên các property dưới đây đang giữ giá trị "chưa có".
    // Phase 6 chỉ cần gán chúng, View đã bind sẵn.

    public string EstimatedSizeText => "≈ — MB";

    public string CompareText => "Ước tính dung lượng: Phase 6";

    public bool IsEstimating => false;

    public bool HasEstimate => false;

    /// <summary>Tỉ lệ dung lượng ước tính so với tổng dung lượng ảnh gốc, 0–100, cho ProgressBar trong flyout.</summary>
    public double EstimateRatio => 0;

    // --- Đang xuất ---

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(IsNotExporting));
                ExportCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>x:Bind không có toán tử "!", nên cần property ngược để ẩn nút Xuất khi đang xuất.</summary>
    public bool IsNotExporting => !IsExporting;

    /// <summary>Tiến độ xuất, 0–100, bind vào ProgressBar.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>Ví dụ "Đang xuất PDF… 45%".</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    // --- Kết quả (InfoBar) ---

    /// <summary>TwoWay: InfoBar tự đặt lại false khi người dùng bấm nút đóng.</summary>
    public bool HasResult
    {
        get => _hasResult;
        set => SetProperty(ref _hasResult, value);
    }

    /// <summary>Success khi xuất xong; Informational khi huỷ; Error khi lỗi.</summary>
    public InfoBarSeverity ResultSeverity
    {
        get => _resultSeverity;
        private set => SetProperty(ref _resultSeverity, value);
    }

    public string ResultTitle
    {
        get => _resultTitle;
        private set => SetProperty(ref _resultTitle, value);
    }

    public string ResultMessage
    {
        get => _resultMessage;
        private set => SetProperty(ref _resultMessage, value);
    }

    /// <summary>Nút "Mở file" chỉ hiện khi đã xuất thành công.</summary>
    public bool CanOpenResult => _resultPath is not null;

    // --- Command ---

    /// <summary>Phím tắt Ctrl+E và nút "Xuất PDF" đều gọi command này.</summary>
    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>Nút "Huỷ" cạnh thanh tiến độ: huỷ <see cref="_exportCts"/> của lần xuất đang chạy.</summary>
    public RelayCommand CancelCommand { get; }

    public RelayCommand OpenResultCommand { get; }

    /// <summary>
    /// MainViewModel gọi mỗi khi danh sách ảnh đổi: bật/tắt nút Xuất và cập nhật tên file mặc định.
    /// Chỉ đổi tên mặc định khi danh sách đi từ trống sang có ảnh, để tên không nhảy lung tung khi sắp xếp.
    /// Khi danh sách bị xoá hết thì quên luôn tên người dùng đã gõ (coi như bắt đầu bộ ảnh mới).
    /// </summary>
    /// <param name="count">Số ảnh hiện có.</param>
    /// <param name="firstItemPath">Đường dẫn ảnh đầu tiên, <c>null</c> nếu danh sách trống.</param>
    public void SetItems(int count, string? firstItemPath)
    {
        bool wasEmpty = _itemCount == 0;
        _itemCount = count;

        if (count == 0)
        {
            FileName = "";
            DefaultFileName = FallbackFileName();
        }
        else if (wasEmpty && firstItemPath is not null)
        {
            string name = Path.GetFileNameWithoutExtension(firstItemPath);
            DefaultFileName = name.Length > 0 ? name : FallbackFileName();
        }

        OnPropertiesChanged(nameof(FileNameError), nameof(HasFileNameError));
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Mở FileSavePicker rồi xuất. Mỗi lần xuất tạo một <see cref="CancellationTokenSource"/> mới;
    /// <see cref="CancelCommand"/> gọi <c>Cancel()</c> trên nó, và exporter dừng ở lần kiểm tra token kế tiếp.
    /// </summary>
    private async Task ExportAsync()
    {
        ExportOptions options = ToOptions();
        string? outputPath = await _filePickerService.PickSaveFileAsync(
            ResolvedFileName, "Tệp PDF", FileExtension);
        if (outputPath is null) return;

        // Chụp lại danh sách tại thời điểm bấm Xuất.
        IReadOnlyList<ImageSourceInfo> sources = _getSources();

        // Progress<T> tạo trên UI thread sẽ gọi callback trên UI thread,
        // nên exporter báo tiến độ từ luồng nền vẫn cập nhật property an toàn.
        var progress = new Progress<double>(value =>
        {
            Progress = value * 100;
            ProgressText = $"Đang xuất PDF… {Progress:0}%";
        });

        _exportCts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = "Đang xuất PDF…";
        HasResult = false;
        IsExporting = true;
        try
        {
            await _pdfExporter.ExportAsync(sources, outputPath, options, progress, _exportCts.Token);

            long size = new FileInfo(outputPath).Length;
            ShowResult(
                InfoBarSeverity.Success,
                $"Đã lưu {Path.GetFileName(outputPath)} · {sources.Count} trang · {FileSizeFormatter.Format(size)}",
                $"Ước tính: {EstimatedSizeText} · {outputPath}",
                outputPath);
        }
        catch (OperationCanceledException)
        {
            ShowResult(InfoBarSeverity.Informational, "Đã huỷ xuất file", "", null);
        }
        catch (Exception ex)
        {
            // Ranh giới với UI: mọi lỗi (file hỏng, ổ đầy, không có quyền ghi...) đều báo cho người dùng.
            ShowResult(InfoBarSeverity.Error, "Xuất file thất bại", ex.Message, null);
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
            IsExporting = false;
        }
    }

    private void ShowResult(InfoBarSeverity severity, string title, string message, string? path)
    {
        ResultSeverity = severity;
        ResultTitle = title;
        ResultMessage = message;
        _resultPath = path;
        OnPropertyChanged(nameof(CanOpenResult));
        OpenResultCommand.NotifyCanExecuteChanged();
        HasResult = true;
    }

    /// <summary>
    /// Mở file PDF bằng ứng dụng mặc định của Windows.
    /// UseShellExecute = true: để Windows tự chọn ứng dụng theo đuôi file, giống double-click trong Explorer.
    /// </summary>
    private void OpenResult()
    {
        if (_resultPath is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_resultPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowResult(InfoBarSeverity.Error, "Không mở được file", ex.Message, null);
        }
    }

    /// <summary>Gom mọi lựa chọn thành ExportOptions cụ thể cho exporter.</summary>
    public ExportOptions ToOptions()
    {
        PresetSettings s = CurrentSettings();
        return new ExportOptions
        {
            Preset = Preset,
            Quality = s.PdfQuality,
            MaxWidth = s.MaxWidth,
        };
    }

    /// <summary>Giá trị của preset đang chọn; với Tuỳ chỉnh thì lấy từ các control.</summary>
    private PresetSettings CurrentSettings()
    {
        if (!IsCustom) return PresetSettings.For(Preset);

        int quality = (int)Math.Round(CustomQuality);
        int? maxWidth = double.IsNaN(CustomMaxWidth) ? null : (int)CustomMaxWidth;
        return new PresetSettings(quality, maxWidth);
    }

    private void CopyToCustom(PresetSettings settings)
    {
        CustomQuality = settings.PdfQuality;
        CustomMaxWidth = settings.MaxWidth ?? double.NaN;
    }

    private static string FallbackFileName() => $"ImageMerger_{DateTime.Now:yyyy-MM-dd}";

    /// <summary>Ví dụ "JPEG 85 · rộng tối đa 2048 px".</summary>
    private static string Describe(PresetSettings s) =>
        s.MaxWidth is int w ? $"JPEG {s.PdfQuality} · rộng tối đa {w} px" : $"JPEG {s.PdfQuality} · giữ nguyên kích thước";
}
