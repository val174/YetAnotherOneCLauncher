using System.Runtime.InteropServices;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Кнопки окна (свернуть, развернуть, закрыть) в Windows рисует сама система поверх продлённой в заголовок области окна;
/// где они стоят, сообщает DWM (задача #31: верхняя строка лаунчера — она же заголовок окна, меню не должно под них заходить).
/// </summary>
public static partial class WindowCaption
{
    private const int DwmwaCaptionButtonBounds = 5;

    /// <summary>
    /// Ширина кнопок окна в физических пикселях: они прижаты к правому краю, так что ширины хватает, чтобы отвести им место
    /// (координаты DWM — от края окна с невидимой рамкой, клиентская область от них сдвинута).
    /// </summary>
    /// <returns><c>false</c> — не Windows или система не ответила.</returns>
    public static bool TryGetButtonsWidth(nint windowHandle, out int width)
    {
        width = 0;
        if (!OperatingSystem.IsWindows() || windowHandle == 0)
        {
            return false;
        }

        try
        {
            if (DwmGetWindowAttribute(windowHandle, DwmwaCaptionButtonBounds, out var bounds, Marshal.SizeOf<Rect>()) != 0
                || bounds.Right <= bounds.Left)
            {
                return false;
            }

            width = bounds.Right - bounds.Left;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int size);
}
