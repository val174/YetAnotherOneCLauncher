using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Editing;

/// <summary>Что делать при добавлении базы — как в штатном стартере 1С.</summary>
public enum InfoBaseAddMode
{
    /// <summary>Добавить в список существующую информационную базу.</summary>
    Existing,

    /// <summary>Создать информационную базу из шаблона (.cf или .dt).</summary>
    FromTemplate,

    /// <summary>Создать базу без конфигурации — для разработки новой или загрузки выгруженной ранее.</summary>
    Empty,
}

/// <summary>Тип СУБД клиент-серверной базы (ключ <c>DBMS</c>).</summary>
public enum DbmsType
{
    MSSQLServer,
    PostgreSQL,
    IBMDB2,
    OracleDatabase,
}

/// <summary>Защищённое соединение с кластером (<c>/SLev0</c>, <c>/SLev1</c>, <c>/SLev2</c>).</summary>
public enum SecureConnectionLevel
{
    Off = 0,
    ConnectionOnly = 1,
    Always = 2,
}

/// <summary>Язык (страна) новой базы: код для ключа <c>Locale</c> и название для списка.</summary>
public sealed record InfoBaseLocale(string Code, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Параметры создания информационной базы командой <c>1cv8 CREATEINFOBASE</c>: файловой — в локальном или
/// сетевом каталоге, клиент-серверной — на сервере 1С:Предприятия (параметры — как в штатном стартере).
/// </summary>
public sealed record InfoBaseCreation
{
    public const string DefaultLocale = "ru_RU";

    /// <summary>Только <see cref="ConnectionKind.File"/> или <see cref="ConnectionKind.Server"/>.</summary>
    public ConnectionKind Kind { get; init; } = ConnectionKind.File;

    public string FilePath { get; init; } = string.Empty;

    /// <summary>Кластер серверов 1С:Предприятия (<c>Srvr</c>).</summary>
    public string Server { get; init; } = string.Empty;

    /// <summary>Имя информационной базы в кластере (<c>Ref</c>).</summary>
    public string InfobaseName { get; init; } = string.Empty;

    public SecureConnectionLevel SecureConnection { get; init; }

    public DbmsType Dbms { get; init; } = DbmsType.MSSQLServer;

    /// <summary>Сервер баз данных (<c>DBSrvr</c>).</summary>
    public string DatabaseServer { get; init; } = string.Empty;

    /// <summary>Имя базы данных (<c>DB</c>).</summary>
    public string DatabaseName { get; init; } = string.Empty;

    /// <summary>Пользователь базы данных (<c>DBUID</c>); пусто — не указывать.</summary>
    public string DatabaseUser { get; init; } = string.Empty;

    /// <summary>Пароль пользователя базы данных (<c>DBPwd</c>). В логи попадает только замаскированным.</summary>
    public string DatabasePassword { get; init; } = string.Empty;

    /// <summary>Смещение дат (<c>SQLYOffs</c>): 0 или 2000, только для MS SQL Server.</summary>
    public int DateOffset { get; init; }

    /// <summary>Создать базу данных в случае её отсутствия (<c>CrSQLDB</c>).</summary>
    public bool CreateDatabase { get; init; } = true;

    /// <summary>Установить блокировку регламентных заданий (<c>SchJobDn</c>).</summary>
    public bool BlockScheduledJobs { get; init; }

    /// <summary>Запретить локальное распознавание речи (<c>disstt</c>, платформа 8.3.24 и новее).</summary>
    public bool DisableLocalSpeechToText { get; init; }

    /// <summary>Язык (страна) базы (<c>Locale</c>).</summary>
    public string Locale { get; init; } = DefaultLocale;

    /// <summary>Шаблон — файл конфигурации (.cf) или выгрузки (.dt); <c>null</c> — база без конфигурации.</summary>
    public string? TemplatePath { get; init; }

    /// <summary>Языки (страны) для выбора — как в штатном стартере, основные.</summary>
    public static IReadOnlyList<InfoBaseLocale> Locales { get; } =
    [
        new("ru_RU", "русский (Россия)"),
        new("en_US", "английский (США)"),
        new("en_GB", "английский (Великобритания)"),
        new("be_BY", "белорусский (Беларусь)"),
        new("kk_KZ", "казахский (Казахстан)"),
        new("ru_KZ", "русский (Казахстан)"),
        new("uz_UZ", "узбекский (Узбекистан)"),
        new("ky_KG", "киргизский (Киргизия)"),
        new("ru_KG", "русский (Киргизия)"),
        new("tg_TJ", "таджикский (Таджикистан)"),
        new("tk_TM", "туркменский (Туркменистан)"),
        new("hy_AM", "армянский (Армения)"),
        new("az_AZ", "азербайджанский (Азербайджан)"),
        new("ka_GE", "грузинский (Грузия)"),
        new("ro_MD", "румынский (Молдова)"),
        new("uk_UA", "украинский (Украина)"),
        new("lv_LV", "латышский (Латвия)"),
        new("lt_LT", "литовский (Литва)"),
        new("et_EE", "эстонский (Эстония)"),
        new("de_DE", "немецкий (Германия)"),
        new("pl_PL", "польский (Польша)"),
        new("bg_BG", "болгарский (Болгария)"),
        new("tr_TR", "турецкий (Турция)"),
        new("vi_VN", "вьетнамский (Вьетнам)"),
    ];

    /// <summary>Что не так с параметрами; пустой список — можно создавать.</summary>
    /// <param name="directoryHasFiles">Есть ли в каталоге файлы (по умолчанию — проверка на диске).</param>
    public IReadOnlyList<string> Validate(Func<string, bool>? directoryHasFiles = null)
    {
        directoryHasFiles ??= DirectoryHasFiles;
        var errors = new List<string>();
        switch (Kind)
        {
            case ConnectionKind.File when string.IsNullOrWhiteSpace(FilePath):
                errors.Add("Укажите каталог новой информационной базы.");
                break;
            case ConnectionKind.File when directoryHasFiles(FilePath.Trim()):
                errors.Add("Каталог не пуст: для новой базы выберите пустой или несуществующий каталог.");
                break;
            case ConnectionKind.Server:
                if (string.IsNullOrWhiteSpace(Server))
                {
                    errors.Add("Укажите кластер серверов 1С:Предприятия.");
                }

                if (string.IsNullOrWhiteSpace(InfobaseName))
                {
                    errors.Add("Укажите имя информационной базы в кластере.");
                }

                if (string.IsNullOrWhiteSpace(DatabaseServer))
                {
                    errors.Add("Укажите сервер баз данных.");
                }

                if (string.IsNullOrWhiteSpace(DatabaseName))
                {
                    errors.Add("Укажите имя базы данных.");
                }

                break;
            case not ConnectionKind.File:
                errors.Add("Новую базу можно создать только в каталоге или на сервере 1С:Предприятия.");
                break;
        }

        if (TemplatePath is not null)
        {
            if (string.IsNullOrWhiteSpace(TemplatePath))
            {
                errors.Add("Выберите шаблон информационной базы.");
            }
            else if (!File.Exists(TemplatePath))
            {
                errors.Add($"Файл шаблона не найден: {TemplatePath}");
            }
        }

        if (string.IsNullOrWhiteSpace(Locale))
        {
            errors.Add("Выберите язык (страну) информационной базы.");
        }

        return errors;
    }

    /// <summary>Строка соединения для <c>CREATEINFOBASE</c>.</summary>
    /// <param name="maskPasswords">Пароль базы данных — звёздочками (для лога и интерфейса).</param>
    public ConnectionString BuildCreateConnection(bool maskPasswords = false)
    {
        if (Kind != ConnectionKind.Server)
        {
            var file = ConnectionString.ForFile(FilePath.Trim());
            file["Locale"] = Locale;
            return file;
        }

        var server = ConnectionString.ForServer(Server.Trim(), InfobaseName.Trim());
        server["DBMS"] = Dbms.ToString();
        server["DBSrvr"] = DatabaseServer.Trim();
        server["DB"] = DatabaseName.Trim();
        if (!string.IsNullOrWhiteSpace(DatabaseUser))
        {
            server["DBUID"] = DatabaseUser.Trim();
        }

        if (!string.IsNullOrEmpty(DatabasePassword))
        {
            server["DBPwd"] = maskPasswords ? "***" : DatabasePassword;
        }

        if (Dbms == DbmsType.MSSQLServer)
        {
            server["SQLYOffs"] = DateOffset == 2000 ? "2000" : "0";
        }

        server["CrSQLDB"] = CreateDatabase ? "Y" : "N";
        server["SchJobDn"] = BlockScheduledJobs ? "Y" : "N";
        if (DisableLocalSpeechToText)
        {
            server["disstt"] = "Y";
        }

        server["Locale"] = Locale;
        return server;
    }

    /// <summary>Строка подключения для списка баз: только расположение (<c>File</c> или <c>Srvr</c> и <c>Ref</c>).</summary>
    public ConnectionString BuildListConnection() => Kind == ConnectionKind.Server
        ? ConnectionString.ForServer(Server.Trim(), InfobaseName.Trim())
        : ConnectionString.ForFile(FilePath.Trim());

    /// <summary>
    /// Команда создания: <c>1cv8 CREATEINFOBASE &lt;строка соединения&gt; [/UseTemplate файл] [/SLevN]
    /// /DisableStartupDialogs /Out файл</c>. В список баз платформа базу не добавляет — это делает лаунчер.
    /// </summary>
    /// <param name="platform">Платформа с толстым клиентом (1cv8): только он создаёт базы.</param>
    /// <param name="outFile">Файл, куда платформа запишет результат.</param>
    /// <param name="maskPasswords">Для лога и интерфейса: пароль базы данных скрыт.</param>
    public LaunchCommand BuildCommand(PlatformInstallation platform, string outFile, bool maskPasswords = false)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var executable = platform.ThickClientPath
                         ?? throw new InvalidOperationException($"У платформы {platform} нет 1cv8 — создать базу нельзя.");
        var arguments = new List<string> { "CREATEINFOBASE", BuildCreateConnection(maskPasswords).ToString() };
        if (!string.IsNullOrWhiteSpace(TemplatePath))
        {
            arguments.AddRange(["/UseTemplate", TemplatePath]);
        }

        if (Kind == ConnectionKind.Server && SecureConnection != SecureConnectionLevel.Off)
        {
            arguments.Add("/SLev" + (int)SecureConnection);
        }

        arguments.AddRange(["/DisableStartupDialogs", "/Out", outFile]);
        return new LaunchCommand(executable, arguments, null, platform);
    }

    private static bool DirectoryHasFiles(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false; // пусть ответит платформа
        }
    }
}
