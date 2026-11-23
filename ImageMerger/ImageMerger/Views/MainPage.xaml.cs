using ImageMerger.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ImageMerger.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// x:Bind trong Page bind vào chính class này (không dùng DataContext),
    /// nên ta expose ViewModel thành property để XAML gọi <c>ViewModel.Items</c>...
    /// </summary>
    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>
    /// MainWindow truyền ViewModel qua tham số của <c>Frame.Navigate</c>.
    /// OnNavigatedTo chạy trước khi x:Bind khởi tạo binding (lúc Loaded), nên ViewModel đã sẵn sàng.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = (MainViewModel)e.Parameter;
    }

    /// <summary>Hàm tĩnh dùng trong x:Bind thay cho IValueConverter: bool → Visibility.</summary>
    public static Visibility ToVisibility(bool isVisible) =>
        isVisible ? Visibility.Visible : Visibility.Collapsed;
}
