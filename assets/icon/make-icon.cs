#:package SkiaSharp@3.119.4
#:property ManagePackageVersionsCentrally=false
#:property TreatWarningsAsErrors=false
#:property AnalysisLevel=none
// Значок приложения (задача #34): база данных и кнопка запуска на графитовом скруглённом квадрате.
// Рисуется вектором в каждом размере (без уменьшения картинки) и пишется в src/YetAnotherOneCLauncher.App/Assets:
// app.ico (16–256) и app.png (256). Тот же рисунок — app-icon.svg рядом.
//   dotnet run assets/icon/make-icon.cs
using SkiaSharp;

var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", ".."));
var output = Path.Combine(root, "src", "YetAnotherOneCLauncher.App", "Assets");
Directory.CreateDirectory(output);

var graphite = SKColor.Parse("#262A31");
var red = SKColor.Parse("#E0353B");
var redTop = SKColor.Parse("#F25A5F");
var yellow = SKColor.Parse("#F7B500");

// Рисунок в координатах 256×256 (как в app-icon.svg).
void Draw(SKCanvas canvas)
{
    using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
    using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

    // Фон: графитовый скруглённый квадрат до самых краёв — без полей значок на панели задач не мельче соседних.
    fill.Color = graphite;
    canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, 256, 256), 44), fill);

    // Рисунок крупнее на 20%: база и кнопка занимают почти весь квадрат.
    canvas.Save();
    canvas.Translate(128, 124);
    canvas.Scale(1.2f);
    canvas.Translate(-130, -128);

    // База: красный цилиндр, крышка светлее, шов посередине.
    fill.Color = red;
    using (var body = SKPath.ParseSvgPathData("M54 70 v108 c0 18 34 30 74 30 s74 -12 74 -30 v-108 Z"))
    {
        canvas.DrawPath(body, fill);
    }

    fill.Color = redTop;
    canvas.DrawOval(new SKRect(54, 42, 202, 98), fill);
    stroke.Color = graphite.WithAlpha(90);
    stroke.StrokeWidth = 6;
    using (var seam = SKPath.ParseSvgPathData("M54 116 c0 18 34 30 74 30 s74 -12 74 -30"))
    {
        canvas.DrawPath(seam, stroke);
    }

    // Запуск: жёлтый круг с графитовой обводкой и треугольником.
    fill.Color = yellow;
    canvas.DrawCircle(182, 176, 48, fill);
    stroke.Color = graphite;
    stroke.StrokeWidth = 10;
    canvas.DrawCircle(182, 176, 48, stroke);
    fill.Color = graphite;
    using var play = SKPath.ParseSvgPathData("M166 150 L208 176 L166 202 Z");
    canvas.DrawPath(play, fill);
    canvas.Restore();
}

SKBitmap Render(int px)
{
    // Без предумножения альфы: так цвета пишутся и в PNG, и в BMP значка.
    var target = new SKBitmap(px, px, SKColorType.Bgra8888, SKAlphaType.Unpremul);
    using var canvas = new SKCanvas(target);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(px / 256f);
    Draw(canvas);
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
