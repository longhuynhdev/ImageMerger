namespace ImageMerger.Services.Export;

/// <summary>
/// Lỗi phát hiện trước khi xuất (ví dụ ảnh ghép vượt giới hạn WebP).
/// Message viết sẵn cho người dùng đọc, kèm gợi ý cách sửa.
/// </summary>
public sealed class ExportValidationException(string message) : Exception(message);
