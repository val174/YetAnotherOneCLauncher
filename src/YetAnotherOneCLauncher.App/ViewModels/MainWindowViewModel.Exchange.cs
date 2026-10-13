using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Exchange;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Сохранение личного списка баз в файл JSON и загрузка из него (меню ≡, контекстное меню, настройки).</summary>
public sealed partial class MainWindowViewModel
{
    private const string JsonTypeName = "Список информационных баз (JSON)";
    private const string JsonExtension = "json";

    /// <summary>Личный список доступен: сохранять и загружать можно в любом режиме списка.</summary>
    public bool HasPersonalList => _store is not null;

    /// <summary>Сохранить весь личный список в файл JSON.</summary>
    [RelayCommand(CanExecute = nameof(HasPersonalList))]
    private Task ExportListJsonAsync() => SaveWholeListJsonAsync(ExportOptions.From(_settings.Settings.ListExchange));

    /// <summary>Весь личный список — в файл JSON; <paramref name="options"/> — из настроек или из открытого окна настроек.</summary>
    private async Task SaveWholeListJsonAsync(ExportOptions options)
    {
        if (_store is null)
        {
            return;
        }

        V8iDocument document;
        try
        {
            document = await _store.LoadAsync() ?? new V8iDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось прочитать список баз: {ex.Message}");
            return;
        }

        var suggested = $"ibases-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd}.{JsonExtension}";
        await SaveJsonAsync("Сохранить список в файл JSON", suggested, document.Sections, wholeList: true, options);
    }

    /// <summary>Контекстное меню: выбранная база или группа (со всем вложенным) — в файл JSON.</summary>
    [RelayCommand(CanExecute = nameof(HasPersonalSelection))]
    private async Task ExportSelectionJsonAsync()
    {
        var (name, sections) = SelectedPersonalSections();
        if (sections.Count > 0)
        {
            await SaveJsonAsync(
                "Сохранить в файл JSON", $"{name}.{JsonExtension}", sections, wholeList: false, ExportOptions.From(_settings.Settings.ListExchange));
        }
    }

    private bool HasPersonalSelection() =>
        _store is not null && (SelectedInfoBase is { InfoBase.IsReadOnly: false } || SelectedFolder is { IsEditable: true });

    private async Task SaveJsonAsync(
        string title, string suggestedName, IEnumerable<V8iSection> sections, bool wholeList, ExportOptions options)
    {
        var path = await _files.SaveFileAsync(title, suggestedName, JsonTypeName, JsonExtension);
        if (path is null)
        {
            return;
        }

        var file = InfoBaseListExchange.Export(
            sections,
            _settings.UserData,
            options,
            wholeList,
            new AboutViewModel().Version,
            DateTimeOffset.Now);
        try
        {
            await InfoBaseListExchange.SaveAsync(file, path);
            StatusText = $"Список сохранён в файл (баз: {file.InfoBases.Count}, групп: {file.Folders.Count}): {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось сохранить {path}: {ex.Message}");
        }
    }

    /// <summary>Загрузить список из файла JSON: окно с выбором «добавить» или «заменить».</summary>
    [RelayCommand(CanExecute = nameof(HasPersonalList))]
    private async Task ImportListJsonAsync()
    {
        if (_store is null)
        {
            return;
        }

        var path = await _files.OpenFileAsync("Загрузить список из файла JSON", JsonTypeName, ["*." + JsonExtension]);
        if (path is null)
        {
            return;
        }

        InfoBaseListFile file;
        V8iDocument current;
        try
        {
            file = await InfoBaseListExchange.LoadAsync(path);
            current = await _store.LoadAsync() ?? new V8iDocument();
        }
        catch (InfoBaseListFileException ex)
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось загрузить {Path.GetFileName(path)}: {ex.Message}");
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось прочитать {path}: {ex.Message}");
            return;
        }

        var otherNames = OtherListNames();
        var import = new ImportListViewModel(
            Path.GetFileName(path),
            file,
            InfoBaseListExchange.Preview(current, file, ImportMode.Add, otherNames),
            InfoBaseListExchange.Preview(current, file, ImportMode.Replace, otherNames),
            current.Sections.Count(s => !string.IsNullOrWhiteSpace(s.Name)));
        if (!await _dialogs.ConfirmImportAsync(import))
        {
            return;
        }

        var mode = import.Mode;
        var added = new List<V8iSection>();
        ImportResult? result = null;
        var withSettings = 0;
        await EditListAsync(
            document =>
            {
                // Правка может повториться, если файл списка меняли параллельно, — начинаем с чистого листа.
                added.Clear();
                result = InfoBaseListExchange.Import(document, file, mode, otherNames, added);
            },
            () => ImportMessage(mode, result, withSettings),
            CurrentSelectionKey,
            backup: mode == ImportMode.Replace,
            afterSave: () =>
            {
                if (import.ApplySettings)
                {
                    withSettings = InfoBaseListExchange.ApplySettings(
                        file, added, _settings.UserData, replaceFolderParameters: mode == ImportMode.Replace);
                    _settings.RequestSave();
                }
            });
    }

    private static string ImportMessage(ImportMode mode, ImportResult? result, int withSettings)
    {
        var text = mode == ImportMode.Replace
            ? $"Список заменён содержимым файла: записей {result?.Added ?? 0}"
            : $"Из файла добавлено записей: {result?.Added ?? 0}; пропущено (уже есть в списке): {result?.Skipped ?? 0}";
        if (result is { SameName: > 0 })
        {
            text += $"; пропущено (база с таким же названием уже есть): {result.SameName}";
        }

        if (withSettings > 0)
        {
            text += $"; настройки перенесены для баз: {withSettings}";
        }

        return text + (mode == ImportMode.Replace ? ". Прежний список — в ibases.v8i.bak." : ".");
    }

    /// <summary>Названия баз общих списков и веб-сервисов: в личном списке такие названия не допускаются.</summary>
    private List<string> OtherListNames() => [.. _bases.Where(b => b.InfoBase.IsReadOnly).Select(b => b.Name)];

    /// <summary>Выбранная база личного списка или группа со всем вложенным из личного списка.</summary>
    private (string Name, List<V8iSection> Sections) SelectedPersonalSections()
    {
        if (SelectedInfoBase is { InfoBase.IsReadOnly: false } infoBase)
        {
            return (infoBase.Name, [infoBase.InfoBase.Section]);
        }

        if (SelectedFolder is { IsEditable: true } folder && _catalog is not null)
        {
            var prefix = folder.Path + "/";
            bool Inside(string path) =>
                string.Equals(path, folder.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            var sections = new List<V8iSection>();
            if (folder.Record is { IsReadOnly: false } record)
            {
                sections.Add(record.Section);
            }

            sections.AddRange(_catalog.Folders.Where(f => !f.IsReadOnly && Inside(f.FolderPath)).Select(f => f.Section));
            sections.AddRange(_catalog.InfoBases.Where(b => !b.IsReadOnly && Inside(b.FolderPath)).Select(b => b.Section));
            return (folder.Name, sections);
        }

        return (string.Empty, []);
    }
}
