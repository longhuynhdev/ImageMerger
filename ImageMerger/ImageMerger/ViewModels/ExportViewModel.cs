using CommunityToolkit.Mvvm.ComponentModel;
using ImageMerger.Models;
using ImageMerger.Services.Export;
using Windows.UI;

namespace ImageMerger.ViewModels;

/// <summary>
/// Các tuỳ chọn trong hộp thoại Xuất. Sống cùng MainViewModel nên lần mở sau vẫn giữ lựa chọn cũ.
///
/// Các property "ShowXxx" quyết định control nào hiện ra, để View không phải tự suy luận:
/// mỗi control chỉ hiện khi nó có ý nghĩa với định dạng và preset đang chọn.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject
{
    public const int MinMaxWidth = 64;
    public const int MaxMaxWidth = 20000;
    public const int MaxSpacing = 500;

    // --- Định dạng ---

    /// <summary>
    /// [NotifyPropertyChangedFor]: khi Format đổi, báo luôn cho các property phụ thuộc,
    /// để x:Bind cập nhật Visibility của các control liên quan.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatIndex), nameof(IsMergedFormat), nameof(IsPng), nameof(IsWebp),
        nameof(ShowQualitySlider), nameof(ShowReduceColors), nameof(PresetSummary), nameof(ValidationMessage),
        nameof(HasValidationError), nameof(IsValid), nameof(FileExtension))]
    public partial ExportFormat Format { get; set; } = ExportFormat.Pdf;

    /// <summary>
    /// RadioButtons làm việc với SelectedIndex (số nguyên), còn ViewModel dùng enum.
    /// Property này "dịch" qua lại; thứ tự các RadioButton trong XAML phải khớp thứ tự enum.
    /// </summary>
    public int FormatIndex
    {
        get => (int)Format;
        set
        {
            if (value >= 0) Format = (ExportFormat)value;
        }
    }

    /// <summary>PNG/WebP ghép thành một ảnh dài, nên có khoảng cách và màu nền. PDF thì không.</summary>
    public bool IsMergedFormat => Format != ExportFormat.Pdf;

    public bool IsPng => Format == ExportFormat.Png;

    public bool IsWebp => Format == ExportFormat.Webp;

    public string FileExtension => Format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Webp => ".webp",
        _ => ".pdf",
    };

    // --- Mức nén ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetIndex), nameof(IsCustom), nameof(ShowQualitySlider), nameof(ShowReduceColors),
        nameof(PresetSummary), nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid))]
    public partial CompressionPreset Preset { get; set; } = CompressionPreset.Balanced;

    /// <summary>Giống FormatIndex: ComboBox dùng SelectedIndex, thứ tự item khớp thứ tự enum.</summary>
    public int PresetIndex
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

    /// <summary>Slider quality chỉ có nghĩa với định dạng có nén mất dữ liệu (PDF/WebP).</summary>
    public bool ShowQualitySlider => IsCustom && !IsPng;

    /// <summary>PNG không có quality; thay vào đó là tuỳ chọn giảm màu.</summary>
    public bool ShowReduceColors => IsCustom && IsPng;

    // --- Giá trị Tuỳ chỉnh ---

    /// <summary>Slider.Value là double, nên property cũng là double.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetSummary))]
    public partial double CustomQuality { get; set; } = 80;

    /// <summary>NumberBox.Value là double; ô trống cho ra NaN, nghĩa là "giữ nguyên chiều rộng".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetSummary), nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid))]
    public partial double CustomMaxWidth { get; set; } = 2048;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetSummary))]
    public partial bool CustomReduceColors { get; set; }

    // --- Ghép ảnh (PNG/WebP) ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid))]
    public partial double Spacing { get; set; } = 0;

    /// <summary>Windows.UI.Color để bind thẳng vào ColorPicker. Đổi sang ArgbColor khi tạo ExportOptions.</summary>
    [ObservableProperty]
    public partial Color BackgroundColor { get; set; } = Color.FromArgb(255, 255, 255, 255);

    // --- Tóm tắt và kiểm tra hợp lệ ---

    /// <summary>Mô tả ngắn giá trị thật sự sẽ dùng, ví dụ "WebP quality 80 · rộng tối đa 2048 px".</summary>
    public string PresetSummary
    {
        get
        {
            PresetSettings s = CurrentSettings();
            string compression = Format switch
            {
                ExportFormat.Pdf => $"JPEG quality {s.PdfQuality}",
                ExportFormat.Webp => s.WebpLossless ? "WebP lossless" : $"WebP quality {s.WebpQuality}",
                _ => s.PngReduceColors ? "PNG-8 (256 màu)" : "PNG đầy đủ màu",
            };
            string width = s.MaxWidth is int w ? $"rộng tối đa {w} px" : "giữ nguyên kích thước";
            return $"{compression} · {width}";
        }
    }

    /// <summary>Chuỗi rỗng nghĩa là hợp lệ.</summary>
    public string ValidationMessage
    {
        get
        {
            if (IsCustom && !double.IsNaN(CustomMaxWidth) && CustomMaxWidth is < MinMaxWidth or > MaxMaxWidth)
            {
                return $"Chiều rộng tối đa phải từ {MinMaxWidth} đến {MaxMaxWidth} px (để trống nếu giữ nguyên).";
            }
            if (IsWebp && IsCustom && !double.IsNaN(CustomMaxWidth) && CustomMaxWidth > WebpExporter.MaxDimension)
            {
                return $"WebP chỉ hỗ trợ tối đa {WebpExporter.MaxDimension} px mỗi chiều. Hãy giảm chiều rộng hoặc chọn PNG.";
            }
            if (IsMergedFormat && (double.IsNaN(Spacing) || Spacing is < 0 or > MaxSpacing))
            {
                return $"Khoảng cách phải từ 0 đến {MaxSpacing} px.";
            }
            return "";
        }
    }

    public bool HasValidationError => ValidationMessage.Length > 0;

    public bool IsValid => !HasValidationError;

    /// <summary>Gom mọi lựa chọn thành ExportOptions cụ thể cho exporter.</summary>
    public ExportOptions ToOptions()
    {
        PresetSettings s = CurrentSettings();
        return new ExportOptions
        {
            Format = Format,
            Preset = Preset,
            Quality = Format == ExportFormat.Webp ? s.WebpQuality : s.PdfQuality,
            Lossless = Format == ExportFormat.Webp && s.WebpLossless,
            ReduceColors = Format == ExportFormat.Png && s.PngReduceColors,
            MaxWidth = s.MaxWidth,
            Spacing = IsMergedFormat ? (int)Spacing : 0,
            Background = new ArgbColor(BackgroundColor.A, BackgroundColor.R, BackgroundColor.G, BackgroundColor.B),
        };
    }

    /// <summary>Giá trị của preset đang chọn; với Tuỳ chỉnh thì lấy từ các control.</summary>
    private PresetSettings CurrentSettings()
    {
        if (!IsCustom) return PresetSettings.For(Preset);

        int quality = (int)Math.Round(CustomQuality);
        int? maxWidth = double.IsNaN(CustomMaxWidth) ? null : (int)CustomMaxWidth;
        return new PresetSettings(quality, WebpLossless: false, quality, CustomReduceColors, maxWidth);
    }

    private void CopyToCustom(PresetSettings settings)
    {
        CustomQuality = Format == ExportFormat.Webp ? settings.WebpQuality : settings.PdfQuality;
        CustomMaxWidth = settings.MaxWidth ?? double.NaN;
        CustomReduceColors = settings.PngReduceColors;
    }
}
