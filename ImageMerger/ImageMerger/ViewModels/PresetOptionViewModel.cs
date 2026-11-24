using ImageMerger.Helpers;
using ImageMerger.Models;

namespace ImageMerger.ViewModels;

/// <summary>
/// Một lựa chọn trong mục "Kích thước file": tên dễ hiểu, mô tả ngắn và dung lượng ước tính.
/// </summary>
public sealed class PresetOptionViewModel : BindableBase
{
    /// <summary>Hiển thị khi chưa có số ước tính.</summary>
    public const string NoEstimateText = "—";

    private string _estimatedSizeText = NoEstimateText;

    public PresetOptionViewModel(CompressionPreset preset, string name, string description, bool isRecommended = false)
    {
        Preset = preset;
        Name = name;
        Description = description;
        IsRecommended = isRecommended;
    }

    /// <summary>Mức nén thật sự dùng khi chọn lựa chọn này.</summary>
    public CompressionPreset Preset { get; }

    public string Name { get; }

    public string Description { get; }

    /// <summary>Hiện nhãn nhỏ "Nên chọn" cạnh tên.</summary>
    public bool IsRecommended { get; }

    /// <summary>Ví dụ "khoảng 4,2 MB". ExportViewModel gán sau mỗi lần ước tính.</summary>
    public string EstimatedSizeText
    {
        get => _estimatedSizeText;
        set => SetProperty(ref _estimatedSizeText, value);
    }
}
