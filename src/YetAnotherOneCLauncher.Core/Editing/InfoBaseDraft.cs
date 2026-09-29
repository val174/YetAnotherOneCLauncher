using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Editing;

/// <summary>
/// Данные базы для добавления или изменения. Для существующей базы хранит исходную строку подключения,
/// чтобы не потерять её дополнительные ключи (например, <c>wsn</c>) при смене пути или сервера.
/// </summary>
public sealed record InfoBaseDraft
{
    public string Name { get; init; } = string.Empty;

    public ConnectionKind Kind { get; init; } = ConnectionKind.File;

    public string FilePath { get; init; } = string.Empty;

    public string Server { get; init; } = string.Empty;

    public string InfobaseName { get; init; } = string.Empty;

    public string WebUrl { get; init; } = string.Empty;

    public string FolderPath { get; init; } = FolderPaths.Root;

    public ClientApp App { get; init; } = ClientApp.Auto;

    /// <summary>Маска версии: "8.3", "8.3.24", "8.3.24.1548" или пусто.</summary>
    public string? Version { get; init; }

    /// <summary>Разрядность клиента (<c>AppArch</c>); <see cref="AppArchitecture.Auto"/> — ключа нет.</summary>
    public AppArchitecture Architecture { get; init; }

    /// <summary>Аутентификация Windows; <c>null</c> — ключ не писать.</summary>
    public bool? WindowsAuthentication { get; init; } = true;

    public string? AdditionalParameters { get; init; }

    /// <summary>Исходная строка подключения изменяемой базы; <c>null</c> для новой.</summary>
    public ConnectionString? OriginalConnection { get; init; }

    public static InfoBaseDraft From(InfoBase infoBase)
    {
        ArgumentNullException.ThrowIfNull(infoBase);
        var connection = infoBase.Connection;
        return new InfoBaseDraft
        {
            Name = infoBase.Name,
            Kind = connection.Kind is ConnectionKind.None or ConnectionKind.Unknown ? ConnectionKind.File : connection.Kind,
            FilePath = connection.FilePath ?? string.Empty,
            Server = connection.Server ?? string.Empty,
            InfobaseName = connection.InfobaseName ?? string.Empty,
            WebUrl = connection.WebUrl ?? string.Empty,
            FolderPath = infoBase.FolderPath,
            App = infoBase.App,
            Version = infoBase.Version,
            Architecture = infoBase.Architecture,
            WindowsAuthentication = infoBase.WindowsAuthentication,
            AdditionalParameters = infoBase.AdditionalParameters,
            OriginalConnection = connection,
        };
    }

    /// <summary>
    /// Строка подключения: ключи выбранного вида заполняются, ключи других видов удаляются,
    /// остальные ключи исходной строки сохраняются.
    /// </summary>
    public ConnectionString BuildConnection()
    {
        var connection = ConnectionString.Parse(OriginalConnection?.ToString());
        foreach (var key in new[] { ConnectionString.FileKey, ConnectionString.ServerKey, ConnectionString.InfobaseRefKey, ConnectionString.WebKey })
        {
            connection[key] = null;
        }

        var result = Kind switch
        {
            ConnectionKind.Server => ConnectionString.ForServer(Server.Trim(), InfobaseName.Trim()),
            ConnectionKind.Web => ConnectionString.ForWeb(WebUrl.Trim()),
            _ => ConnectionString.ForFile(FilePath.Trim()),
        };

        foreach (var (key, value) in connection.Parts)
        {
            result[key] = value;
        }

        return result;
    }

    /// <summary>Что не так с данными; пустой список — можно сохранять.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Укажите название базы.");
        }
        else if (Name.Any(c => c is '\r' or '\n'))
        {
            errors.Add("Название не может содержать перевод строки.");
        }

        switch (Kind)
        {
            case ConnectionKind.File when string.IsNullOrWhiteSpace(FilePath):
                errors.Add("Укажите каталог файловой базы.");
                break;
            case ConnectionKind.Server when string.IsNullOrWhiteSpace(Server):
                errors.Add("Укажите кластер серверов 1С.");
                break;
            case ConnectionKind.Server when string.IsNullOrWhiteSpace(InfobaseName):
                errors.Add("Укажите имя базы в кластере.");
                break;
            case ConnectionKind.Web when !Uri.TryCreate(WebUrl.Trim(), UriKind.Absolute, out var url)
                                         || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps):
                errors.Add("Укажите адрес базы на веб-сервере: http://… или https://….");
                break;
            case ConnectionKind.None or ConnectionKind.Unknown:
                errors.Add("Выберите вариант подключения.");
                break;
        }

        if (!string.IsNullOrWhiteSpace(Version) && !VersionMask.TryParse(Version, out _))
        {
            errors.Add("Версия платформы — числа через точку: 8.3, 8.3.24 или 8.3.24.1548.");
        }

        if (App == ClientApp.WebClient && Kind != ConnectionKind.Web)
        {
            errors.Add("Веб-клиент возможен только для базы на веб-сервере.");
        }

        return errors;
    }
}
