namespace ImageMerger.Models;

/// <summary>
/// Mô tả một ảnh nguồn sẽ được gộp. Chỉ chứa thông tin, không giữ dữ liệu pixel.
/// </summary>
/// <param name="Path">Đường dẫn đầy đủ tới file.</param>
/// <param name="PageIndex">Chỉ số trang nếu nguồn là PDF (Phase 7). Ảnh thường là <c>null</c>.</param>
/// <param name="PixelWidth">Chiều rộng sau khi xoay theo EXIF.</param>
/// <param name="PixelHeight">Chiều cao sau khi xoay theo EXIF.</param>
public sealed record ImageSourceInfo(string Path, int? PageIndex, int PixelWidth, int PixelHeight);
