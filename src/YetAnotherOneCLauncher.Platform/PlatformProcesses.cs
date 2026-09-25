using System.Diagnostics;

namespace YetAnotherOneCLauncher.Platform;

/// <summary>Процессы платформы: толстый и тонкий клиент, Конфигуратор.</summary>
internal static class PlatformProcesses
{
    private static readonly string[] Names = ["1cv8", "1cv8c"];

    public static IReadOnlyList<string> List()
    {
        var result = new List<string>();
        foreach (var name in Names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    result.Add($"{name} (PID {process.Id})");
                }
            }
        }

        return result;
    }
}
