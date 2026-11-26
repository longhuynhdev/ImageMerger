namespace ImageMerger.Models;

/// <summary>Mức nén người dùng chọn. Bảng giá trị: CLAUDE.md mục 3.1.</summary>
public enum CompressionPreset
{
    Original,
    Balanced,
    Compact,
    Custom,
}
