using System.ComponentModel;
using System.Diagnostics;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Linux;

/// <summary>
/// Пароли в связке ключей рабочего стола (GNOME Keyring, KWallet) через <c>secret-tool</c> из libsecret.
/// Пароль передаётся через стандартный ввод, а не в аргументах: аргументы видны другим процессам.
/// Если <c>secret-tool</c> не установлен, хранилище недоступно — лаунчер предлагает вводить пароль при запуске.
/// </summary>
public sealed class SecretToolCredentialStore : ICredentialStore
{
    private const string Tool = "secret-tool";
    private const string ApplicationAttribute = "application";
    private const string KeyAttribute = "key";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Lazy<string?> _toolPath = new(FindTool);

    public string? UnavailableReason => _toolPath.Value is null
        ? "Чтобы сохранять пароли, установите secret-tool (пакет libsecret-tools или libsecret)."
        : null;

    public string? Read(string key)
    {
        var (exitCode, output, error) = Run(["lookup", ApplicationAttribute, PlatformServices.AppFolderName, KeyAttribute, key], input: null);
        return exitCode switch
        {
            0 => output,
            // Код 1 без сообщения — записи нет.
            1 when string.IsNullOrWhiteSpace(error) => null,
            _ => throw Failure("прочитать", error),
        };
    }

    public void Write(string key, string label, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var (exitCode, _, error) = Run(
            ["store", "--label=" + label, ApplicationAttribute, PlatformServices.AppFolderName, KeyAttribute, key],
            input: password);
        if (exitCode != 0)
        {
            throw Failure("сохранить", error);
        }
    }

    public void Delete(string key)
    {
        var (exitCode, _, error) = Run(["clear", ApplicationAttribute, PlatformServices.AppFolderName, KeyAttribute, key], input: null);
        if (exitCode != 0 && !string.IsNullOrWhiteSpace(error))
        {
            throw Failure("удалить", error);
        }
    }

    private (int ExitCode, string Output, string Error) Run(IReadOnlyList<string> arguments, string? input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arguments[^1], "key");
        var toolPath = _toolPath.Value ?? throw new CredentialStoreException(UnavailableReason!);
        var startInfo = new ProcessStartInfo(toolPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo) ?? throw new CredentialStoreException("secret-tool не запустился.");
            if (input is not null)
            {
                process.StandardInput.Write(input);
            }

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                throw new CredentialStoreException("secret-tool не ответил: возможно, связка ключей заблокирована.");
            }

            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult().Trim());
        }
        catch (Win32Exception ex)
        {
            throw new CredentialStoreException("Не удалось запустить secret-tool: " + ex.Message, ex);
        }
    }

    private static CredentialStoreException Failure(string action, string error) =>
        new($"Не удалось {action} пароль в связке ключей: {(string.IsNullOrWhiteSpace(error) ? "secret-tool завершился с ошибкой" : error)}");

    private static string? FindTool() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, Tool))
        .FirstOrDefault(File.Exists);
}
