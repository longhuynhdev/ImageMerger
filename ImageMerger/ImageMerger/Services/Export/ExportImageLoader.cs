using ImageMagick;
using ImageMerger.Models;

namespace ImageMerger.Services.Export;

/// <summary>Đọc ảnh gốc cho các exporter. Người gọi chịu trách nhiệm Dispose ảnh trả về.</summary>
internal static class ExportImageLoader
{
    /// <summary>
    /// Đọc ảnh, xoay theo EXIF, rồi thu nhỏ nếu rộng hơn <paramref name="maxWidth"/> (không bao giờ phóng to).
    /// Lỗi đọc file được đổi thành message có tên file để người dùng biết ảnh nào hỏng.
    /// </summary>
    public static MagickImage Load(ImageSourceInfo source, int? maxWidth)
    {
        MagickImage? image = null;
        try
        {
            image = new MagickImage(source.Path);
            image.AutoOrient();

            if (maxWidth is int limit && image.Width > limit)
            {
                // Chỉ cho chiều rộng, chiều cao 0 = tự tính theo tỉ lệ.
                image.Resize((uint)limit, 0);
            }
            return image;
        }
        catch (MagickException ex)
        {
            image?.Dispose();
            throw new InvalidDataException($"Không đọc được ảnh \"{Path.GetFileName(source.Path)}\".", ex);
        }
    }
}
