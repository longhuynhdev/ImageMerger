namespace ImageMerger.Services;

/// <summary>
/// Mở hộp thoại chọn file. Tách thành interface để ViewModel không phụ thuộc vào cửa sổ
/// và có thể thay bằng bản giả khi viết unit test.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Cho người dùng chọn nhiều ảnh. Trả về danh sách rỗng nếu người dùng huỷ.</summary>
    Task<IReadOnlyList<string>> PickImagesAsync();
}
