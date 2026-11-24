using System.Collections.Concurrent;
using System.Diagnostics;
using ImageMagick;
using ImageMagick.Formats;
using ImageMerger.Models;
using ImageMerger.Services.Export;

namespace ImageMerger.Services.Compression;

/// <summary>
/// Ước tính dung lượng PDF bằng cách lấy mẫu (CLAUDE.md mục 3.2):
/// 1. Mỗi ảnh có một bản mẫu thu nhỏ (cạnh dài <see cref="SampleMaxSize"/> px), tạo một lần rồi cache.
/// 2. Nén bản mẫu thành JPEG với đúng quality đang chọn để đo số byte trên mỗi pixel (bpp).
/// 3. dung lượng ≈ Σ (bpp_i × số pixel đầu ra_i × hiệu chỉnh_i) + overhead của PDF.
///
/// Ảnh thu nhỏ có nhiều chi tiết trên mỗi pixel hơn ảnh lớn, nên bpp đo trên mẫu thường cao hơn thực tế.
/// Mức chênh tăng theo độ phóng từ mẫu lên ảnh đầu ra, nên hệ số hiệu chỉnh có dạng
/// <c>scale^(-ScaleExponent)</c>, với scale = số pixel đầu ra / số pixel mẫu.
/// Ví dụ: ảnh 12 MP so với mẫu 0,2 MP thì scale ≈ 60; ảnh nhỏ hơn mẫu thì scale = 1, không hiệu chỉnh.
///
/// Class này dùng được từ nhiều luồng: lần ước tính cũ (đã huỷ) có thể chưa dừng hẳn khi lần mới bắt đầu.
/// </summary>
public sealed class SampleSizeEstimator : ISizeEstimator
{
    /// <summary>Cạnh dài của bản mẫu.</summary>
    public const uint SampleMaxSize = 512;

    /// <summary>
    /// Số mũ hiệu chỉnh theo độ phóng. 0 = không hiệu chỉnh.
    /// GIÁ TRỊ TẠM, CHƯA ĐO TRÊN ẢNH THẬT: chỉnh lại theo cột "k gợi ý" trong log Debug sau khi xuất
    /// khoảng 5 bộ ảnh khác nhau (xem ExportViewModel.LogEstimateAccuracy).
    /// </summary>
    internal const double ScaleExponent = 0.1;

    /// <summary>Phần chữ của mỗi trang ảnh do PdfPig ghi: object ảnh, nội dung, trang, xref (đo thực tế khoảng 560 byte).</summary>
    private const int PageOverheadBytes = 560;

    /// <summary>Header, Catalog, Pages, trailer của file PDF.</summary>
    private const int FileOverheadBytes = 200;

    /// <summary>Số ảnh tạo mẫu cùng lúc. Đo thử 20 ảnh 12 MP: 1 luồng 1,6 s, 4 luồng 0,7 s; thêm luồng lợi ít.</summary>
    private static readonly int MaxParallelSamples = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private readonly record struct SampleKey(string Path, int? PageIndex);

    /// <summary>
    /// Bản mẫu của một ảnh: pixel RGB thô (3 byte/pixel) để đọc lại cực nhanh, không phải decode như PNG.
    /// Giữ byte[] thay vì MagickImage để không phải Dispose khi xoá khỏi cache (một luồng khác có thể đang dùng).
    /// </summary>
    private sealed class Sample(byte[] rgbPixels, int width, int height, int sourceWidth, int sourceHeight)
    {
        public byte[] RgbPixels { get; } = rgbPixels;
        public int Width { get; } = width;
        public int Height { get; } = height;

        /// <summary>Kích thước ảnh gốc (đã xoay theo EXIF), để tính số pixel đầu ra.</summary>
        public int SourceWidth { get; } = sourceWidth;
        public int SourceHeight { get; } = sourceHeight;

        /// <summary>
        /// bpp đã đo theo từng quality. Kéo slider qua lại hoặc đổi preset thì quality cũ không phải đo lại.
        /// Đổi chiều rộng tối đa không cần đo lại: chỉ số pixel đầu ra thay đổi.
        /// </summary>
        public ConcurrentDictionary<int, double> BytesPerPixelByQuality { get; } = new();
    }

    /// <summary><c>null</c> = ảnh không đọc được; cache luôn để không thử decode lại mỗi lần ước tính.</summary>
    private readonly ConcurrentDictionary<SampleKey, Sample?> _samples = new();

    /// <inheritdoc />
    public Task<SizeEstimate> EstimateAsync(
        IReadOnlyList<ImageSourceInfo> sources,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Estimate(sources, options, cancellationToken), cancellationToken);
    }

    private SizeEstimate Estimate(IReadOnlyList<ImageSourceInfo> sources, ExportOptions options, CancellationToken cancellationToken)
    {
        RemoveStaleSamples(sources);

        // Bước 1 (song song): tạo bản mẫu còn thiếu và đo bpp cho quality này. Kết quả nằm trong cache.
        // Lần đầu với ảnh mới, việc decode chiếm gần hết thời gian (khoảng 50–90 ms/ảnh 12 MP),
        // nên chạy vài ảnh cùng lúc. Mỗi luồng chỉ decode ảnh ở 1/8 kích thước nên RAM tăng không đáng kể,
        // khác với lúc xuất (ảnh full size, phải làm lần lượt).
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = MaxParallelSamples,
        };
        Parallel.ForEach(sources, parallelOptions, source =>
        {
            if (SupportedFormats.IsPdf(source.Path)) return; // PDF không lấy mẫu, xem bước 2
            Sample? sample = GetSample(source);
            sample?.BytesPerPixelByQuality.GetOrAdd(options.Quality, quality => MeasureBytesPerPixel(sample, quality));
        });

        // Bước 2 (tuần tự, rất nhanh): cộng dồn từ cache.
        double corrected = FileOverheadBytes;
        double uncorrected = FileOverheadBytes;
        double weightedLogScale = 0;
        double totalWeight = 0;

        foreach (ImageSourceInfo source in sources)
        {
            if (SupportedFormats.IsPdf(source.Path))
            {
                // File PDF được chép nguyên trang, không phụ thuộc mức nén: lấy luôn dung lượng file gốc.
                // Cộng vào cả hai số, nên log "k gợi ý" bị lệch nhẹ khi bộ test có PDF: hiệu chỉnh hệ số bằng bộ chỉ có ảnh.
                long pdfBytes = PdfFileBytes(source.Path);
                corrected += pdfBytes;
                uncorrected += pdfBytes;
                continue;
            }

            Sample? sample = GetSample(source);
            if (sample is null) continue; // ảnh hỏng: lúc xuất sẽ báo lỗi riêng

            double bytesPerPixel = sample.BytesPerPixelByQuality.GetOrAdd(
                options.Quality, quality => MeasureBytesPerPixel(sample, quality));

            (int width, int height) = OutputSize(sample.SourceWidth, sample.SourceHeight, options.MaxWidth);
            double outputPixels = (double)width * height;
            double scale = Math.Max(1.0, outputPixels / ((double)sample.Width * sample.Height));

            double imageBytes = bytesPerPixel * outputPixels;
            uncorrected += imageBytes + PageOverheadBytes;
            corrected += imageBytes * Math.Pow(scale, -ScaleExponent) + PageOverheadBytes;

            weightedLogScale += imageBytes * Math.Log(scale);
            totalWeight += imageBytes;
        }

        double typicalScale = totalWeight > 0 ? Math.Exp(weightedLogScale / totalWeight) : 1.0;
        return new SizeEstimate((long)corrected, (long)uncorrected, typicalScale);
    }

    /// <summary>
    /// Lấy bản mẫu từ cache, chưa có thì tạo. Hai luồng cùng tạo một ảnh thì một bản bị bỏ, vô hại.
    /// </summary>
    private Sample? GetSample(ImageSourceInfo source) =>
        _samples.GetOrAdd(new SampleKey(source.Path, source.PageIndex), _ => CreateSample(source));

    /// <summary>Xoá bản mẫu của ảnh không còn trong danh sách, để cache không phình mãi.</summary>
    private void RemoveStaleSamples(IReadOnlyList<ImageSourceInfo> sources)
    {
        var current = sources.Select(s => new SampleKey(s.Path, s.PageIndex)).ToHashSet();
        foreach (SampleKey key in _samples.Keys)
        {
            if (!current.Contains(key)) _samples.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Đọc ảnh gốc, thu nhỏ về cạnh dài <see cref="SampleMaxSize"/> và chuẩn hoá giống lúc xuất.
    /// Ảnh gốc được Dispose ngay; chỉ giữ lại pixel của bản mẫu.
    /// </summary>
    private static Sample? CreateSample(ImageSourceInfo source)
    {
        try
        {
            (int sourceWidth, int sourceHeight) = ImageHeader.ReadOrientedSize(source.Path);

            // Giống ThumbnailService: JPEG được decode thẳng ở độ phân giải thấp (libjpeg chọn tỉ lệ n/8 nhỏ nhất
            // mà vẫn ≥ kích thước gợi ý). Gợi ý đúng 512 thay vì 1024 giúp ảnh 4032 px decode ở 1/8 thay vì 3/8,
            // nhanh gấp đôi (đo thử: 20 ảnh từ 3,6 s xuống 1,8 s trước khi chạy song song).
            var settings = new MagickReadSettings();
            settings.SetDefines(new JpegReadDefines { Size = new MagickGeometry(SampleMaxSize, SampleMaxSize) });

            using var image = new MagickImage(source.Path, settings);
            image.AutoOrient();
            image.Resize(new MagickGeometry(SampleMaxSize, SampleMaxSize) { Greater = true });
            ExportImageLoader.PrepareForJpeg(image);

            byte[] pixels = image.ToByteArray(MagickFormat.Rgb);
            return new Sample(pixels, (int)image.Width, (int)image.Height, sourceWidth, sourceHeight);
        }
        catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[SizeEstimate] Bỏ qua \"{source.Path}\": {ex.Message}");
            return null;
        }
    }

    /// <summary>Nén bản mẫu thành JPEG trong bộ nhớ với <paramref name="quality"/> và trả về số byte trên mỗi pixel.</summary>
    private static double MeasureBytesPerPixel(Sample sample, int quality)
    {
        // Dữ liệu RGB thô không có header, nên phải báo cho Magick.NET định dạng và kích thước.
        var settings = new MagickReadSettings
        {
            Format = MagickFormat.Rgb,
            Width = (uint)sample.Width,
            Height = (uint)sample.Height,
        };
        using var image = new MagickImage(sample.RgbPixels, settings);
        image.Quality = (uint)quality;

        using var stream = new MemoryStream();
        image.Write(stream, MagickFormat.Jpeg);
        return stream.Length / ((double)sample.Width * sample.Height);
    }

    /// <summary>
    /// Dung lượng file PDF nguồn (Bước 1: cả file được chép). Bản chép do PdfPig ghi có thể chênh vài %
    /// so với file gốc. File không đọc được thì tính 0, lúc xuất sẽ báo lỗi riêng.
    /// </summary>
    private static long PdfFileBytes(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Kích thước ảnh trong PDF: giống ExportImageLoader, chỉ thu nhỏ khi rộng hơn <paramref name="maxWidth"/>.</summary>
    private static (int Width, int Height) OutputSize(int width, int height, int? maxWidth)
    {
        if (maxWidth is int limit && width > limit)
        {
            return (limit, (int)Math.Round(height * (double)limit / width));
        }
        return (width, height);
    }
}
