using System.Globalization;
using System.Text;

namespace ImageMerger.Services.Export;

/// <summary>
/// Ghi file PDF tối giản, mỗi trang là một ảnh JPEG.
///
/// Vì sao không dùng PDF writer của Magick.NET: nó cần mọi trang nằm trong một
/// MagickImageCollection, tức là mọi ảnh đều được decode trong RAM cùng lúc.
/// Ở đây mỗi ảnh được nén JPEG xong thì ghi thẳng vào file, RAM chỉ giữ một ảnh.
/// PDF cho phép nhúng nguyên byte JPEG (bộ lọc /DCTDecode), nên không cần thư viện PDF.
///
/// Cấu trúc file: header → các object (ảnh, nội dung trang, trang) → object Pages, Catalog
/// → bảng xref (vị trí byte của từng object) → trailer.
/// </summary>
internal sealed class JpegPdfWriter
{
    // Hai object gốc có số cố định, nhưng được ghi ở cuối khi đã biết danh sách trang.
    private const int CatalogId = 1;
    private const int PagesId = 2;

    private readonly Stream _stream;
    private readonly List<long> _objectOffsets = [0, 0]; // index = id - 1; giữ chỗ cho Catalog, Pages
    private readonly List<int> _pageIds = [];

    public JpegPdfWriter(Stream stream)
    {
        _stream = stream;
        WriteAscii("%PDF-1.4\n");
        // Dòng comment chứa byte > 127: quy ước để các công cụ biết file có dữ liệu nhị phân.
        _stream.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
    }

    /// <summary>Thêm một trang chứa ảnh JPEG phủ kín trang.</summary>
    /// <param name="pageWidth">Chiều rộng trang, đơn vị point (1/72 inch).</param>
    /// <param name="pageHeight">Chiều cao trang, đơn vị point.</param>
    public void AddPage(byte[] jpeg, int pixelWidth, int pixelHeight, bool isGray, double pageWidth, double pageHeight)
    {
        int imageId = BeginObject();
        string colorSpace = isGray ? "/DeviceGray" : "/DeviceRGB";
        WriteAscii(
            $"<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} " +
            $"/ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\n" +
            "stream\n");
        _stream.Write(jpeg);
        WriteAscii("\nendstream\nendobj\n");

        // Lệnh vẽ: phóng ảnh (vốn là hình vuông 1×1) ra đúng kích thước trang rồi vẽ.
        string content = $"q {Num(pageWidth)} 0 0 {Num(pageHeight)} 0 0 cm /Im0 Do Q";
        int contentId = BeginObject();
        WriteAscii($"<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

        int pageId = BeginObject();
        WriteAscii(
            $"<< /Type /Page /Parent {PagesId} 0 R /MediaBox [0 0 {Num(pageWidth)} {Num(pageHeight)}] " +
            $"/Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentId} 0 R >>\nendobj\n");
        _pageIds.Add(pageId);
    }

    /// <summary>Ghi các object gốc, bảng xref và trailer. Gọi đúng một lần sau trang cuối.</summary>
    public void Finish()
    {
        BeginObject(PagesId);
        string kids = string.Join(" ", _pageIds.Select(id => $"{id} 0 R"));
        WriteAscii($"<< /Type /Pages /Kids [{kids}] /Count {_pageIds.Count} >>\nendobj\n");

        BeginObject(CatalogId);
        WriteAscii($"<< /Type /Catalog /Pages {PagesId} 0 R >>\nendobj\n");

        // Mỗi dòng xref dài đúng 20 byte: offset 10 chữ số, generation 5 chữ số, cờ n/f, 2 ký tự xuống dòng.
        long xrefOffset = _stream.Position;
        int size = _objectOffsets.Count + 1; // +1 cho object số 0 (bắt buộc, luôn "free")
        var xref = new StringBuilder();
        xref.Append($"xref\n0 {size}\n");
        xref.Append("0000000000 65535 f \n");
        foreach (long offset in _objectOffsets)
        {
            xref.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        xref.Append($"trailer\n<< /Size {size} /Root {CatalogId} 0 R >>\n");
        xref.Append($"startxref\n{xrefOffset}\n%%EOF\n");
        WriteAscii(xref.ToString());
    }

    /// <summary>Bắt đầu một object mới, ghi nhớ vị trí byte của nó cho bảng xref.</summary>
    private int BeginObject()
    {
        _objectOffsets.Add(_stream.Position);
        int id = _objectOffsets.Count;
        WriteAscii($"{id} 0 obj\n");
        return id;
    }

    private void BeginObject(int reservedId)
    {
        _objectOffsets[reservedId - 1] = _stream.Position;
        WriteAscii($"{reservedId} 0 obj\n");
    }

    private void WriteAscii(string text) => _stream.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Số thực trong PDF luôn dùng dấu chấm. Nếu dùng culture hiện tại (vi-VN) sẽ ra "595,5" và hỏng file.
    /// </summary>
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
