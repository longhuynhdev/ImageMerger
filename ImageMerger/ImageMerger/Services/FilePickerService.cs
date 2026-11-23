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
    public async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        var picker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (string extension in SupportedFormats.InputExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var results = await picker.PickMultipleFilesAsync();
        return results.Select(r => r.Path).ToList();
    }

    public async Task<string?> PickSaveFileAsync(string suggestedFileName, string fileTypeLabel, string extension)
    {
        var picker = new FileSavePicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            DefaultFileExtension = extension,
        };
        picker.FileTypeChoices.Add(fileTypeLabel, [extension]);

        // Người dùng bấm Huỷ thì kết quả là null.
        PickFileResult? result = await picker.PickSaveFileAsync();
        return result?.Path;
    }
}
