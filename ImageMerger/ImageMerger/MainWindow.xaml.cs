using System.Runtime.InteropServices;
using ImageMerger.Services;
using ImageMerger.Services.Export;
using ImageMerger.ViewModels;
using ImageMerger.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
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
        ResizeAndCenter();

        var viewModel = new MainViewModel(
            new FilePickerService(AppWindow.Id),
            new ThumbnailService(),
            new PdfExporter());
        RootFrame.Navigate(typeof(MainPage), viewModel);
    }

    /// <summary>
    /// Đặt kích thước mặc định và căn giữa cửa sổ trong vùng làm việc của màn hình.
    /// AppWindow dùng pixel vật lý, nên kích thước DIP phải nhân với hệ số DPI.
    /// </summary>
    private void ResizeAndCenter()
    {
        IntPtr hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = GetDpiForWindow(hwnd) / 96.0;

        // DisplayArea = một màn hình; WorkArea là phần màn hình không tính taskbar.
        RectInt32 workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

        // Không cho cửa sổ lớn hơn vùng làm việc (ví dụ 720 DIP ở scale 150% = 1080 px, cao hơn màn hình 1080p trừ taskbar).
        int width = Math.Min((int)(DefaultWidthDip * scale), workArea.Width);
        int height = Math.Min((int)(DefaultHeightDip * scale), workArea.Height);
        int x = workArea.X + (workArea.Width - width) / 2;
        int y = workArea.Y + (workArea.Height - height) / 2;

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
