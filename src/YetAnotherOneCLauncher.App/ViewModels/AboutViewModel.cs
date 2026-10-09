using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Сведения для окна «О программе»: из атрибутов сборки (Directory.Build.props) и среды выполнения.</summary>
public sealed class AboutViewModel
{
    private const int CommitLength = 7;

    public AboutViewModel()
        : this(typeof(AboutViewModel).Assembly)
    {
    }

    internal AboutViewModel(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "YetAnotherOneCLauncher";
        Author = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Author")?.Value ?? "—";

        // «0.1.0+<хеш коммита>»: версия и короткий хеш сборки отдельно.
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString() ?? "?";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        Version = plus < 0 ? informational : informational[..plus];
        var commit = plus < 0 ? null : informational[(plus + 1)..];
        Commit = commit is { Length: > CommitLength } ? commit[..CommitLength] : commit;

        var avalonia = typeof(Avalonia.Application).Assembly.GetName().Version;
        Environment = string.Join(
            System.Environment.NewLine,
            RuntimeInformation.FrameworkDescription,
            $"Avalonia {avalonia?.ToString(3)}",
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
    }

    public string Product { get; }

    public string Version { get; }

    /// <summary>Короткий хеш коммита, из которого собрана программа; <c>null</c> — неизвестен.</summary>
    public string? Commit { get; }

    public string VersionText => Commit is null ? $"Версия {Version}" : $"Версия {Version} (сборка {Commit})";

    public string Description { get; } = "Лаунчер информационных баз 1С:Предприятия";

    public string Author { get; }

    public string AuthorText => "Автор: " + Author;

    /// <summary>«Проверить обновления» — задаёт главное окно; <c>null</c> — кнопки нет.</summary>
    public ICommand? CheckForUpdatesCommand { get; init; }

    public bool CanCheckForUpdates => CheckForUpdatesCommand is not null;

    /// <summary>Среда выполнения: .NET, Avalonia, ОС — пригодится в сообщении об ошибке.</summary>
    public string Environment { get; }
}
