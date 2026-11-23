namespace ImageMerger.Services;

/// <summary>
/// Mở hộp thoại chọn file. Tách thành interface để ViewModel không phụ thuộc vào cửa sổ
/// và có thể thay bằng bản giả khi viết unit test.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Cho người dùng chọn nhiều ảnh. Trả về danh sách rỗng nếu người dùng huỷ.</summary>
    Task<IReadOnlyList<string>> PickImagesAsync();

    /// <summary>Hỏi nơi lưu file. Trả về <c>null</c> nếu người dùng huỷ.</summary>
    /// <param name="suggestedFileName">Tên gợi ý, không gồm phần mở rộng.</param>
    /// <param name="fileTypeLabel">Nhãn loại file trong hộp thoại, ví dụ "Tài liệu PDF".</param>
    /// <param name="extension">Phần mở rộng, ví dụ ".pdf".</param>
    Task<string?> PickSaveFileAsync(string suggestedFileName, string fileTypeLabel, string extension);
}
