using ImageMerger.Helpers;

namespace ImageMerger.ViewModels;

/// <summary>
/// Một dòng trong flyout "dung lượng theo từng mức nén": tên preset, mô tả ngắn và dung lượng ước tính.
/// </summary>
public sealed class PresetOptionViewModel : BindableBase
{
    /// <summary>Hiển thị khi chưa có số ước tính.</summary>
    public const string NoEstimateText = "—";

    private string _estimatedSizeText = NoEstimateText;

    public PresetOptionViewModel(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>Ví dụ "≈ 4,2 MB". Bộ ước tính (Phase 6) sẽ gán giá trị này.</summary>
    public string EstimatedSizeText
    {
        get => _estimatedSizeText;
        set => SetProperty(ref _estimatedSizeText, value);
    }
}
