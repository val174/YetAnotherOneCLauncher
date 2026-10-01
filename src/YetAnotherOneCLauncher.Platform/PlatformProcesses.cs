using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform;

/// <summary>Процессы платформы текущего пользователя: толстый и тонкий клиент, Конфигуратор — с командными строками.</summary>
internal static partial class PlatformProcesses
{
    private static readonly string[] Names = ["1cv8", "1cv8c"];

    public static IReadOnlyList<PlatformProcess> CurrentUser()
    {
        var result = new List<PlatformProcess>();
        foreach (var name in Names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    var commandLine = OperatingSystem.IsWindows() ? WindowsCommandLine(process.Id)
                        : OperatingSystem.IsLinux() ? LinuxCommandLine(process.Id)
                        : null;
                    if (commandLine is not null)
                    {
                        result.Add(new PlatformProcess(process.Id, name, commandLine));
                    }
                }
            }
        }

        return result;
    }

    // --- Windows: владелец — по токену процесса, командная строка — NtQueryInformationProcess. ---
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>Командная строка процесса текущего пользователя; чужой процесс или недоступный — <c>null</c>.</summary>
    [SupportedOSPlatform("windows")]
    private static string? WindowsCommandLine(int processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            return IsCurrentUser(process) ? ReadCommandLine(process) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsCurrentUser(nint process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            using var owner = new WindowsIdentity(token);
            using var current = WindowsIdentity.GetCurrent();
            return owner.User is not null && owner.User == current.User;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static unsafe string? ReadCommandLine(nint process)
    {
        var status = NtQueryInformationProcess(process, ProcessCommandLineInformation, 0, 0, out var length);
        if (status != StatusInfoLengthMismatch || length <= 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, length, out _) < 0)
            {
                return null;
            }

            // UNICODE_STRING: Length (байты), MaximumLength, указатель на текст.
            var text = (UnicodeString*)buffer;
            return text->Buffer == 0 ? string.Empty : new string((char*)text->Buffer, 0, text->Length / sizeof(char));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int informationClass, nint information, int length, out int returnLength);

    // --- Linux: владелец — Uid из /proc/<pid>/status, командная строка — /proc/<pid>/cmdline. ---

    private static string? LinuxCommandLine(int processId)
    {
        try
        {
            var directory = Path.Combine("/proc", processId.ToString(CultureInfo.InvariantCulture));
            if (UidOf(Path.Combine(directory, "status")) is not { } uid || uid != UidOf("/proc/self/status"))
            {
                return null;
            }

            // Аргументы разделены нулями; с пробелами — в кавычки, как в командной строке 1С.
            var args = File.ReadAllText(Path.Combine(directory, "cmdline"), Encoding.UTF8).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? "\"" + a.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : a));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? UidOf(string statusPath) =>
        File.ReadLines(statusPath).FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal))?
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
}
