using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Launching;

/// <summary>Что делать для запуска базы.</summary>
public abstract record LaunchPlan
{
    private LaunchPlan()
    {
    }

    /// <summary>Запустить процесс платформы.</summary>
    public sealed record Run(LaunchCommand Command, IReadOnlyList<string> Warnings) : LaunchPlan;

    /// <summary>
    /// Нужной версии нет; можно запустить на другой, но только после подтверждения пользователя.
    /// </summary>
    public sealed record ConfirmFallback(string Question, LaunchCommand Command, IReadOnlyList<string> Warnings) : LaunchPlan;

    /// <summary>Открыть базу в браузере (веб-клиент).</summary>
    public sealed record OpenInBrowser(Uri Url) : LaunchPlan;

    /// <summary>Запустить нельзя.</summary>
    public sealed record Failed(string Message) : LaunchPlan;
}

/// <summary>
/// Решает, как запустить базу: выбирает клиент, платформу и формирует командную строку.
/// Ничего не запускает сам — это делает слой Platform.
/// </summary>
public static class LaunchPlanner
{
    private const string EnterpriseKeyword = "ENTERPRISE";
    private const string DesignerKeyword = "DESIGNER";

    public static LaunchPlan Plan(
        LaunchRequest request,
        IReadOnlyList<PlatformInstallation> installations,
        string? starterDefaultVersion,
        LaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(installations);
        options ??= new LaunchOptions();

        var infoBase = request.InfoBase;
        var connection = infoBase.Connection;
        if (connection.Kind == ConnectionKind.None)
        {
            return new LaunchPlan.Failed($"У базы «{infoBase.Name}» не задана строка подключения.");
        }

        var client = ResolveClient(request, options);
        if (client == ClientApp.WebClient)
        {
            return PlanWebClient(infoBase);
        }

        var executable = client == ClientApp.ThinClient ? PlatformExecutable.ThinClient : PlatformExecutable.ThickClient;
        // Разрядность: из свойств базы (AppArch), иначе из настроек лаунчера.
        var (architecture, architectureRequired) = AppArchitectures.Resolve(infoBase.Architecture, options.PreferredArchitecture);
        var selection = PlatformSelector.Select(
            installations, executable, infoBase.Version, starterDefaultVersion, architecture, request.PlatformVersionOverride, architectureRequired);

        // При App=Auto тонкий клиент необязателен: если его нет, подойдёт толстый.
        var clientIsAutomatic = request.ClientOverride is null && infoBase.App == ClientApp.Auto;
        if (selection.Status != PlatformSelectionStatus.Selected
            && executable == PlatformExecutable.ThinClient
            && clientIsAutomatic)
        {
            var thick = PlatformSelector.Select(
                installations, PlatformExecutable.ThickClient, infoBase.Version, starterDefaultVersion, architecture, request.PlatformVersionOverride, architectureRequired);
            if (thick.Status == PlatformSelectionStatus.Selected || selection.Status == PlatformSelectionStatus.NothingInstalled)
            {
                (selection, executable) = (thick, PlatformExecutable.ThickClient);
            }
        }

        if (selection.Status == PlatformSelectionStatus.NothingInstalled || selection.Installation is null)
        {
            return new LaunchPlan.Failed(architectureRequired && installations.Any(i => i.Has(executable))
                ? $"Для «{infoBase.Name}» в свойствах базы указана разрядность: только {AppArchitectures.Describe(architecture)} платформа, "
                  + $"а такая не установлена. Установите её или выберите другую разрядность в свойствах базы."
                : NothingInstalledMessage(executable, selection));
        }

        var command = BuildCommand(request, selection.Installation, executable);
        if (selection.Status == PlatformSelectionStatus.MaskNotInstalled)
        {
            var question =
                $"Платформа {selection.Mask} ({MaskSourceText(selection.MaskSource)}) не установлена. " +
                $"Запустить «{infoBase.Name}» на {selection.Installation}?";
            return new LaunchPlan.ConfirmFallback(question, command, selection.Warnings);
        }

        return new LaunchPlan.Run(command, selection.Warnings);
    }

    /// <summary>Формирует команду для выбранной платформы.</summary>
    public static LaunchCommand BuildCommand(
        LaunchRequest request,
        PlatformInstallation installation,
        PlatformExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(installation);

        var executablePath = installation.GetExecutablePath(executable)
                             ?? throw new ArgumentException($"В платформе {installation} нет {executable}.", nameof(executable));
        var infoBase = request.InfoBase;
        var arguments = new List<string>
        {
            request.Mode == LaunchMode.Designer ? DesignerKeyword : EnterpriseKeyword,
        };

        arguments.AddRange(ConnectionArguments(infoBase.Connection));

        switch (infoBase.WindowsAuthentication)
        {
            case true:
                arguments.Add("/WA+");
                break;
            case false:
                arguments.Add("/WA-");
                break;
        }

        if (request.Mode == LaunchMode.Enterprise && NormalizeSpeed(infoBase.ClientConnectionSpeed) is { } speed)
        {
            arguments.Add("/O");
            arguments.Add(speed);
        }

        if (!string.IsNullOrEmpty(request.UserName))
        {
            arguments.Add("/N");
            arguments.Add(request.UserName);
        }

        if (request.Password is not null)
        {
            arguments.Add("/P");
            arguments.Add(request.Password);
        }

        // Порядок: AdditionalParameters базы, параметры лаунчера (папки, база, разовые), дополнительные аргументы.
        // Всё это идёт после сырого AdditionalParameters, поэтому в Windows аргументы нельзя поставить в общий
        // список до хвоста: они добавляются к хвосту в экранированном виде.
        IEnumerable<string?> parts =
        [
            infoBase.AdditionalParameters,
            .. request.ParameterFragments,
            request.ExtraArguments.Count > 0 ? OneCCommandLine.Format(request.ExtraArguments) : null,
        ];
        var raw = string.Join(' ', parts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

        return new LaunchCommand(executablePath, arguments, string.IsNullOrWhiteSpace(raw) ? null : raw, installation);
    }

    private static ClientApp ResolveClient(LaunchRequest request, LaunchOptions options)
    {
        // Конфигуратор есть только в толстом клиенте.
        if (request.Mode == LaunchMode.Designer)
        {
            return ClientApp.ThickClient;
        }

        var client = request.ClientOverride ?? request.InfoBase.App;
        if (client != ClientApp.Auto)
        {
            return client;
        }

        return request.InfoBase.ConnectionKind == ConnectionKind.File && options.UseThickClientForFileBasesByDefault
            ? ClientApp.ThickClient
            : ClientApp.ThinClient;
    }

    private static LaunchPlan PlanWebClient(InfoBase infoBase)
    {
        if (infoBase.ConnectionKind != ConnectionKind.Web)
        {
            return new LaunchPlan.Failed(
                $"Веб-клиент доступен только для баз, опубликованных на веб-сервере; «{infoBase.Name}» к ним не относится.");
        }

        return Uri.TryCreate(infoBase.Connection.WebUrl, UriKind.Absolute, out var url)
               && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            ? new LaunchPlan.OpenInBrowser(url)
            : new LaunchPlan.Failed($"Некорректный адрес базы «{infoBase.Name}»: {infoBase.Connection.WebUrl}");
    }

    /// <summary>
    /// Для обычных строк подключения — короткие ключи <c>/F</c>, <c>/S</c>, <c>/WS</c>;
    /// если в строке есть что-то ещё (например, учётные данные веб-сервера), она передаётся целиком.
    /// </summary>
    private static IEnumerable<string> ConnectionArguments(ConnectionString connection)
    {
        var keys = connection.Parts.Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Only(params string[] expected) => keys.SetEquals(expected);

        return connection.Kind switch
        {
            ConnectionKind.File when Only(ConnectionString.FileKey) =>
                ["/F", connection.FilePath!],
            ConnectionKind.Server when Only(ConnectionString.ServerKey, ConnectionString.InfobaseRefKey)
                                       && !string.IsNullOrEmpty(connection.InfobaseName) =>
                ["/S", $"{connection.Server}\\{connection.InfobaseName}"],
            ConnectionKind.Web when Only(ConnectionString.WebKey) =>
                ["/WS", connection.WebUrl!],
            _ => ["/IBConnectionString", connection.ToString()],
        };
    }

    private static string? NormalizeSpeed(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "normal" => "Normal",
        "low" => "Low",
        _ => null,
    };

    private static string NothingInstalledMessage(PlatformExecutable executable, PlatformSelection selection)
    {
        var what = executable == PlatformExecutable.ThinClient ? "тонкий клиент (1cv8c)" : "толстый клиент или Конфигуратор (1cv8)";
        return selection.Mask is null
            ? $"Не найдена установленная платформа 1С, в которой есть {what}."
            : $"Не найдена установленная платформа 1С, в которой есть {what}. Нужна версия {selection.Mask} ({MaskSourceText(selection.MaskSource)}).";
    }

    private static string MaskSourceText(VersionMaskSource source) => source switch
    {
        VersionMaskSource.InfoBase => "указана у базы",
        VersionMaskSource.StarterDefault => "версия по умолчанию из 1cestart.cfg",
        VersionMaskSource.UserOverride => "выбрана в лаунчере",
        _ => "не задана",
    };
}
