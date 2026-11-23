using ImageMerger.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ImageMerger.Views;

/// <summary>
/// Hộp thoại chọn tuỳ chọn xuất. Không chứa logic: mọi thứ nằm trong <see cref="ExportViewModel"/>.
/// </summary>
public sealed partial class ExportDialog : ContentDialog
{
    public ExportDialog(ExportViewModel viewModel)
    {
        // Gán ViewModel trước InitializeComponent để x:Bind thấy ngay giá trị.
        ViewModel = viewModel;
        InitializeComponent();
    }

    public ExportViewModel ViewModel { get; }
}
