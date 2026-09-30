using System.Diagnostics;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>Время от запуска процесса — для замеров скорости открытия в логе.</summary>
public static class StartupClock
{
    private static readonly DateTime ProcessStartedAt = ReadStartTime();

    public static TimeSpan Elapsed => DateTime.Now - ProcessStartedAt;

    private static DateTime ReadStartTime()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return DateTime.Now;
        }
    }
}
