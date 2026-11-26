namespace ImageMerger.Services.Export;

internal static class ExportFile
{
    /// <summary>
    /// Ghi vào file tạm rồi mới đổi tên thành <paramref name="outputPath"/>.
    /// Nếu huỷ hoặc lỗi giữa chừng thì xoá file tạm, không để lại file hỏng hay ghi đè mất file cũ.
    /// </summary>
    public static void WriteAtomically(string outputPath, Action<string> writeToPath)
    {
        string tempPath = outputPath + ".tmp";
        try
        {
            writeToPath(tempPath);
            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath); // không báo lỗi nếu file không tồn tại
            throw;
        }
    }
}
