using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Значок файла, как в Проводнике (SHGetFileInfo, крупный — 32×32): у .exe — свой, у .msc, ярлыков и прочих —
/// значок их типа. Пиксели берутся из цветной картинки значка; у старых значков без прозрачности она берётся из маски.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsFileIconReader : IFileIconReader
{
    private const uint ShgfiIcon = 0x100, ShgfiLargeIcon = 0x0;
    private const uint DibRgbColors = 0;
    private const uint CoinitApartmentThreaded = 0x2;

    private static readonly Lock Gate = new();

    public IconPixels? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        // Одновременные вызовы SHGetFileInfo из разных потоков возвращают пустой значок — по одному.
        lock (Gate)
        {
            // Значки по типу файла (.msc, ярлыки) дают расширения оболочки — им нужен COM в потоке.
            var com = CoInitializeEx(0, CoinitApartmentThreaded);
            try
            {
                return ReadShellIcon(path);
            }
            finally
            {
                if (com >= 0)
                {
                    CoUninitialize();
                }
            }
        }
    }

    private static IconPixels? ReadShellIcon(string path)
    {
        var info = default(ShFileInfo);
        if (SHGetFileInfoW(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) == 0 || info.Icon == 0)
        {
            return null;
        }

        try
        {
            return ReadIcon(info.Icon);
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private static IconPixels? ReadIcon(nint icon)
    {
        if (!GetIconInfo(icon, out var iconInfo))
        {
            return null;
        }

        try
        {
            if (iconInfo.Color == 0 || GetObjectW(iconInfo.Color, Marshal.SizeOf<BitmapInfoObject>(), out var bitmap) == 0)
            {
                return null;
            }

            int width = bitmap.Width, height = bitmap.Height;
            var pixels = ReadBits(iconInfo.Color, width, height);
            if (pixels is null)
            {
                return null;
            }

            // Старый значок без прозрачности: альфа нулевая везде — прозрачность из маски (белое в маске — прозрачно).
            if (!HasAlpha(pixels) && ReadBits(iconInfo.Mask, width, height) is { } mask)
            {
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    pixels[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
                }
            }

            return new IconPixels(width, height, pixels);
        }
        finally
        {
            DeleteObject(iconInfo.Color);
            DeleteObject(iconInfo.Mask);
        }
    }

    private static byte[]? ReadBits(nint bitmap, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // сверху вниз
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var dc = GetDC(0);
        try
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, DibRgbColors) == height ? pixels : null;
        }
        finally
        {
            _ = ReleaseDC(0, dc);
        }
    }

    private static bool HasAlpha(byte[] pixels)
    {
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public int HotspotX;
        public int HotspotY;
        public nint Mask;
        public nint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoObject
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SHGetFileInfoW(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("gdi32.dll")]
    private static partial int GetObjectW(nint handle, int size, out BitmapInfoObject bitmap);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint window, nint dc);
}
