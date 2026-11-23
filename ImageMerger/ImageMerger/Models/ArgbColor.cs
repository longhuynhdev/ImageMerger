namespace ImageMerger.Models;

/// <summary>
/// Màu ARGB đơn giản. Dùng thay cho Windows.UI.Color để Models/Services không phụ thuộc WinUI.
/// </summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static ArgbColor White { get; } = new(255, 255, 255, 255);
}
