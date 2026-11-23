namespace ImageMerger.Services.Export;

/// <summary>
/// Kích thước của ảnh ghép dọc, tính trước khi decode bất kỳ ảnh nào.
/// Mọi ảnh được thu phóng về cùng một chiều rộng (<see cref="Width"/>), giữ tỉ lệ.
/// </summary>
/// <param name="Width">Chiều rộng chung của ảnh ghép.</param>
/// <param name="Height">Tổng chiều cao, gồm cả khoảng cách giữa các ảnh.</param>
/// <param name="ImageHeights">Chiều cao của từng ảnh sau khi thu phóng.</param>
/// <param name="Spacing">Khoảng cách giữa hai ảnh liền nhau.</param>
public sealed record VerticalMergeLayout(int Width, long Height, IReadOnlyList<int> ImageHeights, int Spacing)
{
    /// <summary>
    /// Chiều rộng chung = ảnh rộng nhất (để không ảnh nào bị mất chi tiết),
    /// nhưng không vượt quá <paramref name="maxWidth"/>.
    /// </summary>
    /// <param name="sizes">Kích thước từng ảnh (đã xoay theo EXIF), theo đúng thứ tự ghép.</param>
    public static VerticalMergeLayout Calculate(IReadOnlyList<(int Width, int Height)> sizes, int? maxWidth, int spacing)
    {
        if (sizes.Count == 0) throw new ArgumentException("Cần ít nhất một ảnh.", nameof(sizes));

        int width = sizes.Max(s => s.Width);
        if (maxWidth is int limit && width > limit) width = limit;

        var heights = sizes
            .Select(s => Math.Max(1, (int)Math.Round(s.Height * (double)width / s.Width)))
            .ToList();
        long height = heights.Sum(h => (long)h) + (long)spacing * (sizes.Count - 1);

        return new VerticalMergeLayout(width, height, heights, spacing);
    }

    /// <summary>
    /// Chiều rộng lớn nhất sao cho tổng chiều cao không vượt <paramref name="maxHeight"/>.
    /// Chiều cao tỉ lệ thuận với chiều rộng (trừ phần khoảng cách cố định), nên chia thẳng được.
    /// Trả về 0 nếu riêng khoảng cách đã vượt giới hạn.
    /// </summary>
    public int MaxWidthToFitHeight(int maxHeight)
    {
        long spacingTotal = (long)Spacing * (ImageHeights.Count - 1);
        long imagesHeight = Height - spacingTotal;
        if (maxHeight <= spacingTotal || imagesHeight <= 0) return 0;

        return (int)Math.Floor(Width * (double)(maxHeight - spacingTotal) / imagesHeight);
    }
}
