using ImageMerger.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ImageMerger.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();

        // Đăng ký với handledEventsToo = true: ListView có thể đánh dấu sự kiện kéo thả là
        // "đã xử lý" (vì nó đang lo việc đổi thứ tự), nhưng ta vẫn muốn nhận file từ Explorer.
        RootGrid.AddHandler(DragOverEvent, new DragEventHandler(OnFilesDragOver), handledEventsToo: true);
        RootGrid.AddHandler(DropEvent, new DragEventHandler(OnFilesDrop), handledEventsToo: true);
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

    /// <summary>
    /// Khi kéo file qua cửa sổ: báo cho Windows là ta nhận "Copy" để con trỏ hiện dấu +.
    /// Chỉ phản ứng khi dữ liệu kéo là file (StorageItems), bỏ qua khi người dùng
    /// đang kéo một item trong ListView để đổi thứ tự.
    /// </summary>
    private void OnFilesDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Thả vào để thêm";
    }

    /// <summary>
    /// Thả file: lấy đường dẫn rồi giao cho ViewModel. Đây là việc thuần UI (đọc dữ liệu kéo thả),
    /// còn lọc định dạng, sắp xếp, tạo thumbnail đều do ViewModel làm.
    /// <c>async void</c> chỉ chấp nhận được ở event handler như thế này.
    /// </summary>
    private async void OnFilesDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        // Deferral: báo cho hệ thống chờ ta đọc xong dữ liệu (vì có await) rồi mới kết thúc thao tác thả.
        var deferral = e.GetDeferral();
        try
        {
            IReadOnlyList<IStorageItem> storageItems = await e.DataView.GetStorageItemsAsync();
            ViewModel.AddFiles(storageItems.Select(item => item.Path).ToList());
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// "Xoá tất cả" là thao tác phá huỷ nên hỏi lại bằng ContentDialog (việc thuần UI, làm ở code-behind),
    /// người dùng đồng ý thì mới gọi command của ViewModel.
    /// Chữ trên nút nói rõ hậu quả ("Có, xoá tất cả" / "Không, giữ lại") thay vì "OK" / "Huỷ".
    /// ContentDialog cần XamlRoot để biết hiện trên cửa sổ nào.
    /// </summary>
    private async void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Xoá tất cả trang khỏi danh sách?",
            Content = "Các file ảnh và PDF gốc trên máy tính vẫn được giữ nguyên, chỉ danh sách trong ứng dụng bị xoá.",
            PrimaryButtonText = "Có, xoá tất cả",
            CloseButtonText = "Không, giữ lại",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ClearAllCommand.Execute(null);
        }
    }
}
