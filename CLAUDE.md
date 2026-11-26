# CLAUDE.md — Minh Chứng Ơi (tên trong code: ImageMerger)

> File này là bối cảnh chung của dự án dành cho Claude (và cho chính tôi).
> Kế hoạch triển khai chi tiết theo từng phase nằm ở **`BUILD_PLAN.md`**.

---

## 1. Mục đích dự án

**Minh Chứng Ơi** là ứng dụng desktop Windows viết bằng **WinUI 3**. Ứng dụng gộp nhiều ảnh (và các trang PDF) thành **một file PDF duy nhất**, mỗi ảnh một trang, với **dung lượng nằm trong giới hạn của cổng nộp** (xem mục 3).

- **Người dùng mục tiêu:** người trung niên, nhân viên văn phòng ở Việt Nam cần nộp minh chứng và hồ sơ lên các cổng thông tin.
- **Miễn phí, không kiếm tiền.** Mục tiêu là hỗ trợ người dùng. Khi phải chọn, ưu tiên **sự thuận tiện cho người dùng** hơn là thêm tính năng.
- **Tên:**
  - Tên hiển thị cho người dùng: **Minh Chứng Ơi** (đã chốt).
  - Tên trong code (namespace, repo, project) dùng **tiếng Anh**, hiện tại vẫn là `ImageMerger`. Sẽ đổi sang tên tiếng Anh khác, xem `BUILD_PLAN.md` Phase R.
- **Tagline (chưa chốt, đang cân nhắc):**
  - "Nộp là lọt, không lo quá dung lượng."
  - "Gộp giấy tờ thành một file PDF, vừa đúng giới hạn cổng nộp."
  - "Cổng giới hạn 5 MB? Có ngay file vừa đủ."
- **Lưu ý khi đặt tên và viết nội dung:** không dùng từ pháp lý ("chứng thực", "công chứng", "sao y"), không làm giống app hoặc cổng của nhà nước. Ghi rõ đây là công cụ miễn phí, không phải dịch vụ của cơ quan nhà nước.

Đây đồng thời là **dự án học WinUI 3** của tôi, nên:
- Ưu tiên code **dễ đọc, đúng chuẩn MVVM** hơn là code ngắn hoặc "thông minh".
- Mỗi lần dùng một control, API hoặc khái niệm WinUI mới, giải thích ngắn 1–2 câu: nó là gì và vì sao dùng nó.

---

## 2. Mục tiêu (Goals)

1. Thêm ảnh qua **File Picker** (chọn nhiều file) và **kéo thả từ Explorer**.
2. Hiển thị danh sách ảnh dạng thumbnail. Người dùng **sắp xếp lại** (kéo thả hoặc menu Lên/Xuống) và **xoá** từng ảnh.
3. Nút **"Thêm ảnh" luôn nằm cuối danh sách** (`ListView.Footer`), để người dùng thêm tiếp liên tục.
4. **Xuất PDF**: mỗi ảnh là một trang.
5. **File xuất ra nằm trong giới hạn dung lượng của cổng nộp**: app tự chọn mức nén, người dùng chỉ cần chọn giới hạn. Đây là selling point, xem mục 3.

### Không làm ở giai đoạn đầu (Non-goals)
- Chỉnh sửa ảnh (crop, filter, vẽ), OCR.
- Ghép dạng lưới hoặc bố cục tự do.
- Xuất ảnh dài PNG/WebP. Đã làm ở Phase 4–5 rồi gỡ để tập trung vào PDF (code cũ ở git tag `before-pdf-only`). PNG/WebP là định dạng một ảnh, ghép dài thì gặp giới hạn 16383 px của WebP và tốn RAM. Nếu cần làm lại (ví dụ gửi qua ứng dụng chat), cân nhắc dùng JPEG.
- Đồng bộ cloud, đa nền tảng (macOS/Linux).

---

## 3. Selling point: file PDF vừa giới hạn của cổng nộp

Các cổng nộp minh chứng và hồ sơ thường giới hạn dung lượng file (2, 5, 10, 20 MB...). Phần lớn công cụ gộp ảnh chỉ có nút "Xuất": người dùng không biết file ra nặng bao nhiêu, nộp lên bị từ chối rồi phải làm lại. Người trung niên lại thường không hiểu "quality 85" là gì.

### 3.0. Dung lượng mục tiêu (tính năng chính)

- Người dùng chỉ chọn **giới hạn dung lượng**. Mặc định **≤ 20 MB**, có thể chỉnh.
- Khi thêm, xoá ảnh hoặc đổi giới hạn, app **tự chọn mức nén** sao cho file ≤ giới hạn mà **chất lượng cao nhất có thể**. Nếu không cần nén thì không nén.
- File xuất ra **không bao giờ vượt giới hạn mà không báo** người dùng.
- Các preset và slider ở mục 3.1 chuyển thành **tuỳ chọn nâng cao**.
- Chi tiết thiết kế xem `BUILD_PLAN.md` Phase T.

### 3.1. Mức nén (presets, tuỳ chọn nâng cao)

| Preset | Quality JPEG (bên trong PDF) | Chiều rộng tối đa |
|---|---|---|
| **Gốc** | 95 | Giữ nguyên |
| **Cân bằng** *(mặc định)* | 85 | 2048 px |
| **Nhỏ gọn** | 70 | 1280 px |
| **Tuỳ chỉnh** | Slider 1–100 | NumberBox |

### 3.2. Dự đoán dung lượng (size estimation)

**Thuật toán (bản 1, lấy mẫu):**
1. Với mỗi ảnh, tạo một **bản mẫu thu nhỏ** (cạnh dài khoảng 512 px) và cache lại.
2. Encode bản mẫu thành JPEG vào `MemoryStream` với **đúng quality** đang chọn, để đo **số byte trên mỗi pixel** (bytes per pixel).
3. Ước tính:
   `dung lượng ≈ Σ (bytesPerPixel_i × số pixel đầu ra_i) × hệ số hiệu chỉnh + overhead`
   - Overhead của PDF khoảng vài KB mỗi trang.
4. Chạy trên luồng nền, **debounce khoảng 300 ms** khi kéo slider, và **huỷ lần tính trước** bằng `CancellationToken`.
5. Hiển thị dạng `≈ 4.2 MB`. Sau khi xuất xong, hiển thị **dung lượng thật** bên cạnh để so sánh. Ghi lại sai số để tinh chỉnh hệ số hiệu chỉnh.

**Lưu ý:** ảnh thu nhỏ thường có nhiều chi tiết trên mỗi pixel hơn ảnh gốc, nên tỉ lệ bytes per pixel đo trên mẫu thường **cao hơn thực tế**. Hệ số hiệu chỉnh tồn tại để bù phần này. Hệ số được xác định bằng thực nghiệm và có thể khác nhau theo từng mức quality.

**Chỉ tiêu:**
- Sai số **≤ ±20%**.
- Thời gian ước tính **< 1 giây** với 20 ảnh điện thoại.
- UI không bao giờ bị đơ khi đang ước tính.

**Mở rộng sau (stretch):**
- Chế độ **"Chính xác"**: encode toàn bộ vào bộ nhớ khi tổng số pixel nhỏ.
- Xem trước trước/sau khi nén (phóng to một vùng ảnh) để người dùng thấy chất lượng.

---

## 4. Tech stack (đã chốt)

| Thành phần | Lựa chọn |
|---|---|
| Runtime | **.NET 10** |
| UI | **WinUI 3**, Windows App SDK 1.8+ (Packaged app) |
| MVVM | Tự viết, **không dùng CommunityToolkit.Mvvm** (theo yêu cầu tech lead): `Helpers/BindableBase.cs` (cài `INotifyPropertyChanged`), `Helpers/RelayCommand.cs` (`RelayCommand`, `AsyncRelayCommand`) |
| Xử lý ảnh | **Magick.NET** (gói `Magick.NET-Q8-AnyCPU`): đọc PNG/JPG/WebP/HEIC, xoay theo EXIF, resize, nén JPEG để ghi vào PDF |
| Đọc PDF (đầu vào) | `Windows.Data.Pdf.PdfDocument` (có sẵn trong Windows): chỉ để render thumbnail. *Magick.NET đọc PDF cần Ghostscript nên không dùng cho việc này.* |
| Ghi PDF (đầu ra) | **PdfPig** (Apache 2.0): chép nguyên trang từ PDF đầu vào và nhúng ảnh JPEG. Chỉ gọi `Dispose()`, không gọi `Build()` (xem `PdfExporter`). |
| File Picker | `Microsoft.Windows.Storage.Pickers` (`FileOpenPicker`, `FileSavePicker`) với `AppWindow.Id`. Không dùng `InitializeWithWindow` kiểu cũ. |

Target framework:
- App WinUI (`ImageMerger`): `net10.0-windows10.0.19041.0`
- Thư viện nghiệp vụ (`ImageMerger.Core`): `net10.0` thuần. Magick.NET và PdfPig được khai báo ở đây, app nhận lại qua project reference.

**Không thêm thư viện ngoài danh sách trên khi chưa hỏi tôi.**

---

## 5. Định dạng hỗ trợ

- **Đầu vào:** `.png`, `.jpg`, `.jpeg`, `.webp`, `.heic`, `.heif`, `.pdf` (mỗi trang PDF trở thành một item riêng).
- **Đầu ra:** `.pdf`.
- **Giai đoạn MVP**
	- Đầu vào: `.png`
	- Đầu ra: `.pdf`

---

## 6. Kiến trúc và cấu trúc thư mục

```
ImageMerger/                         // thư mục solution, chứa ImageMerger.slnx
├─ ImageMerger.Core/                 // net10.0 thuần: nghiệp vụ dùng chung, không phụ thuộc Windows
│   ├─ Models/
│   │   ├─ ImageSourceInfo.cs        // Path, PageIndex?, PixelWidth, PixelHeight
│   │   ├─ ExportOptions.cs          // Preset, Quality, MaxWidth
│   │   ├─ CompressionPreset.cs
│   │   └─ PresetSettings.cs
│   ├─ Services/
│   │   ├─ ThumbnailService.cs       // thumbnail ảnh bằng Magick.NET, khoảng 240 px
│   │   ├─ ImageHeader.cs, MagickSetup.cs, SupportedFormats.cs
│   │   ├─ Export/
│   │   │   └─ PdfExporter.cs        // PdfPig: ảnh nén JPEG một trang, PDF đầu vào chép nguyên trang
│   │   └─ Compression/
│   │       ├─ ISizeEstimator.cs
│   │       └─ SampleSizeEstimator.cs
│   └─ Helpers/
│       ├─ NaturalStringComparer.cs  // sắp xếp "img2" trước "img10"
│       ├─ PageRangeParser.cs
│       └─ FileSizeFormatter.cs
└─ ImageMerger/                      // app WinUI, tham chiếu ImageMerger.Core
    ├─ ViewModels/
    │   ├─ MainViewModel.cs          // ObservableCollection<ImageItemViewModel>, các command
    │   ├─ ImageItemViewModel.cs     // Thumbnail, DisplayName, Move/Remove commands
    │   └─ ExportViewModel.cs        // tuỳ chọn xuất + EstimatedSize
    ├─ Services/
    │   ├─ FilePickerService.cs      // Microsoft.Windows.Storage.Pickers
    │   └─ PdfThumbnailService.cs    // thumbnail PDF bằng Windows.Data.Pdf (chỉ có trên Windows)
    ├─ Helpers/
    │   ├─ BindableBase.cs, RelayCommand.cs  // MVVM tự viết
    │   └─ XamlFunctions.cs
    └─ Views/
        └─ MainPage.xaml             // tuỳ chọn xuất nằm ở thanh đáy (đã gỡ ExportDialog)
```

Namespace giữ như cũ (`ImageMerger.Models`, `ImageMerger.Services...`, `ImageMerger.Helpers`) dù file nằm ở project nào.

**Nguyên tắc:**
- View chỉ binding vào ViewModel, **không gọi Service trực tiếp từ code-behind**. Ngoại lệ: các việc thuần UI như mở dialog.
- Service không tham chiếu tới kiểu dữ liệu của WinUI, để có thể test bằng unit test. Code nghiệp vụ mới đặt trong **`ImageMerger.Core`**. Chỉ code cần API Windows (picker, `Windows.Data.Pdf`...) mới đặt ở project app.
- **UI chỉ giữ thumbnail.** Ảnh gốc chỉ được đọc khi xuất hoặc ước tính, xử lý **lần lượt từng ảnh** và `Dispose` ngay sau khi dùng.

---

## 7. Quy ước code

- Bật `Nullable`, dùng file-scoped namespace, `async`/`await` từ đầu đến cuối (không `.Result`, không `.Wait()`).
- Dùng `x:Bind` thay cho `Binding` (nhanh hơn và được kiểm tra lúc compile). Nhớ `Mode=OneWay` khi giá trị thay đổi.
- Việc nặng (decode, encode, ghép) chạy trong `Task.Run` và nhận `CancellationToken`.
- Cập nhật UI từ luồng nền qua `DispatcherQueue.TryEnqueue`.
- Mọi đối tượng `MagickImage` / `MagickImageCollection` đều dùng `using`.
- Đặt tên: class/method dùng `PascalCase`, field private dùng `_camelCase`, file XAML trùng tên class.
- Chuỗi hiển thị cho người dùng viết bằng tiếng Việt. Có thể chuyển sang `.resw` sau.

---

## 8. Ràng buộc kỹ thuật đã biết

| Vấn đề | Cách xử lý |
|---|---|
| RAM khi xử lý nhiều ảnh lớn | Xử lý tuần tự từng ảnh và `Dispose` ngay. Có tuỳ chọn chiều rộng tối đa. Đặt `ResourceLimits` cho Magick.NET. |
| Trang PDF quá lớn (Acrobat không mở trang > 200 inch) | `PdfExporter` thu nhỏ kích thước trang về tối đa 14400 pt. |
| Ảnh điện thoại bị xoay sai | Luôn gọi `AutoOrient()` khi load. |
| Picker trả file **không theo thứ tự click** | Sau khi chọn, sắp xếp natural sort theo tên. Người dùng tự kéo để chỉnh lại. |
| HEIC | Magick.NET tự đọc được, không phụ thuộc codec của Windows. |

---

## 9. Build và chạy

```bash
# chạy trong thư mục ImageMerger/ (nơi có ImageMerger.slnx), build cả app và ImageMerger.Core
dotnet restore
dotnet build ImageMerger.slnx -c Debug -p:Platform=x64
```

Chạy và debug bằng **Visual Studio** (bản hỗ trợ .NET 10, đã cài workload *WinUI application development*), profile **Packaged**.

---

## 10. Hướng dẫn làm việc cho Claude

1. **Đọc `BUILD_PLAN.md` trước** để biết đang ở phase nào. Làm đúng phase được yêu cầu, không làm trước các phase sau.
2. Mỗi phase xong phải **build được và chạy được**. Đánh dấu `[x]` vào các task đã xong trong `BUILD_PLAN.md`.
3. Khi giới thiệu control hoặc khái niệm WinUI mới, giải thích ngắn gọn (tôi đang học).
4. Trước khi thay đổi kiến trúc hoặc thêm package, **hỏi tôi trước**.
5. Thay đổi nhỏ và có thể review được. Không viết lại cả file khi chỉ cần sửa vài dòng.
