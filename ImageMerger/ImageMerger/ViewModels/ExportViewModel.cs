using System.Diagnostics;
using ImageMerger.Helpers;
using ImageMerger.Models;
using ImageMerger.Services;
using ImageMerger.Services.Compression;
using ImageMerger.Services.Export;

namespace ImageMerger.ViewModels;

/// <summary>
/// Bước 3 "Lưu thành file PDF": tên file, kích thước file (mức nén), dung lượng ước tính,
/// nút Lưu, tiến độ và thẻ kết quả.
/// Sống cùng MainViewModel (property <c>Export</c>) nên lựa chọn được giữ suốt phiên làm việc.
///
/// Người dùng chỉ thấy 3 lựa chọn (Nhỏ gọn / Vừa phải / Chất lượng cao nhất). Mức "Tuỳ chỉnh" không có nút riêng:
/// chỉnh slider hoặc ô chiều rộng trong "Tuỳ chỉnh nâng cao" là tự chuyển sang tuỳ chỉnh,
/// bấm lại một trong 3 lựa chọn là quay về mức có sẵn.
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
    private bool _isResultSuccess;
    private string _resultTitle = "";
    private string _resultMessage = "";
    private string? _resultPath;

    /// <summary>Huỷ lần xuất đang chạy. Chỉ khác null trong lúc đang xuất.</summary>
    private CancellationTokenSource? _exportCts;

    /// <param name="getSources">Chụp danh sách ảnh hiện tại (theo đúng thứ tự trên màn hình) lúc bấm Lưu.</param>
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

        // Thứ tự hiển thị: nhỏ nhất trước. Thứ tự này KHÁC thứ tự enum, nên SelectedPresetIndex tra theo Preset.
        Presets = (PresetOptionViewModel[])
        [
            new PresetOptionViewModel(CompressionPreset.Compact, "Nhỏ gọn", "Hợp để gửi qua Zalo, email"),
            new PresetOptionViewModel(CompressionPreset.Balanced, "Vừa phải", "Ảnh rõ nét, file không quá nặng", isRecommended: true),
            new PresetOptionViewModel(CompressionPreset.Original, "Chất lượng cao nhất", "Giữ nguyên độ nét, file rất nặng"),
        ];

        ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanSave && !IsExporting);
        CancelCommand = new RelayCommand(() => _exportCts?.Cancel(), () => IsExporting);
        OpenResultCommand = new RelayCommand(OpenResult, () => _resultPath is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => _resultPath is not null);
        DismissResultCommand = new RelayCommand(DismissResult);
    }

    // --- Kích thước file (mức nén) ---

    /// <summary>3 lựa chọn của mục "Kích thước file".</summary>
    public IReadOnlyList<PresetOptionViewModel> Presets { get; }

    public CompressionPreset Preset
    {
        get => _preset;
        set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertiesChanged(nameof(SelectedPresetIndex), nameof(IsCustom), nameof(CustomSummaryText),
                    nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid));
                NotifySaveStateChanged();
                RequestEstimate();
            }
        }
    }

    /// <summary>
    /// Chỉ số trong <see cref="Presets"/> cho RadioButtons.SelectedIndex.
    /// Đang dùng tuỳ chỉnh thì trả -1: không nút nào được chọn, người dùng thấy rõ là đang không dùng mức có sẵn.
    /// </summary>
    public int SelectedPresetIndex
    {
        get
        {
            for (int i = 0; i < Presets.Count; i++)
            {
                if (Presets[i].Preset == Preset) return i;
            }
            return -1;
        }
        set
        {
            // RadioButtons gửi -1 khi ta bỏ chọn tất cả (lúc chuyển sang tuỳ chỉnh): bỏ qua.
            if (value < 0 || value >= Presets.Count) return;
            CompressionPreset preset = Presets[value].Preset;
            if (preset == Preset) return;

            // Đưa giá trị của mức vừa chọn vào các ô tuỳ chỉnh, để khi mở "Tuỳ chỉnh nâng cao"
            // người dùng thấy đúng con số đang dùng và chỉnh tiếp từ đó.
            ShowInCustomControls(PresetSettings.For(preset));
            Preset = preset;
        }
    }

    public bool IsCustom => Preset == CompressionPreset.Custom;

    /// <summary>Dòng giải thích khi đang dùng tuỳ chỉnh (không nút nào trong 3 lựa chọn được chọn).</summary>
    public string CustomSummaryText
    {
        get
        {
            if (!IsCustom) return "";
            PresetSettings s = CurrentSettings();
            string width = s.MaxWidth is int w ? $"rộng tối đa {w} điểm ảnh" : "giữ nguyên kích thước";
            string size = HasEstimate ? $", {EstimatedSizeText}" : "";
            return $"Đang dùng tuỳ chỉnh: chất lượng {s.PdfQuality}, {width}{size}. Bấm một lựa chọn ở trên để quay lại.";
        }
    }

    // --- Giá trị Tuỳ chỉnh ---

    /// <summary>
    /// Slider.Value là double, nên property cũng là double.
    /// Người dùng kéo slider nghĩa là muốn tuỳ chỉnh, nên chuyển sang <see cref="CompressionPreset.Custom"/>.
    /// </summary>
    public double CustomQuality
    {
        get => _customQuality;
        set
        {
            // Slider báo giá trị liên tục khi kéo; debounce trong RequestEstimate gom lại thành một lần tính.
            if (SetProperty(ref _customQuality, value)) OnCustomValueChanged();
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
            if (SetProperty(ref _customMaxWidth, value)) OnCustomValueChanged();
        }
    }

    /// <summary>
    /// Người dùng vừa đổi một giá trị tuỳ chỉnh. Gán Preset = Custom sẽ báo SelectedPresetIndex = -1 (bỏ chọn 3 nút)
    /// và tự tính lại ước tính; nếu đã ở Custom rồi thì chỉ cần tính lại.
    /// </summary>
    private void OnCustomValueChanged()
    {
        if (!IsCustom)
        {
            Preset = CompressionPreset.Custom;
            return;
        }

        OnPropertiesChanged(nameof(CustomSummaryText), nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid));
        NotifySaveStateChanged();
        RequestEstimate();
    }

    /// <summary>
    /// Gán thẳng vào field (không qua setter) rồi báo cho UI: nếu đi qua setter thì
    /// <see cref="OnCustomValueChanged"/> sẽ tưởng người dùng vừa chỉnh và chuyển sang Tuỳ chỉnh.
    /// Slider/NumberBox nhận giá trị mới rồi ghi ngược lại đúng giá trị đó, SetProperty thấy không đổi nên bỏ qua.
    /// </summary>
    private void ShowInCustomControls(PresetSettings settings)
    {
        _customQuality = settings.PdfQuality;
        _customMaxWidth = settings.MaxWidth ?? double.NaN;
        OnPropertiesChanged(nameof(CustomQuality), nameof(CustomMaxWidth));
    }

    // --- Kiểm tra hợp lệ ---

    /// <summary>Chuỗi rỗng nghĩa là hợp lệ.</summary>
    public string ValidationMessage =>
        IsCustom && !double.IsNaN(CustomMaxWidth) && CustomMaxWidth is < MinMaxWidth or > MaxMaxWidth
            ? $"Chiều rộng tối đa phải từ {MinMaxWidth} đến {MaxMaxWidth} điểm ảnh (để trống nếu muốn giữ nguyên)."
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
                NotifySaveStateChanged();
            }
        }
    }

    /// <summary>Tên gợi ý (PlaceholderText): tên ảnh đầu tiên, hoặc "ImageMerger_yyyy-MM-dd".</summary>
    public string DefaultFileName
    {
        get => _defaultFileName;
        private set
        {
            if (SetProperty(ref _defaultFileName, value)) OnPropertyChanged(nameof(FileNameHelpText));
        }
    }

    /// <summary>Dòng hướng dẫn dưới ô tên file.</summary>
    public string FileNameHelpText => $"Để trống thì dùng tên “{DefaultFileName}”.";

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

    /// <summary>Ví dụ "khoảng 4,2 MB". Trong lúc tính lại vẫn giữ số cũ (ProgressRing bên cạnh báo đang tính).</summary>
    public string EstimatedSizeText => FormatEstimate(_estimate);

    /// <summary>Bật ProgressRing nhỏ cạnh tiêu đề "Kích thước file" (cả trong 300 ms chờ debounce).</summary>
    public bool IsEstimating
    {
        get => _isEstimating;
        private set => SetProperty(ref _isEstimating, value);
    }

    public bool HasEstimate => _estimate is not null;

    // --- Nút Lưu ---

    /// <summary>Đủ điều kiện để lưu (chưa tính việc đang lưu dở).</summary>
    public bool CanSave => _itemCount > 0 && IsValid && !_hasPageRangeErrors && !HasFileNameError;

    /// <summary>Ví dụ "Lưu file PDF (khoảng 4,2 MB)". Chưa có ước tính thì chỉ "Lưu file PDF".</summary>
    public string SaveButtonText => HasEstimate ? $"Lưu file PDF ({EstimatedSizeText})" : "Lưu file PDF";

    /// <summary>Dòng chữ dưới nút Lưu: nói rõ bấm xong sẽ thế nào, hoặc vì sao nút đang mờ.</summary>
    public string SaveHintText =>
        _itemCount == 0 ? "Hãy thêm ảnh hoặc file PDF ở bước 1 trước."
        : HasFileNameError ? "Hãy sửa tên file ở trên trước."
        : _hasPageRangeErrors ? "Hãy sửa ô chọn trang ở bước 2 trước."
        : !IsValid ? "Hãy sửa chiều rộng trong “Tuỳ chỉnh nâng cao” trước."
        : "Sau khi bấm, bạn chọn thư mục để lưu file.";

    /// <summary>Mọi thứ ảnh hưởng tới việc lưu được hay không đều gọi hàm này: báo lại chữ gợi ý và CanExecute.</summary>
    private void NotifySaveStateChanged()
    {
        OnPropertiesChanged(nameof(CanSave), nameof(SaveHintText));
        ExportCommand.NotifyCanExecuteChanged();
    }

    // --- Đang lưu ---

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

    /// <summary>x:Bind không có toán tử "!", nên cần property ngược để ẩn nút Lưu khi đang lưu.</summary>
    public bool IsNotExporting => !IsExporting;

    /// <summary>Tiến độ lưu, 0–100, bind vào ProgressBar.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    /// <summary>Ví dụ "Đang lưu trang 3 trên 7…".</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    // --- Thẻ kết quả ---

    /// <summary>Hiện thẻ kết quả (xanh khi xong, thẻ thông báo khi huỷ hoặc lỗi).</summary>
    public bool HasResult
    {
        get => _hasResult;
        private set
        {
            if (SetProperty(ref _hasResult, value)) OnPropertiesChanged(nameof(IsResultSuccess), nameof(IsResultProblem));
        }
    }

    /// <summary>Lưu thành công: thẻ xanh với các nút Mở file / Mở thư mục / Làm file mới.</summary>
    public bool IsResultSuccess
    {
        get => _isResultSuccess && _hasResult;
        private set
        {
            if (SetProperty(ref _isResultSuccess, value)) OnPropertyChanged(nameof(IsResultProblem));
        }
    }

    /// <summary>Huỷ hoặc lỗi: thẻ thông báo với nút "Đã hiểu".</summary>
    public bool IsResultProblem => _hasResult && !_isResultSuccess;

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

    // --- Command ---

    /// <summary>Phím tắt Ctrl+E và nút "Lưu file PDF" đều gọi command này.</summary>
    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>Nút "Huỷ, không lưu nữa": huỷ <see cref="_exportCts"/> của lần lưu đang chạy.</summary>
    public RelayCommand CancelCommand { get; }

    public RelayCommand OpenResultCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    /// <summary>Nút "Đã hiểu" trên thẻ huỷ/lỗi.</summary>
    public RelayCommand DismissResultCommand { get; }

    /// <summary>
    /// MainViewModel gọi mỗi khi danh sách ảnh đổi: bật/tắt nút Lưu và cập nhật tên file mặc định.
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
        NotifySaveStateChanged();

        // Sắp xếp lại cũng gọi vào đây. Tính lại vẫn rẻ vì bản mẫu và bpp đã được cache.
        RequestEstimate();
    }

    /// <summary>
    /// MainViewModel gọi khi ô chọn trang của một file PDF đổi, hoặc khi danh sách đổi.
    /// Còn ô nào sai thì tắt nút Lưu và không ước tính. Lựa chọn trang đổi thì dung lượng cũng đổi, nên tính lại.
    /// </summary>
    /// <param name="hasErrors">Có ít nhất một file PDF đang nhập sai ô chọn trang.</param>
    public void SetPageRangeErrors(bool hasErrors)
    {
        _hasPageRangeErrors = hasErrors;
        NotifySaveStateChanged();
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
    /// Chờ debounce rồi ước tính: mức nén đang dùng trước (số trên nút Lưu), sau đó 3 lựa chọn còn lại.
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

            foreach (PresetOptionViewModel option in Presets)
            {
                SizeEstimate estimate = option.Preset == current.Preset
                    ? currentEstimate // vừa tính xong ở trên
                    : await _sizeEstimator.EstimateAsync(sources, OptionsFor(option.Preset, PresetSettings.For(option.Preset)), token);
                token.ThrowIfCancellationRequested();
                option.EstimatedSizeText = FormatEstimate(estimate);
            }
        }
        catch (OperationCanceledException)
        {
            // Đã có lần tính mới thay thế, hoặc danh sách trống: không làm gì.
        }
        catch (Exception ex)
        {
            // Ước tính chỉ là thông tin phụ: lỗi bất ngờ không được làm sập app hay chặn việc lưu.
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
        OnPropertiesChanged(nameof(EstimatedSizeText), nameof(HasEstimate), nameof(SaveButtonText), nameof(CustomSummaryText));
    }

    private static string FormatEstimate(SizeEstimate? estimate) =>
        estimate is null ? PresetOptionViewModel.NoEstimateText : $"khoảng {FileSizeFormatter.Format(estimate.Bytes)}";

    /// <summary>
    /// Mở FileSavePicker (người dùng chọn thư mục) rồi lưu. Mỗi lần lưu tạo một <see cref="CancellationTokenSource"/> mới;
    /// <see cref="CancelCommand"/> gọi <c>Cancel()</c> trên nó, và exporter dừng ở lần kiểm tra token kế tiếp.
    /// </summary>
    private async Task ExportAsync()
    {
        ExportOptions options = ToOptions();
        string? outputPath = await _filePickerService.PickSaveFileAsync(
            ResolvedFileName, "Tệp PDF", FileExtension);
        if (outputPath is null) return;

        // Chụp lại danh sách tại thời điểm bấm Lưu.
        IReadOnlyList<ImageSourceInfo> sources = _getSources();

        // Chỉ so sánh khi số ước tính khớp với tuỳ chọn đang lưu (không đang tính dở).
        SizeEstimate? estimate = IsEstimating ? null : _estimate;

        // Exporter báo tiến độ theo nguồn (một ảnh hoặc cả một file PDF); đổi sang số trang để người dùng dễ hiểu.
        int[] pagesBefore = CountPagesBefore(sources);
        int totalPages = Math.Max(1, pagesBefore[^1]);

        // Progress<T> tạo trên UI thread sẽ gọi callback trên UI thread,
        // nên exporter báo tiến độ từ luồng nền vẫn cập nhật property an toàn.
        var progress = new Progress<double>(value =>
        {
            Progress = value * 100;
            int doneSources = Math.Clamp((int)Math.Round(value * sources.Count), 0, sources.Count);
            ProgressText = $"Đang lưu trang {Math.Min(pagesBefore[doneSources] + 1, totalPages)} trên {totalPages}…";
        });

        _exportCts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = $"Đang lưu trang 1 trên {totalPages}…";
        HasResult = false;
        IsExporting = true;
        try
        {
            int pageCount = await _pdfExporter.ExportAsync(sources, outputPath, options, progress, _exportCts.Token);

            long size = new FileInfo(outputPath).Length;
            ShowResult(
                success: true,
                "Đã lưu xong",
                $"File {Path.GetFileName(outputPath)}, {pageCount} trang, {FileSizeFormatter.Format(size)}.\n" +
                $"Nằm trong thư mục: {Path.GetDirectoryName(outputPath)}",
                outputPath);

            if (estimate is not null) LogEstimateAccuracy(options, pageCount, estimate, size);
        }
        catch (OperationCanceledException)
        {
            ShowResult(success: false, "Chưa lưu file",
                "Bạn đã bấm huỷ nên chưa có file nào được lưu. Bấm “Lưu file PDF” để thử lại.", null);
        }
        catch (Exception ex)
        {
            // Ranh giới với UI: mọi lỗi (file hỏng, ổ đầy, không có quyền ghi...) đều báo cho người dùng.
            ShowResult(success: false, "Không lưu được file",
                $"Có lỗi xảy ra: {ex.Message}\nHãy thử lưu vào thư mục khác, hoặc kiểm tra các file ảnh và PDF còn mở được không.",
                null);
        }
        finally
        {
            _exportCts.Dispose();
            _exportCts = null;
            IsExporting = false;
        }
    }

    private void ShowResult(bool success, string title, string message, string? path)
    {
        IsResultSuccess = success;
        ResultTitle = title;
        ResultMessage = message;
        _resultPath = path;
        OpenResultCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
        HasResult = true;
    }

    /// <summary>Ẩn thẻ kết quả. Nút "Đã hiểu" và "Làm file mới" (MainViewModel) gọi hàm này.</summary>
    public void DismissResult()
    {
        HasResult = false;
        _resultPath = null;
        OpenResultCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
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
            ShowResult(success: false, "Không mở được file", ex.Message, null);
        }
    }

    /// <summary>Mở File Explorer tại thư mục chứa file và tô sáng file vừa lưu (tham số "/select,").</summary>
    private void OpenFolder()
    {
        if (_resultPath is null) return;
        try
        {
            Process.Start("explorer.exe", $"/select,\"{_resultPath}\"");
        }
        catch (Exception ex)
        {
            ShowResult(success: false, "Không mở được thư mục", ex.Message, null);
        }
    }

    /// <summary>
    /// pagesBefore[i] = số trang của các nguồn đứng trước nguồn i; phần tử cuối là tổng số trang.
    /// Nguồn "cả file PDF" (PageIndex null) chiếm nhiều trang; chưa đọc xong PDF thì tạm tính 1.
    /// </summary>
    private static int[] CountPagesBefore(IReadOnlyList<ImageSourceInfo> sources)
    {
        var pagesBefore = new int[sources.Count + 1];
        for (int i = 0; i < sources.Count; i++)
        {
            ImageSourceInfo s = sources[i];
            int pages = s.PageIndex is null && SupportedFormats.IsPdf(s.Path) ? s.PdfPageCount ?? 1 : 1;
            pagesBefore[i + 1] = pagesBefore[i] + pages;
        }
        return pagesBefore;
    }

    /// <summary>Gom mọi lựa chọn thành ExportOptions cụ thể cho exporter.</summary>
    public ExportOptions ToOptions() => OptionsFor(Preset, CurrentSettings());

    private static ExportOptions OptionsFor(CompressionPreset preset, PresetSettings s) => new()
    {
        Preset = preset,
        Quality = s.PdfQuality,
        MaxWidth = s.MaxWidth,
    };

    /// <summary>Giá trị của mức đang dùng; với Tuỳ chỉnh thì lấy từ các control.</summary>
    private PresetSettings CurrentSettings()
    {
        if (!IsCustom) return PresetSettings.For(Preset);

        int quality = (int)Math.Round(CustomQuality);
        int? maxWidth = double.IsNaN(CustomMaxWidth) ? null : (int)CustomMaxWidth;
        return new PresetSettings(quality, maxWidth);
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
}
