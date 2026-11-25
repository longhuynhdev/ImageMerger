
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
| 6 | Dự đoán dung lượng | 8–12h | Nền tảng cho Phase T |
| T | Dung lượng mục tiêu (mặc định ≤ 20 MB, tự chọn mức nén) | 8–14h | **Selling point** |
| 7 | Định dạng khó: HEIC, PDF đầu vào | 6–10h | |
| 8 | Trau chuốt và đóng gói | 8–15h | **v1.0** |
| — | **Tổng** | **khoảng 48–81h** | |
| R | Đổi tên: hiển thị "Minh Chứng Ơi" (đã chốt), tên trong code (lên plan sau) | 1–4h | |
| C | Tách nghiệp vụ ra thư viện `ImageMerger.Core` (làm dần, xen kẽ Phase 7–8) | 4–8h | |
| W | Bản web cho điện thoại (**đang cân nhắc, chưa làm**) | — | |

---

## Phase 0 — Khởi tạo project

**Học được:** cấu trúc project WinUI 3, `App.xaml`, `MainWindow`, NuGet.

- [x] Tạo project bằng template **WinUI Blank App (Packaged)**, đặt tên `ImageMerger`.
- [x] Đổi `TargetFramework` thành `net10.0-windows10.0.19041.0`, bật `<Nullable>enable</Nullable>`.
- [x] Cập nhật Windows App SDK lên **1.8+**.
- [x] Cài package: `Magick.NET-Q8-AnyCPU`. *(Ban đầu có `CommunityToolkit.Mvvm`, sau đã gỡ theo yêu cầu tech lead và thay bằng `INotifyPropertyChanged` tự viết.)*
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

- [x] `ExportDialog` (`ContentDialog`): định dạng (PDF/PNG/WebP), chiều rộng tối đa, khoảng cách giữa các ảnh, màu nền.
- [x] `ExportViewModel` giữ các tuỳ chọn và kiểm tra tính hợp lệ.
- [x] `PngExporter` / `WebpExporter`: thu phóng các ảnh về cùng chiều rộng, ghép bằng `AppendVertically()`.
- [x] Kiểm tra **giới hạn 16383 px của WebP** trước khi xuất. Nếu vượt: báo rõ và gợi ý giảm chiều rộng hoặc dùng PNG.
- [x] Đặt `ResourceLimits` cho Magick.NET để tránh tràn RAM.

**Xong khi:** xuất được ảnh dài PNG và WebP đúng thứ tự. Trường hợp vượt giới hạn WebP được báo lỗi tử tế, app không crash.

> **Đã gỡ PNG/WebP:** sau Phase 5, app chỉ xuất PDF (lý do xem `CLAUDE.md`, mục Non-goals). Code ghép ảnh nằm ở git tag `before-pdf-only`.

---

## Phase 5 — Mức nén (presets)

**Học được:** `Slider`, đổi `Visibility` theo lựa chọn, bind enum vào UI.

- [x] `CompressionPreset`: **Gốc / Cân bằng / Nhỏ gọn / Tuỳ chỉnh**. Bảng giá trị xem `CLAUDE.md` mục 3.1.
- [x] Trong `ExportDialog`: chọn preset. Khi chọn **Tuỳ chỉnh** thì hiện `Slider` quality (1–100) và `NumberBox` chiều rộng tối đa.
- [x] Các control chỉ hiện khi có ý nghĩa với định dạng đang chọn. Ví dụ: PNG không có slider quality mà có nút bật/tắt **giảm màu (PNG-8)**.
- [x] Ghi chú ngắn trong UI rằng PNG là lossless, muốn nhỏ hơn thì giảm kích thước hoặc giảm màu.
- [x] Các exporter nhận `Quality` và `MaxWidth` từ `ExportOptions`.

**Xong khi:** cùng một bộ ảnh, xuất với 3 preset cho ra 3 file có dung lượng khác nhau rõ rệt.

> **Đã gỡ PNG/WebP:** preset giờ chỉ gồm quality JPEG trong PDF và chiều rộng tối đa. Tuỳ chọn giảm màu PNG-8 đã bỏ.

> **Giao diện mới (nhánh `redesign-export-bar`):** gỡ `ExportDialog` và `CommandBar`. Mức nén, nút Xuất PDF (Ctrl+E), tiến độ và kết quả nằm ở thanh đáy `MainPage`. Ô dung lượng ước tính đã có chỗ trong UI, chờ Phase 6 điền số.

---

## Phase 6 — Dự đoán dung lượng (selling point) ⭐

**Học được:** debounce, huỷ tác vụ cũ, cập nhật UI từ luồng nền, đo lường và tinh chỉnh thuật toán.

- [x] `ISizeEstimator` và `SampleSizeEstimator` theo thuật toán ở `CLAUDE.md` mục 3.2.
- [x] Cache bản mẫu thu nhỏ (cạnh dài khoảng 512 px) cho mỗi ảnh. Tạo lại khi danh sách ảnh thay đổi.
- [x] `ExportViewModel.EstimatedSizeText`, ví dụ `≈ 4.2 MB`. Hiện `ProgressRing` nhỏ trong lúc tính.
- [x] **Debounce 300 ms** khi đổi slider hoặc preset. Huỷ lần tính trước bằng `CancellationToken`.
- [x] Sau khi xuất: hiện **dung lượng thật** bên cạnh dung lượng ước tính trong `InfoBar`.
- [x] Ghi log (Debug output) các cặp ước tính/thực tế, kèm giá trị "k gợi ý" cho hệ số hiệu chỉnh.
- [ ] Thử với khoảng 5 bộ ảnh **thật** khác nhau để chọn **hệ số hiệu chỉnh** (`SampleSizeEstimator.ScaleExponent`, đang là giá trị tạm 0.1).
- [ ] (Khuyến khích) Viết unit test cho `SampleSizeEstimator` trong một project test riêng. *(Chuyển sang Phase C, bước C5.)*

**Xong khi:** sai số ≤ ±20%, ước tính 20 ảnh mất dưới 1 giây, kéo slider không gây giật UI.

> **Đo thử (ảnh tổng hợp, 20 ảnh 4032×3024):** lần đầu (tạo bản mẫu) 0,67 s, đổi quality 0,07 s, đổi chiều rộng ≈ 0 s.
> Sai số chưa đạt: ảnh tổng hợp có nhiễu cho ra khoảng −46%. Ảnh nhiễu không giống ảnh thật, nên phải đo lại bằng ảnh thật.

---

## Phase T — Dung lượng mục tiêu (selling point) ⭐

**Bối cảnh:** xem `CLAUDE.md` mục 3.0. Người dùng là người trung niên, không cần hiểu quality hay chiều rộng. Họ chỉ cần file **lọt qua giới hạn của cổng nộp**.

**Học được:** tìm kiếm nhị phân trên kết quả ước tính, `Expander`, thiết kế UX cho người không rành công nghệ.

**Điều kiện trước:** Phase 6 phải chọn xong hệ số hiệu chỉnh bằng ảnh thật. Ước tính sai thì app chọn sai mức nén.

### Hành vi (đã chốt)
- Giới hạn mặc định **≤ 20 MB**. Người dùng chỉnh được.
- Khi thêm, xoá ảnh, đổi khoảng trang PDF hoặc đổi giới hạn, app **tự chọn mức nén** để file ≤ giới hạn với chất lượng cao nhất có thể. Không cần nén thì giữ mức cao nhất.
- Preset và slider hiện tại chuyển vào phần **Tuỳ chỉnh nâng cao**.

### Thiết kế đề xuất (chưa chốt, xem lại trước khi code)
1. **Thang mức nén** thay cho chỉ chỉnh quality: một danh sách các bậc `(quality, chiều rộng tối đa)`, xếp từ nét nhất đến nhẹ nhất. Ví dụ: `(95, gốc) → (85, gốc) → (85, 2560) → (80, 2048) → (75, 1600) → (70, 1280) → (65, 1024)`.
   - Lý do: với giấy tờ có chữ, quality quá thấp làm chữ bị nhoè và có vệt khối JPEG, còn giảm độ phân giải vừa phải vẫn đọc được. Phối hợp cả hai thì chữ giữ được rõ lâu hơn.
   - Dung lượng giảm dần theo bậc, nên **tìm nhị phân** trên thang (khoảng 3 lần ước tính). Đo ở Phase 6 cho thấy mỗi lần ước tính mất khoảng 0,07 s, đủ nhanh.
   - Các bậc cụ thể cần thí nghiệm với ảnh giấy tờ thật.
2. **Biên an toàn:** ước tính có sai số (chỉ tiêu ±20%), nên đặt mục tiêu khoảng **85–90% giới hạn**. Ví dụ giới hạn 20 MB thì nhắm ≤ 17–18 MB.
3. **Đảm bảo sau khi xuất:** đo dung lượng thật. Nếu vượt giới hạn, tự xuất lại ở bậc thấp hơn (tối đa vài lần) rồi báo cho người dùng biết.
4. **Cách tính MB:** dùng `1 MB = 1.000.000 byte` khi so với giới hạn. Con số này nhỏ hơn `1024 × 1024`, nên file an toàn với cả những cổng tính theo cách còn lại.
5. **Không đạt được giới hạn:** ví dụ riêng phần trang PDF giữ nguyên đã vượt giới hạn, hoặc bậc thấp nhất vẫn vượt. Báo rõ lý do bằng lời dễ hiểu, gợi ý bỏ bớt ảnh hoặc trang. Không tự giảm chất lượng xuống mức không đọc được.
6. **Hiển thị:** kiểu `≈ 14,2 MB / 20 MB ✓`, kèm mô tả chất lượng bằng chữ (ví dụ "Rất nét", "Nét", "Đủ đọc") thay cho con số quality.
7. **Chọn giới hạn:** vài nút chọn nhanh (2 / 5 / 10 / 20 MB) và ô nhập số.

### Cần chốt trước khi code
- [ ] Các bậc của thang mức nén (thí nghiệm với ảnh giấy tờ thật).
- [ ] Người dùng chỉnh tay ở phần nâng cao thì sao: tắt chế độ tự động, hay vẫn cảnh báo khi vượt giới hạn?
- [ ] Danh sách nút chọn nhanh và cách hiển thị chất lượng bằng chữ.
- [ ] Có nhớ giới hạn lần trước không (liên quan Phase 8, `LocalSettings`).

### Task
- [ ] Logic chọn bậc nén theo giới hạn, nằm trong Services (thuần C#, không phụ thuộc WinUI, test được).
- [ ] UI chọn giới hạn ở thanh đáy, mặc định 20 MB.
- [ ] Chuyển preset và slider vào `Expander` "Tuỳ chỉnh nâng cao".
- [ ] Tự tính lại khi danh sách hoặc giới hạn thay đổi (debounce và huỷ lần tính trước như Phase 6).
- [ ] Kiểm tra dung lượng thật sau khi xuất, tự xuất lại nếu vượt.
- [ ] Thông báo khi không thể đạt giới hạn.

**Xong khi:** với khoảng 5 bộ ảnh thật và các giới hạn 2 / 5 / 20 MB, file xuất ra **luôn ≤ giới hạn**. Bậc được chọn không thấp hơn bậc tốt nhất có thể quá 1 bậc (so với cách thử xuất từng bậc).

---

## Phase 7 — Định dạng khó: HEIC, PDF đầu vào

**Học được:** `Windows.Data.Pdf`, xử lý lỗi theo từng file.

- [ ] Thêm `.webp .heic .heif` vào bộ lọc picker và vào xử lý kéo thả (Magick.NET đọc trực tiếp).
- [x] **PDF đầu vào, Bước 1: merge giữ nguyên trang** (đã chốt). Dùng **PdfPig** (Apache 2.0) ghi toàn bộ file PDF đầu ra và gỡ `JpegPdfWriter`.
  - Mỗi file PDF là **một item đã thu gọn** trong danh sách: thumbnail trang 1 (render bằng `Windows.Data.Pdf`), dòng phụ dạng `PDF · 20 trang · 1.2 MB`. Item di chuyển và xoá như một khối.
  - Xuất: trang PDF được **chép nguyên** (giữ chữ, bôi đen được), không nén. Ảnh vẫn nén JPEG như trước.
  - Ước tính dung lượng: item PDF tính bằng dung lượng file gốc, không đổi theo mức nén.
  - Ghi file: chỉ gọi `PdfDocumentBuilder.Dispose()`, **không gọi `Build()`**. `Build()` đọc lại toàn bộ file vào `byte[]`: đo thử file 153 MB thì RAM lên đỉnh 679 MB, còn chỉ `Dispose()` thì đứng yên khoảng 31 MB.
- [x] **PDF đầu vào: chọn trang bằng ô nhập khoảng trang** (đã chốt, thay cho Bước 2 cũ). ⚠️ **Code xong, build được, CHƯA chạy thử và chưa commit.** Session sau: chạy app, thử các trường hợp ở dưới rồi mới commit.
  - Mỗi item PDF có ô "Trang" ở dòng phụ, cú pháp giống hộp thoại In: `1-3`, `1, 2, 5`, `1-3, 7`. Để trống = tất cả. Placeholder: `Tất cả (vd: 1-3, 5)`.
  - **Quy tắc: xuất đúng những gì gõ, theo đúng thứ tự gõ.** `5, 1-3` → trang 5, 1, 2, 3. `1, 1` → trang 1 hai lần. Không loại trùng, không sắp xếp lại: đơn giản nhất cả về code lẫn cách giải thích. Gõ nhầm thì người dùng thấy ngay qua cột vị trí và tổng số trang.
  - Ô nhập **không** đặt ở cột vị trí (`1–8`): cột đó là vị trí trong file xuất, còn ô nhập là trang của file nguồn. Cột vị trí tự cập nhật theo số trang đã chọn.
  - Kiểm tra input, báo lỗi tại chỗ bằng dòng chữ đỏ (giống ô "Tên file"), không dùng dialog: chỉ nhận số, dấu phẩy, `-`/`–`, khoảng trắng; số trong 1..số trang của file; khoảng `a-b` cần a ≤ b. Còn item nhập sai thì nút Xuất PDF bị tắt. PDF chưa đọc xong thì ô nhập bị khoá.
  - Phân tích cú pháp ở `Helpers/PageRangeParser.cs` (thuần C#, không phụ thuộc WinUI).
  - Ước tính dung lượng ≈ dung lượng file × số trang chọn / tổng số trang (gần đúng, vì font và tài nguyên dùng chung giữa các trang).
- [ ] *(Stretch, chỉ làm nếu cần)* **Bước 2 cũ: mở rộng item PDF ra danh sách trang** (`Expander`), xoá và sắp xếp từng trang có thumbnail. Ô nhập khoảng trang đã làm được xoá, sắp xếp và lấy một đoạn; Bước 2 chỉ thêm việc **nhìn thấy** trang. Đây là phần UI khó nhất (ListView kéo thả lồng trong ListView kéo thả), nên chỉ làm nếu dùng thực tế mà hay phải mở file ra để tra số trang. Phương án nhẹ hơn: tooltip xem trước trang.
- *(Ngoài phạm vi)* **Tách một file thành nhiều file.** Khi cần thì dùng trình duyệt (In → chọn trang → Lưu dưới dạng PDF) hoặc PDFsam Basic. Nếu sau này cần thường xuyên, ý tưởng là chèn "vạch cắt" giữa các item, mỗi đoạn xuất thành một file riêng (`ten_1.pdf`, `ten_2.pdf`…).
- [ ] ⏸️ **TẠM HOÃN, chưa code: nén trang PDF scan** (chờ tôi quyết định).
  > **Hướng đang cân nhắc (chưa chốt):** giữ được chữ (bôi đen, copy) của PDF đầu vào. Chỉ nén trang scan.
  >
  > **Use case:** (1) nhiều loại ảnh → một PDF; (2) nhiều PDF → một PDF; (3) trộn lẫn ảnh và PDF. Coi mọi thứ là **danh sách trang**, mỗi trang có cách xử lý riêng, nên use case 3 tự hoạt động mà không cần dialog hỏi người dùng lúc import.
  >
  > **Mức 1 (đề xuất làm trước): phân loại theo từng trang**
  > | Trang có… | Phân loại | Xử lý |
  > |---|---|---|
  > | Ảnh đầu vào (PNG/JPG/HEIC…) | Ảnh | Nén (luồng hiện tại) |
  > | Chữ thật (lấy ra được ký tự) | Trang chữ | Giữ nguyên |
  > | Không có chữ, ảnh phủ khoảng ≥ 90% trang | Trang scan | Nén: render thành ảnh, rồi qua luồng JPEG |
  > | Không có chữ, không có ảnh lớn (vector) | Khác | Giữ nguyên |
  >
  > - Quy tắc an toàn: **có chữ thì giữ nguyên**. Nhờ vậy PDF scan đã OCR (có lớp chữ ẩn) không bị mất khả năng bôi đen, đổi lại trang đó không được nén.
  > - Toàn PDF chữ thì kéo slider nén, dung lượng gần như không đổi. Không chặn, không cảnh báo, nhưng nên hiện một dòng nhỏ như `≈ 3.1 MB · 12 trang giữ nguyên` để người dùng không nghĩ app bị lỗi.
  > - Render trang scan với **DPI khớp độ phân giải ảnh gốc** trong trang, không dùng DPI cố định.
  > - **Chỉ thay bằng bản nén nếu bản mới nhỏ hơn**, vì scan đen trắng JBIG2/CCITT vốn đã rất nhỏ.
  > - Ước tính dung lượng: `Σ(ảnh và trang scan nén, ước tính) + Σ(dung lượng trang giữ nguyên)`. Phần giữ nguyên không đổi theo slider và chỉ đo được gần đúng, vì font và tài nguyên có thể dùng chung giữa các trang.
  >
  > **Mức 2 (stretch): nén ảnh bên trong PDF, không render lại trang.** Chỉ thay các ảnh nhúng bằng bản nén, chữ, vector và lớp OCR giữ nguyên, nên không cần phân loại trang. Cách này khó hơn nhiều: phải xử lý nhiều kiểu mã hoá (JPEG, Flate, JBIG2, CCITT, JPX), nhiều hệ màu (RGB, CMYK, Indexed, ICC), SMask, và ảnh dùng chung giữa nhiều trang.
  >
  > **Thứ tự làm dự kiến:** PDF đầu vào chỉ "Giữ nguyên" (merge) → phân loại trang và nén trang scan (Mức 1) → Mức 2.
  >
  > **Cần chốt trước khi code:**
  > - [x] Thư viện PDF: đã chọn **PdfPig** (xem Bước 1). PdfPig cũng đọc được chữ và ảnh trong trang, dùng lại được cho bước phân loại.
  > - [ ] Thí nghiệm nhỏ (ngoài project): độ chính xác khi phân loại trang chữ, scan, scan có OCR trên file thật.
  > - [x] Đổi tên project: đã chốt tên hiển thị "Minh Chứng Ơi", xem Phase R.
  >
  > *Phương án cũ, đã cân nhắc và tạm bỏ:* chuyển mọi trang PDF thành ảnh (đơn giản, không cần thư viện, nhưng mất chữ, link và bookmark).
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

## Phase R — Đổi tên

### R1. Đổi tên hiển thị thành "Minh Chứng Ơi" (đã chốt)
- [ ] `Package.appxmanifest`: `<DisplayName>` (dòng 20) và `uap:VisualElements DisplayName` (dòng 42).
- [ ] `MainWindow.xaml`: `Title` của `Window` và `TitleBar`.
- [ ] **Không đổi `Identity Name`** trong manifest. Đổi nó thì Windows coi là một app khác, bản cài cũ không được cập nhật và mất `LocalSettings`.
- [ ] Tagline: chưa chốt, các gợi ý ghi ở `CLAUDE.md` mục 1.

**Xong khi:** Start menu, thanh tiêu đề và taskbar đều hiện "Minh Chứng Ơi".

### R2. Đổi tên trong code sang tên tiếng Anh (lên plan sau)
- Lý do: app thuần Việt, nhưng code theo chuẩn tiếng Anh thì có lợi khi xin việc sau này.
- [ ] Chọn tên tiếng Anh.
- Phạm vi dự kiến: tên thư mục, `.slnx`, `.csproj`, `RootNamespace`, namespace trong toàn bộ `.cs`/`.xaml`, `docfx.json`, tên repo.
- ⚠️ **Liên quan Phase C:** làm R2 trước C1 thì `ImageMerger.Core` được tạo với tên mới ngay từ đầu. Làm sau thì phải đổi tên cả project Core.

---

## Phase C — Tách nghiệp vụ ra thư viện `ImageMerger.Core`

**Học được:** class library, project reference, package đi theo project reference (transitive), `.slnx` nhiều project, unit test.

**Vì sao làm, kể cả khi chưa có bản web:**
- Compiler tự kiểm tra quy tắc "Service không tham chiếu WinUI" (`CLAUDE.md` mục 6). Core là `net10.0` thuần nên không `using` được WinUI hay WinRT.
- Unit test chạy bằng project `net10.0` thường, không cần target Windows hay chạy packaged.
- Bản web (Phase W), CLI hoặc công cụ đo đạc sau này chỉ cần tham chiếu Core.

**Cách làm:** mỗi bước là **một commit nhỏ, build được, chạy được**. Chủ yếu là di chuyển file, không sửa logic. Làm xen kẽ với Phase 7–8 được.

**Quyết định đã chốt:**
- Tên project: `ImageMerger.Core`, đặt cạnh project app trong thư mục `ImageMerger/` và cùng `ImageMerger.slnx`.
- TFM: `net10.0`. Bật `Nullable`, `ImplicitUsings`, `GenerateDocumentationFile` giống app. Platform `AnyCPU` (app vẫn x86/x64/ARM64).
- **Giữ nguyên namespace** khi chuyển file (`ImageMerger.Models`, `ImageMerger.Services...`, `ImageMerger.Helpers`), để ViewModel và View không phải sửa `using`. Đổi namespace (nếu muốn) làm task riêng sau.
- Package `Magick.NET-Q8-AnyCPU` và `PdfPig` chuyển sang Core. App nhận lại qua project reference.

### Các bước

> **Trạng thái:** C1–C4 đã code xong và build được (`dotnet build ImageMerger.slnx`). Đã kiểm tra: Core không còn `using` Windows/WinUI; console `net10.0` riêng gọi Core tạo được thumbnail, ước tính và xuất PDF 8 trang. ⚠️ **Chưa chạy thử app WinUI bằng tay và chưa commit.**

- [x] **C1. Tạo project rỗng.** Tạo `ImageMerger.Core.csproj`, thêm vào `ImageMerger.slnx`, cho app tham chiếu Core. Chuyển hai package Magick.NET và PdfPig sang Core. Thêm `ImageMerger.Core.dll` vào `docfx.json`.
  - [x] DLL native `Magick.Native-Q8-x64.dll` vẫn được copy vào output của app (Debug x64).
  - [ ] Chạy app, thêm ảnh và PDF, xuất thử một PDF.
  - [ ] Kiểm tra bản publish Release, vì app bật `PublishTrimmed` (trimming chỉ chạy lúc publish).
- [x] **C2. Chuyển code thuần C#** (không dùng thư viện ngoài): `Models/*`, `Helpers/NaturalStringComparer.cs`, `Helpers/PageRangeParser.cs`, `Helpers/FileSizeFormatter.cs`, `Services/SupportedFormats.cs`.
- [x] **C3. Chuyển các service dùng Magick.NET / PdfPig:** `Services/ImageHeader.cs`, `Services/MagickSetup.cs`, `Services/Export/*`, `Services/Compression/*`.
- [x] **C4. Tách đôi `ThumbnailService`** (đây là file duy nhất trong Services vừa có phần thuần vừa có phần Windows):
  - `CreateThumbnail` (Magick.NET) và `ThumbnailResult` chuyển sang Core.
  - `CreatePdfThumbnailAsync` (dùng `Windows.Data.Pdf`) và `PdfThumbnailResult` ở lại app, thành class riêng `PdfThumbnailService`.
  - Cập nhật `MainWindow.xaml.cs`, `MainViewModel`, `ImageItemViewModel.LoadThumbnailAsync` để nhận hai service.
  - Chưa tạo interface cho phần render PDF. Chỉ thêm khi bản web thật sự cần (lúc đó mới biết web render PDF bằng gì).
- [ ] **C5. Project test `ImageMerger.Core.Tests`** (`net10.0`). ⏸️ *Chờ duyệt package test framework (xUnit hoặc MSTest).*
  - Test trước những phần dễ và đáng test: `PageRangeParser`, `NaturalStringComparer`, `FileSizeFormatter`.
  - Sau đó: `SampleSizeEstimator` với vài ảnh trong `testdata/`, kiểm tra sai số ≤ ±20% (gộp task test ở Phase 6).
- [x] **C6. Cập nhật tài liệu:** cây thư mục (`CLAUDE.md` mục 6), tech stack (mục 4), lệnh build (mục 9).
  - [ ] Thêm lệnh chạy test vào mục 9 sau khi làm C5.

**Ở lại project app (không chuyển):** `Views/*`, `ViewModels/*`, `Helpers/BindableBase.cs`, `Helpers/RelayCommand.cs` (chỉ có ích cho MVVM/XAML, Blazor hay React không dùng), `Helpers/XamlFunctions.cs`, `Services/FilePickerService.cs` + `IFilePickerService.cs`, `PdfThumbnailService`.

**Chưa làm ở phase này (tránh thiết kế trước khi biết yêu cầu):**
- Đổi API từ đường dẫn file (`string path`) sang `Stream`. Web sẽ cần, nhưng chỉ làm khi bắt đầu Phase W.
- Tạo interface cho từng service chỉ để "sau này thay được".

**Xong khi:** solution có 2–3 project, build sạch. App chạy đủ luồng thêm ảnh + PDF → ước tính → xuất PDF như trước khi tách. Core không có `using` nào tới `Microsoft.UI`, `Microsoft.Windows` hay `Windows.*`.

---

## Phase W — Bản web cho điện thoại (đang cân nhắc, chưa làm)

> Ghi lại hướng đã thảo luận, **chưa chốt và chưa code**. Chỉ bắt đầu sau Phase C.

**Hướng đang nghiêng về:**
- Backend: **ASP.NET Core Web API**, xử lý ảnh trên server, dùng lại `ImageMerger.Core`.
- Frontend: **React + Vite (SPA, TypeScript)**, host tĩnh. Đã cân nhắc Blazor WASM (tải lần đầu nặng trên 4G), Blazor Server (cần giữ kết nối SignalR, không hợp mạng di động), TanStack Start (SSR và server functions trùng vai trò với .NET API).
- Người dùng mục tiêu: người trung niên, dân văn phòng ở Việt Nam, dùng **điện thoại**. Có thể làm PWA thay cho app native.
- Repo: nghiêng về để chung repo với app WinUI cho dễ dùng chung Core.

**Vấn đề đã biết, cần thiết kế khi bắt đầu:**
- **Trình duyệt trong Zalo:** upload và tải file hay lỗi. Cần test sớm, cân nhắc Web Share API để chia sẻ PDF.
- **Dữ liệu cá nhân** (CCCD, hợp đồng…): xoá file ngay sau khi xử lý, file tạm có TTL, không log nội dung ảnh, chỉ HTTPS. Tham khảo Nghị định 13/2023.
- **Băng thông upload:** thu nhỏ ảnh trên trình duyệt trước khi upload.
- **Upload lớn:** giới hạn request body mặc định của Kestrel, multipart streaming.
- **Việc nặng CPU:** hàng đợi job (`Channel<T>` + `BackgroundService`), báo tiến độ bằng polling/SSE, rate limiting, giới hạn số job song song, `ResourceLimits` cho Magick.NET.
- **Ước tính dung lượng:** server đo bytes-per-pixel ở vài mức quality rồi gửi về, client nội suy tại chỗ, kéo slider không cần gọi API.
- **Render thumbnail PDF** trên server không có `Windows.Data.Pdf`, chưa có lời giải.
- **Chi phí server** tăng theo số người dùng (khác với app desktop).

**Cần chốt trước khi làm:**
- [ ] Frontend: React + Vite hay lựa chọn khác.
- [ ] Hosting và ngân sách hằng tháng.
- [ ] Cập nhật `CLAUDE.md` mục 2: "đa nền tảng" đang nằm trong Non-goals.

---

## Ý tưởng mở rộng (sau v1.0)

- [ ] Xem trước trước/sau khi nén: phóng to một vùng ảnh để so sánh chất lượng.
- [ ] Xuất ảnh dài (JPEG) để gửi qua ứng dụng chat. Có thể dựa trên code ghép ảnh ở tag `before-pdf-only`.
- [ ] PDF khổ A4 với lề, căn ảnh vừa trang.
- [ ] Xoay hoặc crop nhanh từng ảnh.
- [ ] Đa ngôn ngữ (`.resw`): tiếng Việt và tiếng Anh.
