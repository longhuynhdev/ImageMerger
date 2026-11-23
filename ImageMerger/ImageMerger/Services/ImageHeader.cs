using ImageMagick;

namespace ImageMerger.Services;

/// <summary>Đọc thông tin ảnh từ header, không decode pixel nên rất nhanh.</summary>
public static class ImageHeader
{
    /// <summary>
    /// Kích thước ảnh sau khi xoay theo EXIF. Ảnh điện thoại chụp dọc thường được lưu
    /// nằm ngang kèm cờ "xoay 90°", nên phải đổi chỗ rộng/cao.
    /// </summary>
    public static (int Width, int Height) ReadOrientedSize(string path)
    {
        var info = new MagickImageInfo(path);
        bool isRotated = info.Orientation is Orientation.LeftTop or Orientation.RightTop
                                          or Orientation.RightBottom or Orientation.LeftBottom;
        return isRotated
            ? ((int)info.Height, (int)info.Width)
            : ((int)info.Width, (int)info.Height);
    }
}
