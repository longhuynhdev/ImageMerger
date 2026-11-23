using ImageMagick;
using ImageMerger.Models;

namespace ImageMerger.Services.Export;

/// <summary>
/// Phần chung của PNG và WebP: ghép các ảnh thành một ảnh dài theo chiều dọc.
/// Lớp con chỉ cần nói định dạng nào, kiểm tra gì thêm, và encode ra sao.
///
/// Cách ghép: tạo sẵn một "canvas" đúng kích thước cuối cùng, rồi lần lượt đọc từng ảnh,
/// thu phóng, dán vào đúng vị trí và Dispose ngay. Không dùng MagickImageCollection.AppendVertically()
/// vì cách đó cần mọi ảnh nằm trong RAM cùng lúc.
/// </summary>
public abstract class VerticalMergeExporter : IExporter
{
    public abstract ExportFormat Format { get; }

    public Task ExportAsync(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Export(sources, outputPath, options, progress, cancellationToken), cancellationToken);
    }

    /// <summary>Kiểm tra trước khi ghép. Ném <see cref="ExportValidationException"/> nếu không xuất được.</summary>
    protected virtual void Validate(VerticalMergeLayout layout) { }

    /// <summary>Ghi canvas đã ghép xong ra <paramref name="path"/>.</summary>
    protected abstract void Encode(MagickImage canvas, ExportOptions options, string path);

    private void Export(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // 1. Tính bố cục chỉ từ header (nhanh), để kiểm tra giới hạn trước khi làm việc nặng.
        var sizes = sources.Select(ReadSize).ToList();
        var layout = VerticalMergeLayout.Calculate(sizes, options.MaxWidth, options.Spacing);
        Validate(layout);

        // 2. Canvas tô sẵn màu nền: khoảng cách và vùng trong suốt sẽ có màu này.
        var background = new MagickColor(options.Background.R, options.Background.G, options.Background.B, options.Background.A);
        using var canvas = new MagickImage(background, (uint)layout.Width, (uint)layout.Height);

        // 3. Dán từng ảnh. Bước encode cuối tính là một bước tiến độ nữa (n + 1).
        int y = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (MagickImage image = ExportImageLoader.Load(sources[i], maxWidth: null))
            {
                if (image.ColorSpace is not ColorSpace.sRGB and not ColorSpace.Gray)
                {
                    image.ColorSpace = ColorSpace.sRGB; // ví dụ ảnh CMYK
                }

                // Thu phóng về đúng kích thước đã tính trong layout (bỏ qua tỉ lệ để khỏi lệch 1 px do làm tròn).
                var size = new MagickGeometry((uint)layout.Width, (uint)layout.ImageHeights[i]) { IgnoreAspectRatio = true };
                image.Resize(size);

                canvas.Composite(image, 0, y, CompositeOperator.Over);
            }

            y += layout.ImageHeights[i] + layout.Spacing;
            progress?.Report((i + 1) / (double)(sources.Count + 1));
        }

        // 4. Encode. Bước này không huỷ giữa chừng được, nên kiểm tra lần cuối trước khi bắt đầu.
        cancellationToken.ThrowIfCancellationRequested();
        canvas.Strip();
        ExportFile.WriteAtomically(outputPath, tempPath => Encode(canvas, options, tempPath));
        progress?.Report(1);
    }

    private static (int Width, int Height) ReadSize(ImageSourceInfo source)
    {
        try
        {
            return ImageHeader.ReadOrientedSize(source.Path);
        }
        catch (MagickException ex)
        {
            throw new InvalidDataException($"Không đọc được ảnh \"{Path.GetFileName(source.Path)}\".", ex);
        }
    }
}
