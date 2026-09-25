using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>Корзина Windows через <c>SHFileOperation</c> с <c>FOF_ALLOWUNDO</c>.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsRecycleBin : IRecycleBin
{
    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;

    // Если каталог не помещается в корзину, Windows спросит, удалить ли его насовсем, а не удалит молча.
    private const ushort FofWantNukeWarning = 0x4000;

    public void MoveToRecycleBin(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Environment.Is64BitProcess)
        {
            // В 32-разрядной Windows структура упакована иначе; лаунчер выпускается только 64-разрядным.
            throw new IOException("Корзина поддерживается только в 64-разрядной сборке лаунчера.");
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var from = Marshal.StringToHGlobalUni(fullPath + '\0'); // список путей заканчивается двумя нулями
        try
        {
            var operation = new FileOperation
            {
                Function = FoDelete,
                From = from,
                Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning,
            };
            var result = SHFileOperation(ref operation);
            if (result != 0)
            {
                throw new IOException($"Не удалось переместить в корзину {fullPath} (код {result}).");
            }

            if (operation.AnyOperationsAborted != 0 || Path.Exists(fullPath))
            {
                throw new IOException($"Перемещение в корзину отменено: {fullPath}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref FileOperation operation);

    /// <summary>Структура SHFILEOPSTRUCTW (64-разрядная раскладка).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileOperation
    {
        public nint Window;
        public uint Function;
        public nint From;
        public nint To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public nint ProgressTitle;
    }
}
