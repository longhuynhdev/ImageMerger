
Kế hoạch build dự án từng phần. Mỗi phase kết thúc ở trạng thái **build được, chạy được, dùng thử được**.
Bối cảnh chung của dự án nằm trong `CLAUDE.md`.

**Cách dùng với Claude Code:** gõ prompt dạng
`Làm Phase 1 theo BUILD_PLAN.md. Giải thích các control mới bạn dùng.`

| Phase | Nội dung | Ước tính | Mốc |
|---|---|---|---|
| 0 | Khởi tạo project | 2–4h | |
| 1 | Màn hình trống, File Picker, danh sách thumbnail | 6–10h | |
| 2 | Sắp xếp, xoá, nút Thêm ở cuối, kéo thả từ Explorer | 4–8h | |
| 3 | Xuất PDF | 4–6h | **MVP** |
| 4 | Xuất PNG/WebP ghép dọc và hộp thoại Export | 6–10h | |
| 5 | Mức nén (presets) | 4–6h | |
| 6 | Dự đoán dung lượng | 8–12h | **Selling point** |
| 7 | Định dạng khó: HEIC, PDF đầu vào | 6–10h | |
| 8 | Trau chuốt và đóng gói | 8–15h | **v1.0** |
| — | **Tổng** | **khoảng 48–81h** | |

---

## Phase 0 — Khởi tạo project

**Học được:** cấu trúc project WinUI 3, `App.xaml`, `MainWindow`, NuGet.

- [x] Tạo project bằng template **WinUI Blank App (Packaged)**, đặt tên `ImageMerger`.
- [x] Đổi `TargetFramework` thành `net10.0-windows10.0.19041.0`, bật `<Nullable>enable</Nullable>`.
- [x] Cập nhật Windows App SDK lên **1.8+**.
- [x] Cài package: `CommunityToolkit.Mvvm`, `Magick.NET-Q8-AnyCPU`.
- [x] Tạo các thư mục `Models/`, `ViewModels/`, `Services/`, `Helpers/`, `Views/`.
- [x] Tạo `MainPage.xaml` và cho `MainWindow` hiển thị trang này qua `Frame`.
- [x] Tạo `MainViewModel` rỗng và gắn vào `MainPage` (property `ViewModel`).
- [x] Khởi tạo git, thêm `.gitignore` cho Visual Studio.

**Xong khi:** app chạy lên một cửa sổ trống, build không có warning nghiêm trọng.

---

## Phase 1 — Màn hình trống, File Picker, danh sách thumbnail

**Học được:** `x:Bind`, `ObservableCollection`, `ListView` + `DataTemplate`, `[RelayCommand]`, async trong UI, đổi `Visibility` theo trạng thái.

- [x] `MainViewModel`: `ObservableCollection<ImageItemViewModel> Items`, `IsEmpty`, `HasItems`.
- [x] Màn hình trống: chữ "Chưa có ảnh nào" và nút **Thêm ảnh** (`AccentButtonStyle`).
- [x] `AddImagesCommand` mở `FileOpenPicker` (namespace `Microsoft.Windows.Storage.Pickers`) với `PickMultipleFilesAsync`. Bộ lọc file **chỉ `.png .jpg .jpeg`** ở phase này.
- [x] `NaturalStringComparer`: sắp xếp file vừa chọn theo tên kiểu natural sort, rồi **nối vào cuối** `Items`.
- [x] `ThumbnailService`: dùng Magick.NET đọc ảnh, `AutoOrient()`, resize cạnh dài khoảng 240 px, chuyển thành `BitmapImage`. Chạy nền, không làm đơ UI.
- [x] `ImageItemViewModel`: `DisplayName`, `SizeText` (ví dụ `4032×3024 · 3.1 MB`), `Thumbnail`.
- [x] `ListView` hiển thị: thumbnail, tên file, kích thước.

**Xong khi:** chọn 10 ảnh, danh sách hiện đúng thứ tự natural sort, UI không bị đơ trong lúc tạo thumbnail.

---

## Phase 2 — Sắp xếp, xoá, nút Thêm ở cuối, kéo thả từ Explorer

**Học được:** `CanReorderItems`, `MenuFlyout`, `ListView.Footer`, sự kiện drag & drop (`DragOver`, `Drop`).

- [x] Bật `CanReorderItems="True" CanDragItems="True" AllowDrop="True"` cho `ListView`.
- [x] Nút tay cầm bên trái mỗi item, mở `MenuFlyout`: **Lên đầu / Lên 1 / Xuống 1 / Xuống cuối**.
- [x] Nút **Xoá** bên phải mỗi item.
- [x] Item giữ callback về `MainViewModel` để tự di chuyển hoặc xoá mình.
- [x] Nút **Thêm ảnh** đặt trong `ListView.Footer`, luôn nằm sau ảnh cuối.
- [x] `CommandBar` phía trên: **Thêm ảnh**, **Sắp xếp ▾** (theo tên, theo ngày), **Xoá hết**.
- [x] Kéo file từ Explorer thả vào cửa sổ để thêm ảnh. Bỏ qua và báo các file không được hỗ trợ.

**Xong khi:** sắp xếp được bằng cả kéo thả lẫn menu, xoá được, thêm được ảnh liên tục, kéo được file từ Explorer vào.

---

## Phase 3 — Xuất PDF (MVP)

**Học được:** `FileSavePicker`, `ProgressBar`, `InfoBar`, `CancellationToken`.

- [x] Interface `IExporter` với `Task ExportAsync(IReadOnlyList<ImageSourceInfo>, ExportOptions, IProgress<double>, CancellationToken)`.
- [x] `PdfExporter`: mỗi ảnh một trang, kích thước trang theo ảnh. Dùng Magick.NET, nén JPEG bên trong với quality 85.
- [x] Nút **Xuất ảnh** trên `CommandBar` mở `FileSavePicker` với lựa chọn `.pdf`.
- [x] Hiện `ProgressBar` khi đang xuất, có nút **Huỷ**.
- [x] Hiện `InfoBar` khi xong (đường dẫn file, dung lượng) hoặc khi lỗi.

**Xong khi:** gộp 20 ảnh thành PDF đúng thứ tự trên màn hình và mở được bằng trình đọc PDF. 🎉 **MVP hoàn thành.**

---

## Phase 4 — Xuất PNG/WebP ghép dọc và hộp thoại Export

**Học được:** `ContentDialog`, `RadioButtons`, `ComboBox`, `NumberBox`, `ColorPicker`.

- [ ] `ExportDialog` (`ContentDialog`): định dạng (PDF/PNG/WebP), chiều rộng tối đa, khoảng cách giữa các ảnh, màu nền.
- [ ] `ExportViewModel` giữ các tuỳ chọn và kiểm tra tính hợp lệ.
- [ ] `PngExporter` / `WebpExporter`: thu phóng các ảnh về cùng chiều rộng, ghép bằng `AppendVertically()`.
- [ ] Kiểm tra **giới hạn 16383 px của WebP** trước khi xuất. Nếu vượt: báo rõ và gợi ý giảm chiều rộng hoặc dùng PNG.
- [ ] Đặt `ResourceLimits` cho Magick.NET để tránh tràn RAM.

**Xong khi:** xuất được ảnh dài PNG và WebP đúng thứ tự. Trường hợp vượt giới hạn WebP được báo lỗi tử tế, app không crash.

---

## Phase 5 — Mức nén (presets)

**Học được:** `Slider`, đổi `Visibility` theo lựa chọn, bind enum vào UI.

- [ ] `CompressionPreset`: **Gốc / Cân bằng / Nhỏ gọn / Tuỳ chỉnh**. Bảng giá trị xem `CLAUDE.md` mục 3.1.
- [ ] Trong `ExportDialog`: chọn preset. Khi chọn **Tuỳ chỉnh** thì hiện `Slider` quality (1–100) và `NumberBox` chiều rộng tối đa.
- [ ] Các control chỉ hiện khi có ý nghĩa với định dạng đang chọn. Ví dụ: PNG không có slider quality mà có nút bật/tắt **giảm màu (PNG-8)**.
- [ ] Ghi chú ngắn trong UI rằng PNG là lossless, muốn nhỏ hơn thì giảm kích thước hoặc giảm màu.
- [ ] Các exporter nhận `Quality` và `MaxWidth` từ `ExportOptions`.

**Xong khi:** cùng một bộ ảnh, xuất với 3 preset cho ra 3 file có dung lượng khác nhau rõ rệt.

---

## Phase 6 — Dự đoán dung lượng (selling point) ⭐

**Học được:** debounce, huỷ tác vụ cũ, cập nhật UI từ luồng nền, đo lường và tinh chỉnh thuật toán.

- [ ] `ISizeEstimator` và `SampleSizeEstimator` theo thuật toán ở `CLAUDE.md` mục 3.2.
- [ ] Cache bản mẫu thu nhỏ (cạnh dài khoảng 512 px) cho mỗi ảnh. Tạo lại khi danh sách ảnh thay đổi.
- [ ] `ExportViewModel.EstimatedSizeText`, ví dụ `≈ 4.2 MB`. Hiện `ProgressRing` nhỏ trong lúc tính.
- [ ] **Debounce 300 ms** khi đổi slider hoặc preset. Huỷ lần tính trước bằng `CancellationToken`.
- [ ] Sau khi xuất: hiện **dung lượng thật** bên cạnh dung lượng ước tính trong `InfoBar`.
- [ ] Ghi log (Debug output) các cặp ước tính/thực tế. Thử với khoảng 5 bộ ảnh khác nhau để chọn **hệ số hiệu chỉnh** cho từng định dạng.
- [ ] (Khuyến khích) Viết unit test cho `SampleSizeEstimator` trong một project test riêng.

**Xong khi:** sai số ≤ ±20% với WebP và PDF, ước tính 20 ảnh mất dưới 1 giây, kéo slider không gây giật UI.

---

## Phase 7 — Định dạng khó: HEIC, PDF đầu vào

**Học được:** `Windows.Data.Pdf`, xử lý lỗi theo từng file.

- [ ] Thêm `.webp .heic .heif` vào bộ lọc picker và vào xử lý kéo thả (Magick.NET đọc trực tiếp).
- [ ] Thêm `.pdf`: `ImageLoaderService` dùng `PdfDocument` render **từng trang** thành ảnh. Mỗi trang là một item, tên dạng `doc.pdf · trang 3`.
- [ ] File hỏng hoặc không đọc được: bỏ qua, gom lỗi lại và hiện một `InfoBar` tổng hợp, không dừng cả lô.
- [ ] Thử với ảnh HEIC từ iPhone và ảnh chụp dọc để kiểm tra xoay EXIF.

**Xong khi:** trộn lẫn PNG, HEIC và một file PDF 5 trang trong cùng danh sách và xuất ra đúng.

---

## Phase 8 — Trau chuốt và đóng gói (v1.0)

- [ ] Nhớ tuỳ chọn xuất lần trước (`ApplicationData.Current.LocalSettings`).
- [ ] Phím tắt: `Ctrl+O` thêm ảnh, `Delete` xoá, `Ctrl+E` xuất (`KeyboardAccelerator`).
- [ ] Hỗ trợ theme Sáng/Tối, nền `Mica` cho cửa sổ.
- [ ] Kiểm tra với 50+ ảnh: RAM, tốc độ, ListView cuộn mượt.
- [ ] Đặt icon app và tên hiển thị trong `Package.appxmanifest`.
- [ ] Đóng gói MSIX và cài thử trên một máy khác (hoặc máy ảo).

**Xong khi:** cài được app trên máy sạch, dùng trọn luồng thêm → sắp xếp → chọn mức nén → xuất mà không có lỗi.

---

## Ý tưởng mở rộng (sau v1.0)

- [ ] Chế độ **dung lượng mục tiêu**: "File ≤ 5 MB", app tự tìm quality bằng binary search.
- [ ] Xem trước trước/sau khi nén: phóng to một vùng ảnh để so sánh chất lượng.
- [ ] Ghép ngang, ghép lưới (n cột).
- [ ] PDF khổ A4 với lề, căn ảnh vừa trang.
- [ ] Xoay hoặc crop nhanh từng ảnh.
- [ ] Đa ngôn ngữ (`.resw`): tiếng Việt và tiếng Anh.
