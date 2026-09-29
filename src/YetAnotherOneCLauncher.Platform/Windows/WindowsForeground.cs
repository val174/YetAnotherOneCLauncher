using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Передача права вывести окно на передний план. Windows не даёт фоновому процессу перекрыть окно, с которым
/// работает пользователь (окно лишь мигает на панели задач). Повторно запущенный лаунчер это право имеет —
/// его только что открыл пользователь — и отдаёт его уже работающему, прежде чем попросить показать окно.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsForeground
{
    private const int AsfwAny = -1;

    /// <summary>Разрешить вывести окно на передний план любому процессу (действует до следующего ввода пользователя).</summary>
    public static void AllowAnyProcess() => AllowSetForegroundWindow(AsfwAny);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
