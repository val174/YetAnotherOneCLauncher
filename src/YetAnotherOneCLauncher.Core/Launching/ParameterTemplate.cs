namespace YetAnotherOneCLauncher.Core.Launching;

/// <summary>Нужно ли шаблону значение и какое.</summary>
public enum ParameterValueKind
{
    /// <summary>Ключ без значения: <c>/ClearCache</c>.</summary>
    None,

    /// <summary>Ключ со строкой: <c>/UC код</c>.</summary>
    Text,

    /// <summary>Ключ с путём к файлу: <c>/Execute "C:\обработка.epf"</c>.</summary>
    File,
}

/// <summary>Готовый параметр командной строки 1С с описанием — для вставки в параметры запуска.</summary>
public sealed record ParameterTemplate
{
    /// <summary>Название для списка.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Текст параметра без значения, например <c>/UC</c>.</summary>
    public string Text { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public ParameterValueKind Value { get; init; }

    /// <summary>Фрагмент для вставки: ключ и значение (в кавычках, если нужно).</summary>
    public string Format(string? value)
    {
        if (Value == ParameterValueKind.None || string.IsNullOrWhiteSpace(value))
        {
            return Text;
        }

        return Text + " " + OneCCommandLine.Quote(value.Trim());
    }

    public override string ToString() => Name;
}

/// <summary>Встроенные шаблоны параметров и работа с текстом параметров.</summary>
public static class ParameterLibrary
{
    public static IReadOnlyList<ParameterTemplate> BuiltIn { get; } =
    [
        new()
        {
            Name = "Без стартовых сообщений",
            Text = "/DisableStartupMessages",
            Description = "Не показывать предупреждения при запуске, например о несовпадении версии платформы.",
        },
        new()
        {
            Name = "Без стартовых диалогов",
            Text = "/DisableStartupDialogs",
            Description = "Не показывать окно выбора пользователя и другие диалоги запуска. Если данных для входа не хватает, запуск завершится ошибкой.",
        },
        new()
        {
            Name = "Обычное приложение",
            Text = "/RunModeOrdinaryApplication",
            Description = "Толстый клиент в режиме обычного приложения — для конфигураций на обычных формах.",
        },
        new()
        {
            Name = "Управляемое приложение",
            Text = "/RunModeManagedApplication",
            Description = "Толстый клиент в режиме управляемого приложения.",
        },
        new()
        {
            Name = "Код доступа (/UC)",
            Text = "/UC",
            Value = ParameterValueKind.Text,
            Description = "Код разрешения входа, когда в базе установлена блокировка сеансов.",
        },
        new()
        {
            Name = "Параметр запуска (/C)",
            Text = "/C",
            Value = ParameterValueKind.Text,
            Description = "Строка, которую конфигурация получит в свойстве ПараметрЗапуска.",
        },
        new()
        {
            Name = "Открыть обработку (/Execute)",
            Text = "/Execute",
            Value = ParameterValueKind.File,
            Description = "Сразу после запуска открыть внешнюю обработку (.epf).",
        },
        new()
        {
            Name = "Язык интерфейса (/L)",
            Text = "/L",
            Value = ParameterValueKind.Text,
            Description = "Код языка интерфейса платформы: ru, en, uk…",
        },
        new()
        {
            Name = "Язык локализации (/VL)",
            Text = "/VL",
            Value = ParameterValueKind.Text,
            Description = "Код локализации сеанса — формат чисел и дат: ru_RU, en_US…",
        },
        new()
        {
            Name = "Искать аппаратный ключ",
            Text = "/UseHwLicenses+",
            Description = "Искать аппаратный ключ защиты (HASP).",
        },
        new()
        {
            Name = "Не искать аппаратный ключ",
            Text = "/UseHwLicenses-",
            Description = "Не искать аппаратный ключ защиты — запуск быстрее, если используются программные лицензии.",
        },
        new()
        {
            Name = "Очистить кэш",
            Text = "/ClearCache",
            Description = "Очистить кэш клиент-серверных вызовов и метаданных при запуске.",
        },
        new()
        {
            Name = "Без входа по пользователю Windows (/WA-)",
            Text = "/WA-",
            Description = "Не входить по пользователю Windows (аутентификация ОС) — спросить имя и пароль 1С.",
        },
        new()
        {
            Name = "Вход по пользователю Windows (/WA+)",
            Text = "/WA+",
            Description = "Входить по пользователю Windows (аутентификация ОС), если это разрешено в базе.",
        },
        new()
        {
            Name = "Без заставки",
            Text = "/DisableSplash",
            Description = "Не показывать заставку при запуске.",
        },
        new()
        {
            Name = "Команда «Все функции»",
            Text = "/DisplayAllFunctions",
            Description = "Показать команду «Все функции» в главном меню.",
        },
        new()
        {
            Name = "Интерфейс «Такси»",
            Text = "/iTaxi",
            Description = "Запустить в интерфейсе «Такси».",
        },
        new()
        {
            Name = "Режим отладки",
            Text = "/Debug",
            Description = "Запуск в режиме отладки: к сеансу можно подключиться отладчиком.",
        },
        new()
        {
            Name = "Показывать производительность",
            Text = "/DisplayPerformance",
            Description = "Показывать число вызовов сервера и объём переданных данных.",
        },
        new()
        {
            Name = "Низкая скорость соединения",
            Text = "/O Low",
            Description = "Режим низкой скорости соединения: меньше данных по сети (тонкий клиент).",
        },
        new()
        {
            Name = "Привилегированный режим",
            Text = "/UsePrivilegedMode",
            Description = "Сеанс в привилегированном режиме — нужны административные права.",
        },
        new()
        {
            Name = "Выполнять регламентные задания",
            Text = "/AllowExecuteScheduledJobs -Force",
            Description = "Файловая база: выполнять регламентные задания в этом сеансе.",
        },
        new()
        {
            Name = "Менеджер тестирования",
            Text = "/TESTMANAGER",
            Description = "Запуск менеджером автоматизированного тестирования.",
        },
        new()
        {
            Name = "Клиент тестирования",
            Text = "/TESTCLIENT",
            Description = "Запуск клиентом автоматизированного тестирования.",
        },
        new()
        {
            Name = "Журнал действий пользователя",
            Text = "/LogUI",
            Description = "Записывать действия пользователя в интерфейсе — для сценариев тестирования.",
        },
        new()
        {
            Name = "Файл служебных сообщений (/Out)",
            Text = "/Out",
            Value = ParameterValueKind.File,
            Description = "Файл, в который выводятся служебные сообщения (например, результат пакетного режима).",
        },
    ];

    /// <summary>
    /// Свой параметр из окна «Свои шаблоны параметров»: в списке шаблонов называется описанием
    /// (или самим параметром, если описания нет) и вставляется как есть.
    /// </summary>
    public static ParameterTemplate Custom(string parameter, string? description)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var text = parameter.Trim();
        var about = description?.Trim() ?? string.Empty;
        return new ParameterTemplate { Name = about.Length > 0 ? about : text, Text = text, Description = about };
    }

    /// <summary>Описание для таблицы: у старых своих шаблонов описания нет — тогда их название.</summary>
    public static string DescriptionOf(ParameterTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!string.IsNullOrWhiteSpace(template.Description))
        {
            return template.Description;
        }

        return template.Name == template.Text ? string.Empty : template.Name;
    }

    /// <summary>Добавляет фрагмент к тексту параметров через пробел.</summary>
    public static string Append(string? text, string fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        var trimmed = text?.TrimEnd() ?? string.Empty;
        return trimmed.Length == 0 ? fragment.Trim() : trimmed + " " + fragment.Trim();
    }
}
