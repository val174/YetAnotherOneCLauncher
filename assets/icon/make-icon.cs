#:package SkiaSharp@3.119.4
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property AnalysisLevel=none
// Сборка иконки приложения из картинки: вырезает красный скруглённый квадрат, делает фон прозрачным
// и пишет app.ico (16–256, PNG внутри) и app.png (256) в src/YetAnotherOneCLauncher.App/Assets.
//   dotnet run assets/icon/make-icon.cs
using SkiaSharp;

var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", ".."));
var source = Path.Combine(root, "assets", "icon", "app-icon-source.jpg");
var output = Path.Combine(root, "src", "YetAnotherOneCLauncher.App", "Assets");
Directory.CreateDirectory(output);

using var image = SKBitmap.Decode(source) ?? throw new InvalidOperationException("Не удалось прочитать " + source);

// Красный фон значка: R высокий, G и B низкие.
static bool IsRed(SKColor c) => c.Red > 150 && c.Green < 100 && c.Blue < 100;

int left = image.Width, top = image.Height, right = -1, bottom = -1;
for (var y = 0; y < image.Height; y++)
{
    for (var x = 0; x < image.Width; x++)
    {
        if (IsRed(image.GetPixel(x, y)))
        {
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
    }
}

if (right < 0)
{
    throw new InvalidOperationException("Красный квадрат не найден.");
}

// Радиус скругления: на строке чуть ниже верхнего края красное начинается на расстоянии ≈ радиуса от левого края.
var probeRow = top + 2;
var firstRed = Enumerable.Range(left, right - left).First(x => IsRed(image.GetPixel(x, probeRow)));
var width = right - left + 1;
var height = bottom - top + 1;
var size = Math.Max(width, height);
var radiusRatio = Math.Clamp((firstRed - left) / (double)size * 1.15, 0.12, 0.3);
Console.WriteLine($"Значок: {left},{top} — {right},{bottom} ({width}×{height} px), скругление {radiusRatio:P0}");

// Основа 512×512: сам значок без фона, края сглажены.
const int master = 512;
using var masterBitmap = new SKBitmap(master, master, SKColorType.Rgba8888, SKAlphaType.Premul);
using (var canvas = new SKCanvas(masterBitmap))
{
    canvas.Clear(SKColors.Transparent);
    var inset = 2f; // на краю JPEG светлая кайма — отрезаем
    var rect = new SKRect(inset, inset, master - inset, master - inset);
    canvas.ClipRoundRect(new SKRoundRect(rect, (float)(master * radiusRatio)), SKClipOperation.Intersect, antialias: true);
    using var cropped = new SKBitmap();
    // Точно по красной области: картинка может быть чуть не квадратной (708×682) — лёгкое растяжение незаметно,
    // а квадратная вырезка захватила бы светлый фон.
    image.ExtractSubset(cropped, new SKRectI(left, top, left + width, top + height));
    using var croppedImage = SKImage.FromBitmap(cropped);
    canvas.DrawImage(croppedImage, new SKRect(0, 0, master, master), new SKSamplingOptions(SKCubicResampler.Mitchell));
}

using var masterImage = SKImage.FromBitmap(masterBitmap);
SKBitmap Render(int px)
{
    // Без предумножения альфы: так цвета пишутся и в PNG, и в BMP значка.
    var target = new SKBitmap(px, px, SKColorType.Bgra8888, SKAlphaType.Unpremul);
    using var canvas = new SKCanvas(target);
    canvas.Clear(SKColors.Transparent);
    // Мип-карты: при сильном уменьшении (512 → 16) без них мелкие детали рябят.
    canvas.DrawImage(masterImage, new SKRect(0, 0, px, px), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    return target;
}

byte[] Png(SKBitmap bitmap)
{
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// Классическая запись значка: BITMAPINFOHEADER, 32-битные BGRA снизу вверх, затем пустая маска AND.
// Её понимают все компоненты Windows; PNG внутри ICO часть из них (GDI+) в малых размерах не читает.
byte[] Dib(SKBitmap bitmap)
{
    var px = bitmap.Width;
    var maskStride = (px + 31) / 32 * 4;
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(40);
    writer.Write(px);
    writer.Write(px * 2); // высота вместе с маской
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write(0); // BI_RGB
    writer.Write(px * px * 4 + maskStride * px);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    writer.Write(0);
    for (var y = px - 1; y >= 0; y--)
    {
        for (var x = 0; x < px; x++)
        {
            var c = bitmap.GetPixel(x, y);
            writer.Write(c.Blue);
            writer.Write(c.Green);
            writer.Write(c.Red);
            writer.Write(c.Alpha);
        }
    }

    writer.Write(new byte[maskStride * px]);
    writer.Flush();
    return stream.ToArray();
}

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
var bitmaps = sizes.Select(Render).ToList();
var images = bitmaps.Select(b => b.Width >= 256 ? Png(b) : Dib(b)).ToList();
var largestPng = Png(bitmaps[^1]);

// ICO: заголовок, каталог записей, затем данные: BMP до 128, PNG для 256.
using (var ico = new BinaryWriter(File.Create(Path.Combine(output, "app.ico"))))
{
    ico.Write((ushort)0);
    ico.Write((ushort)1);
    ico.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        ico.Write((byte)0);
        ico.Write((byte)0);
        ico.Write((ushort)1);
        ico.Write((ushort)32);
        ico.Write(images[i].Length);
        ico.Write(offset);
        offset += images[i].Length;
    }

    foreach (var png in images)
    {
        ico.Write(png);
    }
}

File.WriteAllBytes(Path.Combine(output, "app.png"), largestPng);
Console.WriteLine($"Готово: {Path.Combine(output, "app.ico")} ({string.Join(", ", sizes)}), app.png");
