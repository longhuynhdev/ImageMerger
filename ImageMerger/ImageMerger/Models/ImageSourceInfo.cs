namespace ImageMerger.Models;

/// <summary>
/// Mô tả một ảnh nguồn sẽ được gộp. Chỉ chứa thông tin, không giữ dữ liệu pixel.
/// </summary>
/// <param name="Path">Đường dẫn đầy đủ tới file.</param>
/// <param name="PageIndex">
/// Chỉ số trang (bắt đầu từ 0) nếu nguồn là PDF. Ảnh thường là <c>null</c>.
/// Với file PDF, <c>null</c> nghĩa là <b>cả file</b> (Phase 7, Bước 1: mỗi file PDF là một item).
/// </param>
/// <param name="PixelWidth">Chiều rộng sau khi xoay theo EXIF.</param>
/// <param name="PixelHeight">Chiều cao sau khi xoay theo EXIF.</param>
public sealed record ImageSourceInfo(string Path, int? PageIndex, int PixelWidth, int PixelHeight);
