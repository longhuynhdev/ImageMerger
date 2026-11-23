using System.Runtime.InteropServices;
using ImageMerger.Services;
using ImageMerger.Services.Export;
using ImageMerger.ViewModels;
using ImageMerger.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ImageMerger;

/// <summary>
/// Cửa sổ chính. Chỉ chứa title bar và một Frame; nội dung nằm trong <see cref="MainPage"/>.
/// Đây cũng là nơi "lắp ráp" service và ViewModel (composition root), vì chỉ cửa sổ mới có AppWindow.Id.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultWidthDip = 900;
    private const int DefaultHeightDip = 720;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ResizeToDefault();

        var viewModel = new MainViewModel(
            new FilePickerService(AppWindow.Id),
            new ThumbnailService(),
            (IExporter[])[new PdfExporter(), new PngExporter(), new WebpExporter()]);
        RootFrame.Navigate(typeof(MainPage), viewModel);
    }

    /// <summary>AppWindow.Resize nhận pixel vật lý, nên phải nhân với hệ số DPI của màn hình.</summary>
    private void ResizeToDefault()
    {
        IntPtr hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(DefaultWidthDip * scale), (int)(DefaultHeightDip * scale)));
    }
}
