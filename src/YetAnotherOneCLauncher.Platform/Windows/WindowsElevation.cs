using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>Права пользователя: может ли он запускать программы с правами администратора.</summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsElevation
{
    private const int TokenElevationTypeClass = 18;
    private const int TokenElevationTypeFull = 2;
    private const int TokenElevationTypeLimited = 3;

    /// <summary>
    /// Администратор — уже с повышенными правами или работающий без них при включённом UAC (MMC у такого
    /// пользователя запускается с повышением). Обычный пользователь — <c>false</c>.
    /// </summary>
    public static bool CanElevate()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            return true;
        }

        return GetTokenInformation(identity.Token, TokenElevationTypeClass, out var type, sizeof(int), out _)
               && type is TokenElevationTypeFull or TokenElevationTypeLimited;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int informationClass, out int information, int length, out int returnLength);
}
