using ImageMerger.Helpers;
using ImageMerger.Models;

namespace ImageMerger.ViewModels;

/// <summary>
/// Các tuỳ chọn trong hộp thoại Xuất PDF. Sống cùng MainViewModel nên lần mở sau vẫn giữ lựa chọn cũ.
///
/// Các control Tuỳ chỉnh (slider quality, chiều rộng tối đa) chỉ hiện khi <see cref="IsCustom"/>,
/// để View không phải tự suy luận.
/// </summary>
public sealed class ExportViewModel : BindableBase
{
    public const int MinMaxWidth = 64;
    public const int MaxMaxWidth = 20000;

    public const string FileExtension = ".pdf";

    private CompressionPreset _preset = CompressionPreset.Balanced;
    private double _customQuality = 85;
    private double _customMaxWidth = 2048;

    // --- Mức nén ---

    public CompressionPreset Preset
    {
        get => _preset;
        set
        {
            if (SetProperty(ref _preset, value))
            {
                OnPropertiesChanged(nameof(PresetIndex), nameof(IsCustom), nameof(PresetSummary),
                    nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid));
            }
        }
    }

    /// <summary>ComboBox dùng SelectedIndex (số nguyên), thứ tự item khớp thứ tự enum.</summary>
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

    // --- Giá trị Tuỳ chỉnh ---

    /// <summary>Slider.Value là double, nên property cũng là double.</summary>
    public double CustomQuality
    {
        get => _customQuality;
        set
        {
            if (SetProperty(ref _customQuality, value)) OnPropertyChanged(nameof(PresetSummary));
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
                OnPropertiesChanged(nameof(PresetSummary), nameof(ValidationMessage), nameof(HasValidationError), nameof(IsValid));
            }
        }
    }

    // --- Tóm tắt và kiểm tra hợp lệ ---

    /// <summary>Mô tả ngắn giá trị thật sự sẽ dùng, ví dụ "JPEG quality 85 · rộng tối đa 2048 px".</summary>
    public string PresetSummary
    {
        get
        {
            PresetSettings s = CurrentSettings();
            string width = s.MaxWidth is int w ? $"rộng tối đa {w} px" : "giữ nguyên kích thước";
            return $"JPEG quality {s.PdfQuality} · {width}";
        }
    }

    /// <summary>Chuỗi rỗng nghĩa là hợp lệ.</summary>
    public string ValidationMessage =>
        IsCustom && !double.IsNaN(CustomMaxWidth) && CustomMaxWidth is < MinMaxWidth or > MaxMaxWidth
            ? $"Chiều rộng tối đa phải từ {MinMaxWidth} đến {MaxMaxWidth} px (để trống nếu giữ nguyên)."
            : "";

    public bool HasValidationError => ValidationMessage.Length > 0;

    public bool IsValid => !HasValidationError;

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
}
