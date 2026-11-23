# Học WinUI 3 và Magick.NET qua code ImageMerger

Tài liệu này dành cho người mới với WinUI 3 và Magick.NET. Mỗi khái niệm đều chỉ ra **file nào trong ImageMerger đang dùng nó**, để bạn mở code ra đọc song song. Đừng cố đọc hết một lần: đọc phần 1–2, mở app chạy thử, rồi quay lại các phần sau khi cần.

**Mục lục**

1. [Bản đồ codebase: đọc file nào trước](#1-bản-đồ-codebase-đọc-file-nào-trước)
2. [Một cú bấm đi qua những đâu](#2-một-cú-bấm-đi-qua-những-đâu)
3. [WinUI 3: các khái niệm nền](#3-winui-3-các-khái-niệm-nền)
4. [Binding hoạt động thế nào](#4-binding-hoạt-động-thế-nào)
5. [Command: thay cho Click handler](#5-command-thay-cho-click-handler)
6. [Luồng (thread) và async trong WinUI](#6-luồng-thread-và-async-trong-winui)
7. [Các control đang dùng trong app](#7-các-control-đang-dùng-trong-app)
8. [Magick.NET](#8-magicknet)
9. [MVVM: vì sao có tầng ViewModel](#9-mvvm-vì-sao-có-tầng-viewmodel)
10. [Bảng tra: CommunityToolkit.Mvvm và code tự viết](#10-bảng-tra-communitytoolkitmvvm-và-code-tự-viết)
11. [Lỗi hay gặp](#11-lỗi-hay-gặp)
12. [Bài tập nhỏ để tự luyện](#12-bài-tập-nhỏ-để-tự-luyện)

---

## 1. Bản đồ codebase: đọc file nào trước

App chia làm 4 tầng. Mũi tên nghĩa là "biết về / gọi tới". Không có mũi tên ngược lại.

```
View (XAML + code-behind)  ──binding──▶  ViewModel  ──gọi──▶  Service  ──dùng──▶  Model
   Views/*.xaml                          ViewModels/*.cs        Services/**/*.cs      Models/*.cs
                                                                (Magick.NET ở đây)
```

| Tầng | Trách nhiệm | Không được làm |
|---|---|---|
| **Model** | Dữ liệu thuần: `ImageSourceInfo`, `ExportOptions`, enum `ExportFormat`… | Không biết UI, không biết Magick |
| **Service** | Việc nặng: đọc ảnh, tạo thumbnail, xuất file (Magick.NET) | Không dùng kiểu của WinUI (`BitmapImage`, `Visibility`…) |
| **ViewModel** | Trạng thái màn hình + logic: danh sách ảnh, đang xuất hay không, tuỳ chọn nào hợp lệ | Không chạm vào control cụ thể (`ListView`, `Button`…) |
| **View** | Vẽ giao diện, bind vào ViewModel, việc thuần UI (mở dialog, kéo thả) | Không gọi Service trực tiếp |

**Thứ tự đọc gợi ý** (mỗi file đều có comment tiếng Việt giải thích):

1. `App.xaml.cs` → điểm bắt đầu: khởi tạo Magick, tạo `MainWindow`.
2. `MainWindow.xaml.cs` → "lắp ráp" service + ViewModel, rồi điều hướng tới `MainPage`.
3. `Helpers/BindableBase.cs` → nền tảng của mọi binding (phần 4).
4. `Helpers/RelayCommand.cs` → nền tảng của mọi nút bấm (phần 5).
5. `ViewModels/ImageItemViewModel.cs` → ViewModel nhỏ nhất, dễ đọc nhất.
6. `Views/MainPage.xaml` song song với `ViewModels/MainViewModel.cs`: mở hai file cạnh nhau, tìm từng `{x:Bind ViewModel.Xxx}` trong XAML rồi tìm property `Xxx` trong ViewModel.
7. `Services/ThumbnailService.cs` → Magick.NET đơn giản nhất.
8. `Views/ExportDialog.xaml` + `ViewModels/ExportViewModel.cs` → binding hai chiều, validation.
9. `Services/Export/*` → Magick.NET nâng cao.

---

## 2. Một cú bấm đi qua những đâu

Hiểu được hai luồng dưới đây là hiểu được 80% app.

### 2.1. Bấm "Thêm ảnh"

```
[MainPage.xaml]  <Button Command="{x:Bind ViewModel.AddImagesCommand}">
      │ người dùng bấm → Button gọi ICommand.Execute()
      ▼
[MainViewModel]  AddImagesAsync()
      │ await _filePickerService.PickImagesAsync()        ← mở hộp thoại chọn file
      ▼
      AddFiles(paths)
      │ lọc định dạng, natural sort, tạo ImageItemViewModel cho mỗi file
      │ Items.Add(item)  ← ObservableCollection báo cho ListView → dòng mới hiện ngay
      ▼
      ProcessThumbnailQueueAsync()                         ← lần lượt từng ảnh
      │
[ImageItemViewModel]  LoadThumbnailAsync()
      │ await Task.Run(() => thumbnailService.CreateThumbnail(...))   ← luồng nền, Magick.NET
      │ (sau await: đã quay về UI thread)
      │ Thumbnail = bitmap  ← setter gọi SetProperty → PropertyChanged("Thumbnail")
      ▼
[MainPage.xaml]  <Image Source="{x:Bind Thumbnail, Mode=OneWay}">  ← binding nghe thấy, vẽ ảnh
```

### 2.2. Bấm "Xuất ảnh"

```
[MainPage.xaml.cs] OnExportClick
      │ new ExportDialog(ViewModel.ExportSettings)  ← việc thuần UI: mở dialog
      │ người dùng chọn định dạng / preset (binding TwoWay ghi thẳng vào ExportViewModel)
      ▼ bấm "Xuất"
[MainViewModel]  ExportCommand → ExportAsync()
      │ options = ExportSettings.ToOptions()     ← ViewModel → Model thuần
      │ exporter = _exporters.Single(e => e.Format == options.Format)
      │ _exportCts = new CancellationTokenSource()
      │ await exporter.ExportAsync(..., progress, _exportCts.Token)
      ▼
[PdfExporter / PngExporter / WebpExporter]   ← Task.Run, Magick.NET, đọc từng ảnh rồi Dispose
      │ progress.Report(0.4) → Progress<T> chuyển về UI thread → ExportProgress = 40
      ▼
[MainViewModel]  ShowStatus(...) → InfoBar hiện "Đã xuất 12 ảnh (3.4 MB)"
```

Nút **Huỷ** bind vào `ExportCancelCommand`, command này gọi `_exportCts.Cancel()`. Exporter kiểm tra `cancellationToken.ThrowIfCancellationRequested()` ở mỗi ảnh, ném `OperationCanceledException`, và `ExportAsync` bắt nó để báo "Đã huỷ".

---

## 3. WinUI 3: các khái niệm nền

### 3.1. WinUI 3 và Windows App SDK là gì

- **WinUI 3** là bộ control giao diện (Button, ListView…) mới nhất của Microsoft cho app desktop Windows.
- **Windows App SDK** là gói NuGet chứa WinUI 3 cùng các API khác (cửa sổ `AppWindow`, File Picker mới…). Nó tách khỏi Windows nên cập nhật qua NuGet chứ không phải chờ cập nhật Windows.
- Namespace của WinUI 3 bắt đầu bằng **`Microsoft.UI.Xaml`**. Nếu thấy ví dụ trên mạng dùng `Windows.UI.Xaml`, đó là UWP (thế hệ cũ), đa số vẫn dùng được nếu đổi namespace.

### 3.2. XAML và code-behind: hai nửa của một class

`MainPage.xaml` và `MainPage.xaml.cs` là **hai phần của cùng một class** `MainPage` (nhờ từ khoá `partial`).

- XAML mô tả cây control.
- Lúc build, compiler sinh ra `MainPage.g.cs` (trong `obj/`) chứa hàm `InitializeComponent()`: hàm này đọc XAML và tạo các control.
- `x:Name="RootGrid"` trong XAML sinh ra một field `RootGrid` dùng được trong code-behind.

Vì vậy constructor của mọi Page/Window/Dialog đều gọi `InitializeComponent()` đầu tiên.

### 3.3. App → Window → Frame → Page

```
App (App.xaml.cs)                 tạo MainWindow khi app khởi động (OnLaunched)
 └─ MainWindow (Window)           cửa sổ thật, có title bar
     └─ Frame (RootFrame)         "khung" để hiển thị Page, hỗ trợ điều hướng
         └─ MainPage (Page)       nội dung chính
```

- **`Window`**: cửa sổ. Không có `DataContext`, không có sự kiện điều hướng, nên ta đặt nội dung vào một `Page`.
- **`Frame.Navigate(typeof(MainPage), parameter)`**: tạo `MainPage` và truyền `parameter` cho nó. Page nhận lại trong `OnNavigatedTo(e)` qua `e.Parameter`. Đây là cách `MainWindow` truyền `MainViewModel` cho `MainPage`.
- **`AppWindow`**: API cấp thấp hơn của cửa sổ (đổi kích thước, icon, lấy `AppWindow.Id`). File Picker mới cần `AppWindow.Id` để biết mở trên cửa sổ nào.

### 3.4. Layout: Grid và StackPanel

- **`Grid`**: chia hàng/cột. `Height="Auto"` = vừa đủ nội dung, `Height="*"` = chiếm phần còn lại. Control con chọn ô bằng `Grid.Row="1"`, `Grid.Column="2"`. Hai control cùng một ô thì **chồng lên nhau** (MainPage dùng điều này: màn hình trống và ListView cùng nằm ở `Grid.Row="3"`, chỉ một trong hai `Visible`).
- **`StackPanel`**: xếp con theo một hàng dọc (hoặc ngang với `Orientation="Horizontal"`). `Spacing="8"` = khoảng cách giữa các con.
- `RowSpacing` / `ColumnSpacing` / `Padding` / `Margin`: khoảng cách, đơn vị là **pixel độc lập DPI** (tự phóng to trên màn hình 150%).

### 3.5. Resource và theme

- `{StaticResource AccentButtonStyle}`: lấy một style có sẵn của WinUI.
- `{ThemeResource TextFillColorSecondaryBrush}`: giống StaticResource nhưng **tự đổi khi người dùng chuyển Sáng/Tối**. Luôn dùng ThemeResource cho màu, đừng viết cứng `#888888`.

### 3.6. Visibility

Control có `Visibility="Visible"` hoặc `"Collapsed"` (ẩn và không chiếm chỗ). Nó là enum chứ không phải bool, nên ta có hàm `Helpers/XamlFunctions.ToVisibility(bool)` để đổi bool thành Visibility ngay trong `x:Bind` (xem phần 4.5).

---

## 4. Binding hoạt động thế nào

Đây là phần quan trọng nhất. Binding là "dây nối" giữa một property của control và một property của ViewModel.

### 4.1. x:Bind cơ bản

```xml
<TextBlock Text="{x:Bind ViewModel.StatusTitle, Mode=OneWay}" />
```

- `x:Bind` tìm `ViewModel` **trên chính class `MainPage`** (không dùng `DataContext` như WPF). Vì thế `MainPage.xaml.cs` có property `public MainViewModel ViewModel { get; }`.
- `x:Bind` được **kiểm tra lúc compile**: gõ sai tên property là lỗi build ngay, không âm thầm hỏng lúc chạy như `{Binding}`.
- Compiler sinh code C# thật (trong `obj/.../MainPage.g.cs`) để đọc `ViewModel.StatusTitle` và gán vào `TextBlock.Text`. Bạn có thể mở file đó ra xem.

### 4.2. Ba chế độ (Mode)

| Mode | Chiều | Khi nào dùng | Ví dụ trong app |
|---|---|---|---|
| `OneTime` *(mặc định của x:Bind!)* | VM → UI, **chỉ một lần** | Giá trị không bao giờ đổi | `Text="{x:Bind DisplayName}"` |
| `OneWay` | VM → UI, mỗi khi VM báo đổi | Giá trị đổi theo thời gian | `Source="{x:Bind Thumbnail, Mode=OneWay}"` |
| `TwoWay` | VM ⇄ UI | Người dùng nhập/chọn | `Value="{x:Bind ViewModel.CustomQuality, Mode=TwoWay}"` |

> ⚠️ **Lỗi số 1 của người mới:** quên `Mode=OneWay`. UI hiện đúng giá trị lúc đầu rồi không bao giờ cập nhật nữa. (`{Binding}` cũ mặc định là OneWay, `x:Bind` mặc định là OneTime.)

### 4.3. INotifyPropertyChanged: làm sao UI biết giá trị đã đổi

Binding `OneWay` không tự "theo dõi" property. Nó chỉ **lắng nghe một sự kiện**: `PropertyChanged`. Interface `INotifyPropertyChanged` chỉ có đúng sự kiện này.

Xem `Helpers/BindableBase.cs`:

```csharp
public abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false; // không đổi thì thôi
        field = value;
        OnPropertyChanged(propertyName);  // "này UI, property <tên> vừa đổi"
        return true;
    }
    ...
}
```

Một property "observable" luôn có dạng:

```csharp
private string _statusTitle = "";          // 1. field lưu giá trị

public string StatusTitle
{
    get => _statusTitle;                   // 2. binding đọc qua getter
    set => SetProperty(ref _statusTitle, value);  // 3. gán + raise PropertyChanged
}
```

Trình tự khi code gán `StatusTitle = "Xong"`:

1. `SetProperty` so sánh, thấy khác, gán `_statusTitle = "Xong"`.
2. Raise `PropertyChanged` với tên `"StatusTitle"` (tên do `[CallerMemberName]` tự điền).
3. Code sinh ra của `x:Bind` đang lắng nghe, thấy tên khớp, gọi lại getter, gán vào `TextBlock.Text`.

**Vì sao kiểm tra "không đổi thì thôi"?** Với `TwoWay`, UI gán vào VM, VM raise sự kiện, UI lại đọc… Nếu không chặn khi giá trị bằng nhau, có thể lặp vô hạn.

### 4.4. Property tính toán (computed property)

```csharp
public bool CanExport => HasItems && !IsExporting;
```

Property này **không có setter**, nên không ai gọi `SetProperty` cho nó. Phải báo thay ở nơi các giá trị nguồn thay đổi:

```csharp
public bool IsExporting
{
    get => _isExporting;
    set
    {
        if (SetProperty(ref _isExporting, value))     // true = giá trị thật sự đổi
        {
            OnPropertyChanged(nameof(CanExport));     // báo luôn cho property phụ thuộc
            ExportCancelCommand.NotifyCanExecuteChanged();
        }
    }
}
```

`ExportViewModel.Format` là ví dụ lớn nhất: đổi định dạng thì 11 property phụ thuộc phải báo lại. Quên báo một cái là một control "đứng hình". Khi thấy control không cập nhật, câu hỏi đầu tiên luôn là: *"property này đã được báo PropertyChanged chưa?"*

### 4.5. Gọi hàm trong x:Bind

```xml
Visibility="{x:Bind helpers:XamlFunctions.ToVisibility(ViewModel.HasItems), Mode=OneWay}"
```

`x:Bind` gọi được hàm static. Với `Mode=OneWay`, khi `HasItems` báo đổi, hàm được gọi lại. Đây là cách thay cho `IValueConverter` dài dòng của WPF.

### 4.6. ObservableCollection: danh sách tự cập nhật

`INotifyPropertyChanged` báo khi **một property** đổi. Còn khi **thêm/xoá phần tử** trong danh sách thì cần `ObservableCollection<T>`: nó raise `CollectionChanged` (Add, Remove, Move…). `ListView` nghe sự kiện này và chỉ vẽ lại đúng dòng bị ảnh hưởng.

```csharp
public ObservableCollection<ImageItemViewModel> Items { get; } = [];
```

```xml
<ListView ItemsSource="{x:Bind ViewModel.Items}">
```

Lưu ý:
- `ItemsSource` không cần `Mode=OneWay` vì **bản thân** `Items` không bao giờ bị gán lại, chỉ nội dung của nó thay đổi.
- `Items.Count` **không** tự báo cho UI. Vì thế `MainViewModel` đăng ký `Items.CollectionChanged += OnItemsChanged` và tự báo `IsEmpty`, `HasItems`, `CanExport`.
- Dùng `Items.Move(old, new)` thay vì Remove + Insert: ListView chỉ dời một dòng, thumbnail không bị tạo lại.

### 4.7. DataTemplate và x:DataType

```xml
<ListView.ItemTemplate>
    <DataTemplate x:DataType="vm:ImageItemViewModel">
        <TextBlock Text="{x:Bind DisplayName}" />
    </DataTemplate>
</ListView.ItemTemplate>
```

- `DataTemplate` là "khuôn" để vẽ **mỗi phần tử** của danh sách.
- Trong template, `x:Bind` không bind vào `MainPage` nữa mà bind vào **từng item**. `x:DataType` cho compiler biết item có kiểu gì để kiểm tra tên property lúc build.
- `xmlns:vm="using:ImageMerger.ViewModels"` ở đầu file khai báo tiền tố `vm:` trỏ tới namespace C#.

### 4.8. Thời điểm khởi tạo binding

`x:Bind` khởi tạo khi Page bắt đầu được nạp (sự kiện `Loading`), sau `OnNavigatedTo`. Vì thế:
- `MainPage` gán `ViewModel` trong `OnNavigatedTo` là kịp.
- `ExportDialog` gán `ViewModel` **trước** `InitializeComponent()` trong constructor.

Nếu gán `ViewModel` quá muộn, binding đã đọc `null` và (với OneTime) sẽ không đọc lại. Khi đó gọi `Bindings.Update()` để ép đọc lại toàn bộ.

---

## 5. Command: thay cho Click handler

### 5.1. ICommand

`Button`, `MenuFlyoutItem`, `AppBarButton` có property `Command` kiểu `ICommand`:

```csharp
public interface ICommand
{
    void Execute(object? parameter);       // việc cần làm khi bấm
    bool CanExecute(object? parameter);    // false → control tự mờ đi (disabled)
    event EventHandler? CanExecuteChanged; // "hỏi lại CanExecute đi"
}
```

Lợi ích so với `Click="..."`: logic nằm trong ViewModel (test được), và trạng thái bật/tắt nút được quản lý tự động.

### 5.2. RelayCommand trong app

Xem `Helpers/RelayCommand.cs`. `RelayCommand` bọc một `Action` và một `Func<bool>` (điều kiện):

```csharp
SortByNameCommand = new RelayCommand(SortByName, () => HasItems);
```

```xml
<MenuFlyoutItem Command="{x:Bind ViewModel.SortByNameCommand}" Text="Theo tên" />
```

**Quan trọng:** control chỉ hỏi `CanExecute` khi nhận sự kiện `CanExecuteChanged`. Khi `HasItems` đổi, phải gọi `SortByNameCommand.NotifyCanExecuteChanged()` (xem `MainViewModel.OnItemsChanged`). Quên gọi thì menu "Theo tên" mờ mãi, kể cả khi đã có ảnh.

### 5.3. AsyncRelayCommand

Với việc async (mở picker, xuất file), `AsyncRelayCommand` trả `CanExecute = false` trong lúc task đang chạy, nên người dùng bấm hai lần cũng không mở hai picker. Code muốn **chờ** command xong thì gọi `ExecuteAsync()` (xem `MainPage.OnExportClick`).

### 5.4. Command trong từng item

Mỗi `ImageItemViewModel` có command riêng (`MoveUpCommand`, `RemoveCommand`…). Nhưng item không biết vị trí của mình trong danh sách, nên nó nhận **callback** từ `MainViewModel` qua constructor và gọi ngược về đó. `MainViewModel` cập nhật `IsFirst`/`IsLast` cho mọi item sau mỗi thay đổi, nhờ đó "Lên 1" tự mờ ở ảnh đầu tiên.

---

## 6. Luồng (thread) và async trong WinUI

### 6.1. Quy tắc vàng

1. **Chỉ UI thread được chạm vào control** và các object WinUI như `BitmapImage`. Chạm từ luồng khác → exception `RPC_E_WRONG_THREAD` (0x8001010E).
2. **Không làm việc nặng trên UI thread**, nếu không cửa sổ sẽ "Not Responding".

Hai quy tắc này kéo theo mẫu code xuất hiện khắp app:

```csharp
// Đang ở UI thread
ThumbnailResult result = await Task.Run(() => thumbnailService.CreateThumbnail(...)); // việc nặng: luồng nền
// Sau await: tự động quay lại UI thread
Thumbnail = await CreateBitmapAsync(result.EncodedPng); // tạo BitmapImage: phải ở UI thread
```

### 6.2. Vì sao sau `await` lại về đúng UI thread?

UI thread có một `SynchronizationContext`. Khi `await` từ UI thread, phần code sau `await` được đẩy về context đó. Do đó trong ViewModel, sau `await`, gán property bình thường là an toàn.

Ngoại lệ: bên trong `Task.Run(...)` bạn **đang ở luồng nền**. Đừng gán property bind vào UI từ trong đó.

### 6.3. Báo tiến độ từ luồng nền: `Progress<T>`

```csharp
var progress = new Progress<double>(value => ExportProgress = value * 100); // tạo trên UI thread
await exporter.ExportAsync(..., progress, token);  // exporter gọi progress.Report() từ luồng nền
```

`Progress<T>` nhớ context lúc được tạo và chạy callback trên đó, nên `ExportProgress` luôn được gán trên UI thread.

### 6.4. DispatcherQueue

Khi đang ở luồng nền mà không có `await` hay `Progress<T>` để quay về, dùng:

```csharp
DispatcherQueue.TryEnqueue(() => StatusTitle = "Xong");
```

`DispatcherQueue` là hàng đợi công việc của UI thread. Phase 6 (ước tính dung lượng) có thể cần cái này.

### 6.5. Huỷ tác vụ: CancellationToken

- `CancellationTokenSource` (CTS) là "công tắc", `CancellationToken` là "dây" truyền vào tác vụ.
- Tác vụ tự kiểm tra định kỳ: `token.ThrowIfCancellationRequested()`.
- Gọi `cts.Cancel()` → lần kiểm tra kế tiếp ném `OperationCanceledException`.
- Xem `MainViewModel.ExportAsync` và `ExportCancelCommand`.

### 6.6. `async void`

Chỉ dùng `async void` cho **event handler** (`OnExportClick`, `OnFilesDrop`), vì chữ ký sự kiện bắt buộc trả `void`. Mọi chỗ khác dùng `async Task` để exception không bị mất.

---

## 7. Các control đang dùng trong app

| Control | Dùng làm gì | Ở đâu |
|---|---|---|
| `CommandBar` + `AppBarButton` | Thanh lệnh có icon + nhãn | MainPage, hàng trên cùng |
| `MenuFlyout` | Menu thả xuống / menu chuột phải | Nút "Sắp xếp", tay cầm của item |
| `Flyout` | Popup nhẹ chứa bất kỳ nội dung nào | Xác nhận "Xoá hết", ColorPicker |
| `ContextFlyout` | Flyout tự mở khi chuột phải | Tay cầm của item |
| `InfoBar` | Thông báo trong trang, không chặn thao tác | Kết quả xuất, file bị bỏ qua |
| `ProgressBar` / `ProgressRing` | Tiến độ có % / vòng xoay chờ | Đang xuất / đang tạo thumbnail |
| `ListView` | Danh sách cuộn, ảo hoá (chỉ tạo control cho dòng đang hiện) | Danh sách ảnh |
| `ListView.Footer` | Nội dung luôn nằm sau phần tử cuối | Nút "Thêm ảnh" cuối danh sách |
| `ContentDialog` | Hộp thoại modal | ExportDialog |
| `RadioButtons` | Chọn một trong vài lựa chọn | Định dạng PDF/PNG/WebP |
| `ComboBox` | Danh sách thả xuống | Mức nén |
| `Slider` | Kéo chọn số | Chất lượng |
| `NumberBox` | Ô nhập số, ô trống = `NaN` | Chiều rộng tối đa, khoảng cách |
| `ToggleSwitch` | Công tắc bật/tắt | Giảm màu PNG-8 |
| `ColorPicker` | Chọn màu | Màu nền |
| `TitleBar` + `MicaBackdrop` | Thanh tiêu đề tuỳ biến, nền mờ kiểu Windows 11 | MainWindow |

Một vài điểm cần nhớ:

- **`ContentDialog` cần `XamlRoot`** (`new ExportDialog(...) { XamlRoot = XamlRoot }`), nếu không sẽ lỗi vì không biết hiện trên cửa sổ nào. Mỗi lúc chỉ được mở một ContentDialog.
- **Kéo thả từ Explorer:** đặt `AllowDrop="True"` và một `Background` (kể cả `Transparent`) để vùng trống nhận được chuột. `MainPage` đăng ký `DragOver`/`Drop` với `handledEventsToo: true` vì `ListView` "nuốt" sự kiện kéo thả của nó.
- **Deferral** (`e.GetDeferral()`): khi event handler có `await`, deferral bảo hệ thống "chờ tôi xong đã".
- **File Picker** mới ở namespace `Microsoft.Windows.Storage.Pickers`, khởi tạo bằng `AppWindow.Id` (xem `Services/FilePickerService.cs`).

> Mẹo: cài app **WinUI 3 Gallery** từ Microsoft Store. Mỗi control đều có demo chạy được và code XAML để copy.

---

## 8. Magick.NET

### 8.1. Magick.NET là gì

Magick.NET là thư viện .NET bọc **ImageMagick** (bộ xử lý ảnh viết bằng C). Đọc/ghi hơn 100 định dạng (PNG, JPEG, WebP, HEIC…), resize, xoay, ghép, đổi màu.

- Gói **`Magick.NET-Q8-AnyCPU`**: **Q8** = mỗi kênh màu 8 bit (đủ cho ảnh thường, tốn RAM bằng một nửa Q16). **AnyCPU** = có sẵn bản native cho x64 và ARM64.
- Namespace: `using ImageMagick;`

### 8.2. Các kiểu chính

| Kiểu | Là gì | Ví dụ trong app |
|---|---|---|
| `MagickImage` | Một ảnh đã decode, pixel nằm trong RAM | Mọi exporter |
| `MagickImageInfo` | Chỉ đọc header (kích thước, orientation), **không decode pixel**, rất nhanh | `Services/ImageHeader.cs` |
| `MagickGeometry` | Kích thước + cờ cách resize | `ThumbnailService`, `VerticalMergeExporter` |
| `MagickReadSettings` | Tuỳ chọn khi đọc | `ThumbnailService` (đọc JPEG ở độ phân giải thấp) |
| `MagickColor` / `MagickColors` | Màu | Nền canvas, `MagickColors.White` |
| `MagickFormat` | Enum định dạng | `MagickFormat.Png`, `MagickFormat.Png8`, `MagickFormat.Jpeg` |
| `XxxWriteDefines` / `XxxReadDefines` | Tuỳ chọn riêng từng định dạng | `WebPWriteDefines { Lossless = true }`, `JpegReadDefines` |
| `MagickImageCollection` | Nhiều ảnh (trang PDF, khung GIF) | *Cố ý không dùng*, xem 8.6 |

### 8.3. Quy tắc số 1: luôn `using`

`MagickImage` giữ **bộ nhớ native** (ngoài tầm của garbage collector). Một ảnh 4000×3000 tốn khoảng 48 MB. Không Dispose thì RAM tăng vọt rồi mới được thu hồi rất muộn.

```csharp
using var image = new MagickImage(path);   // Dispose tự động khi ra khỏi scope
```

Hàm trả về một `MagickImage` cho người gọi (như `ExportImageLoader.Load`) thì người gọi chịu trách nhiệm `using`. Nếu lỗi giữa chừng trong hàm, phải tự Dispose trước khi ném lỗi (xem `catch` trong `ExportImageLoader.Load`).

### 8.4. Pipeline cơ bản: đọc → xử lý → ghi

```csharp
using var image = new MagickImage("input.jpg");   // 1. đọc + decode
image.AutoOrient();                               // 2. xoay đúng theo EXIF
image.Resize(1280, 0);                            // 3. rộng 1280, cao 0 = tự tính theo tỉ lệ
image.Strip();                                    // 4. bỏ EXIF/metadata cho file nhẹ hơn
image.Quality = 80;                               // 5. chất lượng nén
image.Write("output.webp");                       // 6. encode, định dạng theo đuôi file
// hoặc: byte[] bytes = image.ToByteArray(MagickFormat.Png);   // encode vào bộ nhớ
```

### 8.5. Các thao tác app đang dùng

**`AutoOrient()`**: Điện thoại thường lưu ảnh chụp dọc **nằm ngang** kèm cờ EXIF "hãy xoay 90°". Không gọi `AutoOrient()` thì ảnh bị nằm ngang. Luôn gọi ngay sau khi đọc. Vì lý do này `ImageHeader.ReadOrientedSize` phải đổi chỗ rộng/cao khi orientation là xoay 90°.

**Resize và `MagickGeometry`**:

```csharp
image.Resize(1280, 0);                                            // giữ tỉ lệ, cao tự tính
image.Thumbnail(new MagickGeometry(240, 240) { Greater = true }); // vừa khung 240×240, chỉ thu nhỏ
image.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true }); // ép đúng w×h
```

- `Greater = true`: chỉ resize nếu ảnh **lớn hơn** khung (không phóng to ảnh nhỏ).
- `IgnoreAspectRatio = true`: ép đúng kích thước. `VerticalMergeExporter` dùng cái này vì đã tự tính chiều cao, tránh lệch 1 px do làm tròn.
- `Thumbnail()` giống `Resize()` nhưng bỏ luôn metadata, nhanh hơn cho ảnh nhỏ.

**Đọc nhanh JPEG lớn** (`ThumbnailService`):

```csharp
var settings = new MagickReadSettings();
settings.SetDefines(new JpegReadDefines { Size = new MagickGeometry(480, 480) });
using var image = new MagickImage(path, settings);
```

Báo trước cho bộ giải mã JPEG kích thước cần dùng, nó sẽ decode ở 1/2, 1/4 hoặc 1/8 độ phân giải: nhanh hơn và ít RAM hơn nhiều.

**Màu và kênh alpha** (`PdfExporter`):

```csharp
if (image.HasAlpha)                       // JPEG không có trong suốt
{
    image.BackgroundColor = MagickColors.White;
    image.Alpha(AlphaAction.Remove);      // phủ nền trắng thay vì để thành đen
}
if (image.ColorSpace is not ColorSpace.sRGB and not ColorSpace.Gray)
    image.ColorSpace = ColorSpace.sRGB;   // ví dụ ảnh CMYK
```

**Ghép ảnh** (`VerticalMergeExporter`):

```csharp
using var canvas = new MagickImage(backgroundColor, width, totalHeight); // ảnh trống tô màu nền
canvas.Composite(image, 0, y, CompositeOperator.Over);                   // dán image vào vị trí (0, y)
```

**Giảm màu PNG-8** (`PngExporter`):

```csharp
canvas.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
canvas.Write(path, MagickFormat.Png8);
```

**Ghi WebP với tuỳ chọn riêng** (`WebpExporter`):

```csharp
canvas.Quality = 80;
canvas.Write(path, new WebPWriteDefines { Lossless = false });
```

### 8.6. Quản lý RAM

- **`ResourceLimits.LimitMemory(new Percentage(25))`** (`Services/MagickSetup.cs`): khi vượt 25% RAM, ImageMagick chuyển sang lưu tạm trên đĩa thay vì làm treo máy. Gọi một lần lúc app khởi động.
- **Xử lý từng ảnh một:** đọc → dùng → Dispose → ảnh tiếp theo. Đó là lý do app *không* dùng `MagickImageCollection.AppendVertically()` hay PDF writer của Magick: cả hai cần mọi ảnh nằm trong RAM cùng lúc. Thay vào đó:
  - PNG/WebP: tạo canvas đúng kích thước cuối rồi dán từng ảnh.
  - PDF: nén từng ảnh thành JPEG rồi ghi thẳng vào file (`JpegPdfWriter`).
- **Đọc header trước** (`MagickImageInfo`) để tính bố cục và kiểm tra giới hạn (WebP ≤ 16383 px) trước khi làm việc nặng.

### 8.7. Quality nghĩa là gì với từng định dạng

| Định dạng | `Quality` |
|---|---|
| JPEG (trong PDF) | 1–100, càng cao càng đẹp và càng nặng. 85 là mức cân bằng phổ biến |
| WebP | 1–100 khi lossy. Đặt `Lossless = true` thì quality chỉ còn ảnh hưởng tốc độ nén |
| PNG | Lossless, không có "chất lượng". Muốn nhỏ hơn: giảm kích thước hoặc giảm màu (PNG-8) |

### 8.8. Magick.NET và threading

- Mọi thao tác Magick là **đồng bộ và tốn CPU** → luôn gọi trong `Task.Run` (Service tự làm, xem `PdfExporter.ExportAsync`).
- Một `MagickImage` không nên dùng đồng thời từ nhiều luồng. Mỗi luồng tạo ảnh riêng là an toàn.
- Lỗi đọc file ném `MagickException` (hoặc lớp con như `MagickCorruptImageErrorException`). App đổi nó thành message tiếng Việt có tên file.

---

## 9. MVVM: vì sao có tầng ViewModel

**MVVM** = Model, View, ViewModel. ViewModel là "mô hình của màn hình": nó giữ mọi thứ màn hình cần hiển thị (danh sách, cờ đang xuất, thông báo lỗi) dưới dạng property thuần C#, còn View chỉ việc bind vào.

Lợi ích cụ thể trong ImageMerger:

- **Test được:** `ExportViewModel.ValidationMessage` có thể unit test mà không cần mở cửa sổ. Logic nằm trong code-behind thì gần như không test được.
- **Không bị "lẫn":** đổi giao diện (ví dụ thay ListView bằng GridView) không cần sửa logic sắp xếp, xuất file.
- **Phase 6** (ước tính dung lượng có debounce + huỷ) là logic phức tạp. Đặt trong ViewModel thì đọc và test dễ hơn nhiều so với nhét vào `MainPage.xaml.cs`.

**Khi nào không cần ViewModel?** Màn hình rất nhỏ, gần như không có logic (một hộp thoại "Giới thiệu"): code-behind là đủ. Một số team cũng chọn mô hình View + Model (code-behind làm luôn phần ViewModel) cho app nhỏ. Đó là đánh đổi chứ không sai tuyệt đối. Khi làm dự án của công ty, theo quy ước của team. Những khái niệm trong tài liệu này (binding, `INotifyPropertyChanged`, command, threading) vẫn y nguyên, chỉ khác chỗ đặt code.

---

## 10. Bảng tra: CommunityToolkit.Mvvm và code tự viết

Dự án này **không dùng** CommunityToolkit.Mvvm (theo yêu cầu tech lead). Nhưng phần lớn ví dụ trên mạng dùng nó, nên đây là bảng "dịch":

| CommunityToolkit.Mvvm | Code tự viết tương đương trong app |
|---|---|
| `: ObservableObject` | `: BindableBase` (`Helpers/BindableBase.cs`) |
| `[ObservableProperty] public partial string Name { get; set; }` | field `_name` + property có `set => SetProperty(ref _name, value);` |
| `[NotifyPropertyChangedFor(nameof(B))]` trên A | Trong setter của A: `if (SetProperty(...)) OnPropertyChanged(nameof(B));` |
| `[RelayCommand] void Save()` → `SaveCommand` | `SaveCommand = new RelayCommand(Save);` trong constructor |
| `[RelayCommand(CanExecute = nameof(CanSave))]` | `new RelayCommand(Save, () => CanSave)` |
| `[NotifyCanExecuteChangedFor(nameof(SaveCommand))]` | Trong setter: `SaveCommand.NotifyCanExecuteChanged();` |
| `[RelayCommand] async Task LoadAsync()` → `LoadCommand` | `new AsyncRelayCommand(LoadAsync)` |
| `IncludeCancelCommand = true` → `LoadCancelCommand` | Tự giữ `CancellationTokenSource`, command Huỷ gọi `_cts.Cancel()` (xem `ExportCancelCommand`) |

Toolkit chỉ là **source generator**: lúc build nó sinh ra đúng những dòng code ở cột phải. Hiểu cột phải là hiểu toolkit.

---

## 11. Lỗi hay gặp

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| UI hiện giá trị đầu rồi không đổi nữa | Quên `Mode=OneWay` trên `x:Bind` |
| Vẫn không đổi dù đã có `OneWay` | Setter không gọi `SetProperty`/`OnPropertyChanged`, hoặc là property tính toán chưa được báo (4.4) |
| Nhập vào ô nhưng VM không nhận | Quên `Mode=TwoWay` |
| Nút/menu mờ mãi hoặc sáng mãi | Quên `NotifyCanExecuteChanged()` |
| Thêm phần tử nhưng ListView không hiện | Dùng `List<T>` thay vì `ObservableCollection<T>` |
| Exception `0x8001010E` / `RPC_E_WRONG_THREAD` | Chạm UI/`BitmapImage` từ luồng nền (6.1) |
| App "Not Responding" | Làm việc Magick trên UI thread, quên `Task.Run` |
| `ContentDialog` lỗi khi `ShowAsync` | Thiếu `XamlRoot`, hoặc đang mở một dialog khác |
| RAM tăng mãi | Quên `using` với `MagickImage` |
| Ảnh điện thoại bị nằm ngang | Quên `AutoOrient()` |
| Lỗi build `WMC1110` / không tìm thấy property | Sai tên trong `x:Bind`, hoặc thiếu `x:DataType` trong `DataTemplate` |

---

## 12. Bài tập nhỏ để tự luyện

Làm trên một branch riêng, mỗi bài chỉ vài dòng:

1. **Binding OneWay:** thêm một `TextBlock` trên `CommandBar` hiển thị "12 ảnh". Gợi ý: property `ItemCountText` trong `MainViewModel`, nhớ báo nó trong `OnItemsChanged`. Thử bỏ `Mode=OneWay` để thấy nó "đứng hình".
2. **Command có CanExecute:** thêm lệnh "Đảo ngược thứ tự" vào menu Sắp xếp, chỉ bật khi có từ 2 ảnh trở lên.
3. **TwoWay:** thêm `ToggleSwitch` "Chuyển sang ảnh xám" vào ExportDialog, bind vào một bool mới trong `ExportViewModel`.
4. **Magick.NET:** dùng bool ở bài 3, trong `ExportImageLoader.Load` gọi `image.Grayscale()` khi được bật (cần thêm field vào `ExportOptions`).
5. **Đọc code sinh ra:** build, mở `obj/x64/Debug/.../Views/MainPage.g.cs`, tìm đoạn code mà `x:Bind ViewModel.StatusTitle` sinh ra. Bạn sẽ thấy nó đăng ký `PropertyChanged` như mô tả ở phần 4.3.

---

## Tài liệu tham khảo

- WinUI 3 trên Microsoft Learn: <https://learn.microsoft.com/windows/apps/winui/winui3/>
- x:Bind markup extension: <https://learn.microsoft.com/windows/uwp/xaml-platform/x-bind-markup-extension>
- Data binding in depth: <https://learn.microsoft.com/windows/uwp/data-binding/data-binding-in-depth>
- Threading và DispatcherQueue: <https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.dispatching.dispatcherqueue>
- Magick.NET (GitHub, có thư mục `docs/` với nhiều ví dụ): <https://github.com/dlemstra/Magick.NET>
- App **WinUI 3 Gallery** (Microsoft Store): demo trực quan mọi control.
