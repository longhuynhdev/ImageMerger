using ImageMagick;

namespace ImageMerger.Services;

/// <summary>Cấu hình Magick.NET một lần khi app khởi động.</summary>
public static class MagickSetup
{
    public static void Initialize()
    {
        // Giới hạn RAM ImageMagick được dùng cho pixel. Khi ảnh ghép vượt quá mức này,
        // ImageMagick tự chuyển sang lưu tạm trên ổ đĩa (chậm hơn) thay vì làm tràn RAM.
        // Ví dụ: ghép dọc 20 ảnh 4000×3000 cần khoảng 1 GB.
        ResourceLimits.LimitMemory(new Percentage(25));
    }
}
