using ImageMerger.Models;

namespace ImageMerger.Services.Export;

public interface IExporter
{
    /// <summary>Định dạng mà exporter này tạo ra. MainViewModel dựa vào đây để chọn exporter.</summary>
    ExportFormat Format { get; }

    /// <summary>
    /// Gộp các ảnh theo đúng thứ tự trong <paramref name="sources"/> và ghi ra <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="progress">Nhận giá trị 0..1 sau mỗi ảnh.</param>
    Task ExportAsync(
        IReadOnlyList<ImageSourceInfo> sources,
        string outputPath,
        ExportOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
