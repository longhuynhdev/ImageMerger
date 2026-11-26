using System.Text.RegularExpressions;

namespace ImageMerger.Helpers;

/// <summary>
/// Phân tích chuỗi chọn trang kiểu hộp thoại In, ví dụ <c>"1-3, 5"</c>.
/// Quy tắc: trả về đúng những trang được gõ, theo đúng thứ tự gõ. Không loại trùng, không sắp xếp:
/// <c>"5, 1-3"</c> → trang 5, 1, 2, 3; <c>"1, 1"</c> → trang 1 hai lần.
/// Class thuần C#, không phụ thuộc WinUI, nên viết unit test được.
/// </summary>
public static partial class PageRangeParser
{
    /// <summary>
    /// Một phần tử: <c>"5"</c> hoặc <c>"1-3"</c>. Nhận cả gạch ngang thường và gạch "–" (en dash).
    /// Dùng [0-9] thay vì \d, vì \d trong .NET khớp cả chữ số của các bảng chữ khác (ví dụ chữ số Ả Rập).
    /// </summary>
    [GeneratedRegex(@"^([0-9]+)(?:\s*[-–]\s*([0-9]+))?$")]
    private static partial Regex PartRegex();

    /// <summary>
    /// Chuyển <paramref name="text"/> thành danh sách chỉ số trang (bắt đầu từ 0).
    /// Chuỗi trống hoặc chỉ có khoảng trắng nghĩa là "tất cả trang": trả về <c>true</c> và <paramref name="pageIndices"/> = <c>null</c>.
    /// </summary>
    /// <param name="text">Chuỗi người dùng gõ.</param>
    /// <param name="pageCount">Tổng số trang của file PDF.</param>
    /// <param name="pageIndices">Các trang đã chọn, theo thứ tự gõ; <c>null</c> = tất cả.</param>
    /// <param name="error">Thông báo lỗi tiếng Việt khi trả về <c>false</c>, ngược lại là chuỗi rỗng.</param>
    public static bool TryParse(string text, int pageCount, out IReadOnlyList<int>? pageIndices, out string error)
    {
        pageIndices = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) return true;

        var result = new List<int>();
        // Bỏ qua phần rỗng, để "1, 2," (đang gõ dở) không bị báo lỗi.
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = PartRegex().Match(part);
            if (!match.Success)
            {
                error = $"\"{part}\" không hợp lệ. Ví dụ: 1-3, 5";
                return false;
            }

            // Số quá lớn (tràn int) cũng coi là vượt quá số trang.
            if (!TryParsePage(match.Groups[1].Value, pageCount, out int first, out error)) return false;
            int last = first;
            if (match.Groups[2].Success && !TryParsePage(match.Groups[2].Value, pageCount, out last, out error)) return false;

            if (first > last)
            {
                error = $"Khoảng \"{part}\" bị ngược, hãy viết {last}-{first}";
                return false;
            }

            for (int page = first; page <= last; page++) result.Add(page - 1);
        }

        if (result.Count == 0)
        {
            error = "Chưa chọn trang nào";
            return false;
        }

        pageIndices = result;
        return true;
    }

    private static bool TryParsePage(string digits, int pageCount, out int page, out string error)
    {
        error = "";
        if (int.TryParse(digits, out page) && page >= 1 && page <= pageCount) return true;

        error = page == 0 && digits.Trim('0').Length == 0
            ? "Số trang bắt đầu từ 1"
            : $"File chỉ có {pageCount} trang";
        return false;
    }
}
