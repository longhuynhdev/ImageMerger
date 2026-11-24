#:package Magick.NET-Q8-AnyCPU@15.1.0

// Sinh bộ ảnh test cho các trường hợp biên (edge cases) của ImageMerger.
// Chạy từ thư mục gốc repo:
//     dotnet run tools/GenerateTestImages.cs
// Kết quả nằm ở testdata/generated/ (đã nằm trong .gitignore).

using ImageMagick;
using ImageMagick.Drawing;

var scriptDir = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();
var root = Path.GetFullPath(Path.Combine(scriptDir, "..", "testdata", "generated"));
Directory.CreateDirectory(root);
Console.WriteLine($"Output: {root}");

// 1. Natural sort: picker trả file không theo thứ tự, app phải sắp img2 trước img10.
var dir = Sub("natural-sort");
string[] colors = ["#e74c3c", "#e67e22", "#f1c40f", "#2ecc71", "#3498db", "#9b59b6"];
int[] numbers = [1, 2, 3, 10, 11, 20];
for (var i = 0; i < numbers.Length; i++)
{
    using var img = Labeled(colors[i], 800, 600, $"img{numbers[i]}");
    Save(img, dir, $"img{numbers[i]}.png");
}

// 2. Kích thước khắc nghiệt.
dir = Sub("sizes");
using (var img = new MagickImage(MagickColors.Red, 1, 1)) Save(img, dir, "tiny_1x1.png");
using (var img = Gradient(8000, 6000, "navy-orange")) Save(img, dir, "huge_8000x6000.png");
// Trang PDF > 14400 pt (200 inch) Acrobat không mở được → PdfExporter phải thu nhỏ.
using (var img = Gradient(1000, 30000, "white-black")) Save(img, dir, "very_tall_1000x30000.png");
using (var img = Gradient(30000, 800, "green-yellow")) Save(img, dir, "very_wide_30000x800.png");

// 3. Biến thể PNG.
dir = Sub("png-variants");
using (var img = new MagickImage("radial-gradient:red-none", Size(1200, 1200)))
    Save(img, dir, "alpha_transparent.png"); // JPEG không có alpha: nền phải thành trắng, không phải đen.
using (var img = Gradient(1200, 900, "black-white"))
{
    img.ColorType = ColorType.Grayscale;
    Save(img, dir, "grayscale.png");
}
using (var img = Gradient(1200, 900, "purple-cyan"))
{
    img.Depth = 16;
    Save(img, dir, "16bit.png", MagickFormat.Png48);
}
using (var img = Plasma(1200, 900))
    Save(img, dir, "palette_8bit.png", MagickFormat.Png8);

// 4. Nội dung cực trị cho bộ ước tính dung lượng.
//    Ảnh tổng hợp KHÔNG thay được ảnh thật khi chỉnh hệ số hiệu chỉnh, chỉ để xem sai số xấu nhất.
dir = Sub("content");
using (var img = new MagickImage(new MagickColor("#3498db"), 3000, 2000)) Save(img, dir, "flat_color.png");
using (var img = Gradient(3000, 2000, "#ff0000-#0000ff")) Save(img, dir, "smooth_gradient.png");
using (var img = Plasma(3000, 2000)) Save(img, dir, "plasma_photo_like.png");
using (var img = new MagickImage(MagickColors.Gray, 3000, 2000))
{
    img.AddNoise(NoiseType.Random); // nhiễu ngẫu nhiên: nén kém nhất có thể
    Save(img, dir, "random_noise.png");
}
using (var img = ScreenshotLike(1920, 1080)) Save(img, dir, "screenshot_text.png");

// 5. File lỗi: app không được crash.
dir = Sub("broken");
File.WriteAllBytes(Path.Combine(dir, "empty_0byte.png"), []);
using (var img = Plasma(800, 600))
{
    img.Format = MagickFormat.Jpeg;
    File.WriteAllBytes(Path.Combine(dir, "actually_jpeg.png"), img.ToByteArray()); // sai đuôi
    img.Format = MagickFormat.Png;
    var bytes = img.ToByteArray();
    File.WriteAllBytes(Path.Combine(dir, "truncated.png"), bytes[..(bytes.Length / 2)]); // bị cắt cụt
}
File.WriteAllText(Path.Combine(dir, "text_file.png"), "Đây không phải ảnh.");

// 6. Tên file khó.
dir = Sub("names");
using (var img = Labeled("#16a085", 800, 600, "unicode")) Save(img, dir, "ảnh chụp màn hình tiếng Việt (1).png");
using (var img = Labeled("#8e44ad", 800, 600, "spaces")) Save(img, dir, "  khoảng trắng   nhiều  .png");
using (var img = Labeled("#c0392b", 800, 600, "long")) Save(img, dir, new string('a', 180) + ".png");
using (var img = Labeled("#2c3e50", 800, 600, "UPPER")) Save(img, dir, "UPPERCASE.PNG");

// 7. PDF nhiều trang (cho Phase 7: PDF đầu vào). Magick.NET ghi PDF không cần Ghostscript.
dir = Sub("pdf");
using (var pages = new MagickImageCollection())
{
    pages.Add(Labeled("#e74c3c", 1240, 1754, "Trang 1")); // A4 dọc @150dpi
    pages.Add(Labeled("#27ae60", 1754, 1240, "Trang 2 (ngang)"));
    pages.Add(Labeled("#2980b9", 1240, 1754, "Trang 3"));
    pages.Write(Path.Combine(dir, "three_pages.pdf"), MagickFormat.Pdf);
    Console.WriteLine("  pdf/three_pages.pdf");
}

// 8. Thử nghiệm: Magick.NET bản NuGet có encode được HEIC không?
dir = Sub("heic");
try
{
    using var img = Plasma(1200, 900);
    Save(img, dir, "plasma.heic", MagickFormat.Heic);
}
catch (MagickException ex)
{
    Console.WriteLine($"  heic: KHÔNG encode được ({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
}

Console.WriteLine("Xong.");

// ---------- helpers ----------

string Sub(string name)
{
    var path = Path.Combine(root, name);
    Directory.CreateDirectory(path);
    return path;
}

void Save(IMagickImage<byte> img, string folder, string fileName, MagickFormat? format = null)
{
    var path = Path.Combine(folder, fileName);
    if (format is { } f) img.Write(path, f); else img.Write(path);
    var kb = new FileInfo(path).Length / 1024.0;
    Console.WriteLine($"  {Path.GetFileName(folder)}/{fileName}  ({img.Width}x{img.Height}, {kb:N0} KB)");
}

static MagickReadSettings Size(uint w, uint h) => new() { Width = w, Height = h };

static MagickImage Gradient(uint w, uint h, string spec) => new($"gradient:{spec}", Size(w, h));

static MagickImage Plasma(uint w, uint h) => new("plasma:fractal", Size(w, h));

static MagickImage Labeled(string color, uint w, uint h, string text)
{
    var img = new MagickImage(new MagickColor(color), w, h);
    new Drawables()
        .FontPointSize(Math.Min(w, h) / 6.0)
        .FillColor(MagickColors.White)
        .Gravity(Gravity.Center)
        .Text(0, 0, text)
        .Draw(img);
    return img;
}

static MagickImage ScreenshotLike(uint w, uint h)
{
    var img = new MagickImage(MagickColors.White, w, h);
    var d = new Drawables().FontPointSize(18).FillColor(MagickColors.Black);
    for (var y = 40; y < h - 20; y += 28)
        d.Text(40, y, $"Dòng {y / 28}: The quick brown fox jumps over the lazy dog 0123456789");
    d.Draw(img);
    return img;
}
