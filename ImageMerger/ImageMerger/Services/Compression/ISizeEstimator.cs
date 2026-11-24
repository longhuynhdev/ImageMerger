using ImageMerger.Models;

namespace ImageMerger.Services.Compression;

/// <summary>Kết quả ước tính dung lượng file PDF.</summary>
/// <param name="Bytes">Dung lượng ước tính (byte), đã áp hệ số hiệu chỉnh. Đây là số hiển thị cho người dùng.</param>
/// <param name="UncorrectedBytes">Dung lượng khi chưa hiệu chỉnh. Chỉ dùng để ghi log và tinh chỉnh hệ số.</param>
/// <param name="TypicalScale">
/// Tỉ lệ "số pixel đầu ra / số pixel bản mẫu" tiêu biểu của bộ ảnh (trung bình nhân, có trọng số).
/// Chỉ dùng để ghi log: cùng với dung lượng thật, nó cho biết hệ số hiệu chỉnh nên là bao nhiêu.
/// </param>
public sealed record SizeEstimate(long Bytes, long UncorrectedBytes, double TypicalScale);

/// <summary>Ước tính dung lượng file PDF đầu ra trước khi xuất.</summary>
public interface ISizeEstimator
{
    /// <summary>
    /// Ước tính dung lượng khi xuất <paramref name="sources"/> với <paramref name="options"/>.
    /// Chạy nền; ném <see cref="OperationCanceledException"/> khi bị huỷ.
    /// </summary>
    Task<SizeEstimate> EstimateAsync(
        IReadOnlyList<ImageSourceInfo> sources,
        ExportOptions options,
        CancellationToken cancellationToken);
}
