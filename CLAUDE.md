# CLAUDE.md — ImageMerger

> File này là bối cảnh chung của dự án dành cho Claude (và cho chính tôi).
> Kế hoạch triển khai chi tiết theo từng phase nằm ở **`BUILD_PLAN.md`**.

---

## 1. Mục đích dự án

**ImageMerger** là ứng dụng desktop Windows viết bằng **WinUI 3**. Ứng dụng gộp nhiều ảnh (và các trang PDF) thành **một file PDF duy nhất**, mỗi ảnh một trang. Người dùng có thể **chọn mức nén** và **xem trước dung lượng file đầu ra** trước khi xuất.

Đây đồng thời là **dự án học WinUI 3** của tôi, nên:
- Ưu tiên code **dễ đọc, đúng chuẩn MVVM** hơn là code ngắn hoặc "thông minh".
- Mỗi lần dùng một control, API hoặc khái niệm WinUI mới, giải thích ngắn 1–2 câu: nó là gì và vì sao dùng nó.

---

## 2. Mục tiêu (Goals)

1. Thêm ảnh qua **File Picker** (chọn nhiều file) và **kéo thả từ Explorer**.
2. Hiển thị danh sách ảnh dạng thumbnail. Người dùng **sắp xếp lại** (kéo thả hoặc menu Lên/Xuống) và **xoá** từng ảnh.
3. Nút **"Thêm ảnh" luôn nằm cuối danh sách** (`ListView.Footer`), để người dùng thêm tiếp liên tục.
4. **Xuất PDF**: mỗi ảnh là một trang.
5. **Chọn mức nén và dự đoán dung lượng**. Đây là selling point, xem mục 3.

### Không làm ở giai đoạn đầu (Non-goals)
- Chỉnh sửa ảnh (crop, filter, vẽ), OCR.
- Ghép dạng lưới hoặc bố cục tự do.
- Xuất ảnh dài PNG/WebP. Đã làm ở Phase 4–5 rồi gỡ để tập trung vào PDF (code cũ ở git tag `before-pdf-only`). PNG/WebP là định dạng một ảnh, ghép dài thì gặp giới hạn 16383 px của WebP và tốn RAM. Nếu cần làm lại (ví dụ gửi qua ứng dụng chat), cân nhắc dùng JPEG.
- Đồng bộ cloud, đa nền tảng (macOS/Linux).

---

## 3. Selling point: chọn mức nén và dự đoán dung lượng

Phần lớn công cụ gộp ảnh chỉ có nút "Xuất". Người dùng không biết file ra sẽ nặng bao nhiêu, phải xuất thử rồi xuất lại. ImageMerger cho phép **chọn mức nén và thấy ngay dung lượng ước tính** trước khi xuất.

### 3.1. Mức nén (presets)

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
- Chế độ **dung lượng mục tiêu**, ví dụ "File phải ≤ 5 MB": app tự tìm quality phù hợp bằng binary search trên bản mẫu.
- Xem trước trước/sau khi nén (phóng to một vùng ảnh) để người dùng thấy chất lượng.

---

## 4. Tech stack (đã chốt)

| Thành phần | Lựa chọn |
|---|---|
| Runtime | **.NET 10** |
| UI | **WinUI 3**, Windows App SDK 1.8+ (Packaged app) |
| MVVM | Tự viết, **không dùng CommunityToolkit.Mvvm** (theo yêu cầu tech lead): `Helpers/BindableBase.cs` (cài `INotifyPropertyChanged`), `Helpers/RelayCommand.cs` (`RelayCommand`, `AsyncRelayCommand`) |
| Xử lý ảnh | **Magick.NET** (gói `Magick.NET-Q8-AnyCPU`): đọc PNG/JPG/WebP/HEIC, xoay theo EXIF, resize, nén JPEG để ghi vào PDF |
| Đọc PDF (đầu vào) | `Windows.Data.Pdf.PdfDocument` (có sẵn trong Windows), render từng trang thành ảnh. *Magick.NET đọc PDF cần Ghostscript nên không dùng cho việc này.* |
| File Picker | `Microsoft.Windows.Storage.Pickers` (`FileOpenPicker`, `FileSavePicker`) với `AppWindow.Id`. Không dùng `InitializeWithWindow` kiểu cũ. |

Target framework: `net10.0-windows10.0.19041.0`

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
ImageMerger/
├─ Models/
│   ├─ ImageSourceInfo.cs        // Path, PageIndex?, PixelWidth, PixelHeight
│   ├─ ExportOptions.cs          // Preset, Quality, MaxWidth
│   └─ CompressionPreset.cs
├─ ViewModels/
│   ├─ MainViewModel.cs          // ObservableCollection<ImageItemViewModel>, các command
│   ├─ ImageItemViewModel.cs     // Thumbnail, DisplayName, Move/Remove commands
│   └─ ExportViewModel.cs        // tuỳ chọn xuất + EstimatedSize
├─ Services/
│   ├─ ImageLoaderService.cs     // file → danh sách ImageSourceInfo (PDF tách trang)
│   ├─ ThumbnailService.cs       // tạo thumbnail khoảng 240 px
│   ├─ Export/
│   │   ├─ PdfExporter.cs        // mỗi ảnh nén JPEG, một trang
│   │   └─ JpegPdfWriter.cs      // ghi thẳng JPEG vào file PDF
│   └─ Compression/
│       ├─ ISizeEstimator.cs
│       └─ SampleSizeEstimator.cs
├─ Helpers/
│   └─ NaturalStringComparer.cs  // sắp xếp "img2" trước "img10"
└─ Views/
    ├─ MainPage.xaml
    └─ ExportDialog.xaml
```

**Nguyên tắc:**
- View chỉ binding vào ViewModel, **không gọi Service trực tiếp từ code-behind**. Ngoại lệ: các việc thuần UI như mở dialog.
- Service không tham chiếu tới kiểu dữ liệu của WinUI, để có thể test bằng unit test.
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
dotnet restore
dotnet build -c Debug -p:Platform=x64
```

Chạy và debug bằng **Visual Studio** (bản hỗ trợ .NET 10, đã cài workload *WinUI application development*), profile **Packaged**.

---

## 10. Hướng dẫn làm việc cho Claude

1. **Đọc `BUILD_PLAN.md` trước** để biết đang ở phase nào. Làm đúng phase được yêu cầu, không làm trước các phase sau.
2. Mỗi phase xong phải **build được và chạy được**. Đánh dấu `[x]` vào các task đã xong trong `BUILD_PLAN.md`.
3. Khi giới thiệu control hoặc khái niệm WinUI mới, giải thích ngắn gọn (tôi đang học).
4. Trước khi thay đổi kiến trúc hoặc thêm package, **hỏi tôi trước**.
5. Thay đổi nhỏ và có thể review được. Không viết lại cả file khi chỉ cần sửa vài dòng.
