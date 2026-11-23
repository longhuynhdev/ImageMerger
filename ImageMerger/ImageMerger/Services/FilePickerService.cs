using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace ImageMerger.Services;

/// <summary>
/// Dùng <see cref="FileOpenPicker"/> mới của Windows App SDK. Picker này nhận
/// <see cref="WindowId"/> (lấy từ <c>AppWindow.Id</c>) để biết cửa sổ cha,
/// thay cho cách cũ <c>InitializeWithWindow</c> với HWND.
/// </summary>
public sealed class FilePickerService(WindowId windowId) : IFilePickerService
{
    /// <summary>Phase 1 chỉ hỗ trợ PNG/JPEG. Phase 7 sẽ thêm WebP, HEIC, PDF.</summary>
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

    public async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        var picker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (string extension in ImageExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var results = await picker.PickMultipleFilesAsync();
        return results.Select(r => r.Path).ToList();
    }
}
