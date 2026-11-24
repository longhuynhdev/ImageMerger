using System.Diagnostics;
using ImageMerger.Helpers;
using ImageMerger.Models;
using ImageMerger.Services;
using ImageMerger.Services.Compression;
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
    private readonly ISizeEstimator _sizeEstimator;
    private readonly Func<IReadOnlyList<ImageSourceInfo>> _getSources;

    /// <summary>Chờ người dùng ngừng kéo slider / gõ số bao lâu thì mới tính.</summary>
    private static readonly TimeSpan EstimateDebounce = TimeSpan.FromMilliseconds(300);

    private CompressionPreset _preset = CompressionPreset.Balanced;
    private double _customQuality = 85;
    private double _customMaxWidth = 2048;
    private int _itemCount;
    private bool _hasPageRangeErrors;
    private long _sourceBytes;
    private string _fileName = "";

    private SizeEstimate? _estimate;
    private bool _isEstimating;

    /// <summary>Huỷ lần ước tính đang chờ/đang chạy. Mỗi lần đổi tuỳ chọn tạo một cái mới.</summary>
    private CancellationTokenSource? _estimateCts;
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
        ISizeEstimator sizeEstimator,
        Func<IReadOnlyList<ImageSourceInfo>> getSources)
    {
        _filePickerService = filePickerService;
        _pdfExporter = pdfExporter;
        _sizeEstimator = sizeEstimator;
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
            ExportAsync, () => _itemCount > 0 && IsValid && !_hasPageRangeErrors && !HasFileNameError && !IsExporting);
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
                RequestEstimate();
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
        set
        {
            // Slider báo giá trị liên tục khi kéo; debounce trong RequestEstimate gom lại thành một lần tính.
            if (SetProperty(ref _customQuality, value)) RequestEstimate();
        }
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
                RequestEstimate();
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
    // Các property dưới đây đều tính từ _estimate; RequestEstimate/RunEstimateAsync cập nhật nó.

    /// <summary>Ví dụ "≈ 4.2 MB". Trong lúc tính lại vẫn giữ số cũ (ProgressRing bên cạnh báo đang tính).</summary>
    public string EstimatedSizeText => FormatEstimate(_estimate);

    /// <summary>Dòng chú thích dưới số ước tính, ví dụ "35% so với ảnh gốc (48.3 MB)".</summary>
    public string CompareText =>
        _itemCount == 0 ? "Chưa có ảnh"
        : !IsValid ? "Sửa tuỳ chọn để xem ước tính"
        : _hasPageRangeErrors ? "Sửa ô chọn trang để xem ước tính"
        : _estimate is null ? "Đang ước tính…"
        : _sourceBytes > 0 ? $"{EstimateRatio:0}% so với file gốc ({FileSizeFormatter.Format(_sourceBytes)})"
        : "";

    /// <summary>Bật ProgressRing nhỏ cạnh số ước tính (cả trong 300 ms chờ debounce).</summary>
    public bool IsEstimating
    {
        get => _isEstimating;
        private set => SetProperty(ref _isEstimating, value);
    }

    public bool HasEstimate => _estimate is not null;

    /// <summary>Tỉ lệ dung lượng ước tính so với tổng dung lượng ảnh gốc, 0–100, cho ProgressBar trong flyout.</summary>
    public double EstimateRatio =>
        _estimate is null || _sourceBytes <= 0 ? 0 : Math.Min(100, 100.0 * _estimate.Bytes / _sourceBytes);

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
    /// <param name="sourceBytes">Tổng dung lượng các file ảnh gốc, để so sánh với dung lượng ước tính.</param>
    public void SetItems(int count, string? firstItemPath, long sourceBytes)
    {
        bool wasEmpty = _itemCount == 0;
        _itemCount = count;
        _sourceBytes = sourceBytes;

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

        // Sắp xếp lại cũng gọi vào đây. Tính lại vẫn rẻ vì bản mẫu và bpp đã được cache.
        RequestEstimate();
    }

    /// <summary>
    /// MainViewModel gọi khi ô chọn trang của một file PDF đổi, hoặc khi danh sách đổi.
    /// Còn ô nào sai thì tắt nút Xuất và không ước tính. Lựa chọn trang đổi thì dung lượng cũng đổi, nên tính lại.
    /// </summary>
    /// <param name="hasErrors">Có ít nhất một file PDF đang nhập sai ô chọn trang.</param>
    public void SetPageRangeErrors(bool hasErrors)
    {
        _hasPageRangeErrors = hasErrors;
        OnPropertyChanged(nameof(CompareText));
        ExportCommand.NotifyCanExecuteChanged();
        RequestEstimate();
    }

    /// <summary>
    /// Xin tính lại dung lượng ước tính. Gọi bao nhiêu lần cũng được: lần gọi mới huỷ lần trước,
    /// và việc tính chỉ bắt đầu sau <see cref="EstimateDebounce"/> không có thay đổi nào nữa (debounce).
    /// </summary>
    private void RequestEstimate()
    {
        // Không Dispose CTS cũ ở đây: lần chạy cũ có thể vẫn đang dùng token của nó.
        // Lần chạy nào tự Dispose CTS của mình trong finally.
        _estimateCts?.Cancel();

        if (_itemCount == 0 || !IsValid || _hasPageRangeErrors)
        {
            _estimateCts = null;
            IsEstimating = false;
            SetEstimate(null);
            foreach (PresetOptionViewModel option in Presets) option.EstimatedSizeText = PresetOptionViewModel.NoEstimateText;
            return;
        }

        var cts = new CancellationTokenSource();
        _estimateCts = cts;
        IsEstimating = true;

        // Không await: setter của property không thể async. RunEstimateAsync tự bắt mọi lỗi.
        _ = RunEstimateAsync(cts);
    }

    /// <summary>
    /// Chờ debounce rồi ước tính: mức nén đang chọn trước (số chính ở thanh đáy), sau đó các preset còn lại cho flyout.
    ///
    /// Hàm này chạy trên UI thread. Việc nặng nằm trong <c>_sizeEstimator.EstimateAsync</c> (Task.Run, luồng nền);
    /// sau mỗi <c>await</c>, SynchronizationContext của WinUI đưa code quay lại UI thread, nên gán property ở đây
    /// là an toàn mà không cần DispatcherQueue.TryEnqueue.
    /// </summary>
    private async Task RunEstimateAsync(CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        try
        {
            // Task.Delay nhận token: bị huỷ trong lúc chờ thì ném OperationCanceledException ngay.
            await Task.Delay(EstimateDebounce, token);

            // Chụp danh sách và tuỳ chọn tại thời điểm bắt đầu tính.
            IReadOnlyList<ImageSourceInfo> sources = _getSources();
            ExportOptions current = ToOptions();

            SizeEstimate currentEstimate = await _sizeEstimator.EstimateAsync(sources, current, token);
            // Có thể lần tính này vừa xong đúng lúc bị huỷ: kiểm tra lại để không ghi đè kết quả của lần mới hơn.
            token.ThrowIfCancellationRequested();
            SetEstimate(currentEstimate);

            for (int i = 0; i < Presets.Count; i++)
            {
                var preset = (CompressionPreset)i;
                SizeEstimate? estimate;
                if (preset == current.Preset)
                {
                    estimate = currentEstimate; // vừa tính xong ở trên
                }
                else if (preset == CompressionPreset.Custom)
                {
                    estimate = null; // Tuỳ chỉnh chỉ có số khi đang chọn
                }
                else
                {
                    estimate = await _sizeEstimator.EstimateAsync(sources, OptionsFor(preset, PresetSettings.For(preset)), token);
                    token.ThrowIfCancellationRequested();
                }

                Presets[i].EstimatedSizeText = estimate is null ? PresetOptionViewModel.NoEstimateText : FormatEstimate(estimate);
            }
        }
        catch (OperationCanceledException)
        {
            // Đã có lần tính mới thay thế, hoặc danh sách trống: không làm gì.
        }
        catch (Exception ex)
        {
            // Ước tính chỉ là thông tin phụ: lỗi bất ngờ không được làm sập app hay chặn việc xuất.
            Debug.WriteLine($"[SizeEstimate] Lỗi: {ex}");
            SetEstimate(null);
        }
        finally
        {
            // Chỉ lần chạy mới nhất mới được tắt ProgressRing.
            if (_estimateCts == cts)
            {
                _estimateCts = null;
                IsEstimating = false;
            }
            cts.Dispose();
        }
    }

    private void SetEstimate(SizeEstimate? estimate)
    {
        _estimate = estimate;
        OnPropertiesChanged(nameof(EstimatedSizeText), nameof(CompareText), nameof(HasEstimate), nameof(EstimateRatio));
    }

    private static string FormatEstimate(SizeEstimate? estimate) =>
        estimate is null ? "≈ — MB" : $"≈ {FileSizeFormatter.Format(estimate.Bytes)}";

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

        // Chỉ so sánh khi số ước tính khớp với tuỳ chọn đang xuất (không đang tính dở).
        SizeEstimate? estimate = IsEstimating ? null : _estimate;

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
            int pageCount = await _pdfExporter.ExportAsync(sources, outputPath, options, progress, _exportCts.Token);

            long size = new FileInfo(outputPath).Length;
            string comparison = estimate is null
                ? ""
                : $"Ước tính {FormatEstimate(estimate)}, thực tế {FileSizeFormatter.Format(size)} " +
                  $"(lệch {PercentError(estimate.Bytes, size):+0;-0;0}%) · ";
            ShowResult(
                InfoBarSeverity.Success,
                $"Đã lưu {Path.GetFileName(outputPath)} · {pageCount} trang · {FileSizeFormatter.Format(size)}",
                comparison + outputPath,
                outputPath);

            if (estimate is not null) LogEstimateAccuracy(options, pageCount, estimate, size);
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
    public ExportOptions ToOptions() => OptionsFor(Preset, CurrentSettings());

    private static ExportOptions OptionsFor(CompressionPreset preset, PresetSettings s) => new()
    {
        Preset = preset,
        Quality = s.PdfQuality,
        MaxWidth = s.MaxWidth,
    };

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

    /// <summary>Sai số của ước tính so với thực tế, tính bằng %. Dương = ước tính cao hơn thực tế.</summary>
    private static double PercentError(long estimated, long actual) =>
        actual > 0 ? 100.0 * (estimated - actual) / actual : 0;

    /// <summary>
    /// Ghi cặp ước tính/thực tế ra cửa sổ Output của Visual Studio (chỉ hiện khi chạy Debug).
    /// Dùng để chọn <c>SampleSizeEstimator.ScaleExponent</c>: xuất khoảng 5 bộ ảnh khác nhau,
    /// lấy giá trị "k gợi ý" ở giữa các lần đo. k gợi ý là số mũ làm ước tính khớp đúng lần xuất này:
    /// thực tế ≈ chưa hiệu chỉnh × scale^(-k), suy ra k = -ln(thực tế / chưa hiệu chỉnh) / ln(scale).
    /// </summary>
    private static void LogEstimateAccuracy(ExportOptions options, int pageCount, SizeEstimate estimate, long actual)
    {
        string suggestedK = estimate.TypicalScale > 1.5
            ? (-Math.Log((double)actual / estimate.UncorrectedBytes) / Math.Log(estimate.TypicalScale)).ToString("0.000")
            : "n/a (ảnh gần bằng bản mẫu)";

        Debug.WriteLine(
            $"[SizeEstimate] q={options.Quality} maxWidth={options.MaxWidth?.ToString() ?? "giữ nguyên"} pages={pageCount} " +
            $"estimate={estimate.Bytes} actual={actual} error={PercentError(estimate.Bytes, actual):+0.0;-0.0}% " +
            $"uncorrected={estimate.UncorrectedBytes} scale={estimate.TypicalScale:0.0} k gợi ý={suggestedK}");
    }

    private static string FallbackFileName() => $"ImageMerger_{DateTime.Now:yyyy-MM-dd}";

    /// <summary>Ví dụ "JPEG 85 · rộng tối đa 2048 px".</summary>
    private static string Describe(PresetSettings s) =>
        s.MaxWidth is int w ? $"JPEG {s.PdfQuality} · rộng tối đa {w} px" : $"JPEG {s.PdfQuality} · giữ nguyên kích thước";
}
