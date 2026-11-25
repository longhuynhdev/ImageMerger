namespace ImageMerger.Helpers;

/// <summary>
/// So sánh chuỗi kiểu "natural sort": các đoạn số được so theo giá trị,
/// nên "img2" đứng trước "img10". Phần chữ so sánh không phân biệt hoa thường.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int result = CompareNumberRun(x, ref i, y, ref j);
                if (result != 0) return result;
            }
            else
            {
                int result = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (result != 0) return result;
                i++;
                j++;
            }
        }

        // Chuỗi nào hết trước thì đứng trước ("a" < "a1").
        int lengthResult = (x.Length - i).CompareTo(y.Length - j);
        return lengthResult != 0 ? lengthResult : string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// So sánh hai đoạn số liên tiếp bắt đầu tại <paramref name="i"/> và <paramref name="j"/>.
    /// So theo độ dài sau khi bỏ số 0 ở đầu, rồi so từng chữ số, nên không bao giờ bị tràn số.
    /// </summary>
    private static int CompareNumberRun(string x, ref int i, string y, ref int j)
    {
        int startX = i, startY = j;
        while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
        while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

        ReadOnlySpan<char> numX = x.AsSpan(startX, i - startX).TrimStart('0');
        ReadOnlySpan<char> numY = y.AsSpan(startY, j - startY).TrimStart('0');

        if (numX.Length != numY.Length) return numX.Length.CompareTo(numY.Length);
        int digits = numX.SequenceCompareTo(numY);
        if (digits != 0) return digits;

        // Cùng giá trị: "01" và "1" → chuỗi ít số 0 ở đầu đứng trước.
        return (i - startX).CompareTo(j - startY);
    }
}
