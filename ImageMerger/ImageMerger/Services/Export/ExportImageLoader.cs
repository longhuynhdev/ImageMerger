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

    /// <summary>
    /// Chuẩn hoá ảnh trước khi nén JPEG. Dùng chung cho PdfExporter và bộ ước tính dung lượng,
    /// để bản mẫu ước tính được xử lý giống hệt ảnh thật khi xuất.
    /// </summary>
    public static void PrepareForJpeg(MagickImage image)
    {
        // JPEG không có kênh trong suốt: phủ nền trắng thay vì để vùng trong suốt thành đen.
        if (image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }

        // PDF ở đây chỉ khai báo RGB hoặc Gray, nên đưa ảnh CMYK... về sRGB.
        if (image.ColorSpace is not ColorSpace.sRGB and not ColorSpace.Gray)
        {
            image.ColorSpace = ColorSpace.sRGB;
        }

        image.Strip(); // bỏ EXIF/metadata: ảnh đã được xoay đúng, không cần nữa
    }
}
