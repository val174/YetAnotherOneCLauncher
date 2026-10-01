using System.ComponentModel;
using System.Diagnostics;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform;

/// <summary>
/// Запуск платформы. В Windows командная строка собирается по правилам 1С
/// (<see cref="LaunchCommand.ToWindowsArguments"/>): стандартное экранирование .NET (<c>\"</c>) 1С не понимает.
/// В Linux аргументы передаются по одному, без оболочки.
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public int Start(LaunchCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return StartDetached(StartInfo(command), $"Не удалось запустить {command.ExecutablePath}");
    }

    public async Task<int> RunAsync(LaunchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        Process process;
        try
        {
            process = Process.Start(StartInfo(command))
                      ?? throw new LaunchFailedException($"Не удалось запустить {command.ExecutablePath}: процесс не создан.");
        }
        catch (Win32Exception ex)
        {
            throw new LaunchFailedException($"Не удалось запустить {command.ExecutablePath}: {ex.Message}", ex);
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    private static ProcessStartInfo StartInfo(LaunchCommand command)
    {
        var startInfo = new ProcessStartInfo(command.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = command.Platform.BinDirectory,
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.Arguments = command.ToWindowsArguments();
        }
        else
        {
            foreach (var argument in command.ToArgumentVector())
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        return startInfo;
    }

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }
            : new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { url.AbsoluteUri } };

        StartDetached(startInfo, $"Не удалось открыть {url} в браузере");
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        // Без завершающего разделителя: иначе .NET экранирует его перед закрывающей кавычкой, а explorer.exe этого не понимает.
        path = Path.TrimEndingDirectorySeparator(path);
        if (!Directory.Exists(path))
        {
            throw new LaunchFailedException($"Каталог не найден: {path}");
        }

        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { path } }
            : new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { path } };

        StartDetached(startInfo, $"Не удалось открыть каталог {path}");
    }

    public void StartProgram(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new LaunchFailedException($"Файл не найден: {path}");
        }

        var startInfo = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty };
        StartDetached(startInfo, $"Не удалось запустить {path}");
    }

    private static int StartDetached(ProcessStartInfo startInfo, string errorPrefix)
    {
        try
        {
            using var process = Process.Start(startInfo)
                                ?? throw new LaunchFailedException(errorPrefix + ": процесс не создан.");
            return process.Id;
        }
        catch (Win32Exception ex)
        {
            throw new LaunchFailedException($"{errorPrefix}: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new LaunchFailedException($"{errorPrefix}: {ex.Message}", ex);
        }
    }
}
