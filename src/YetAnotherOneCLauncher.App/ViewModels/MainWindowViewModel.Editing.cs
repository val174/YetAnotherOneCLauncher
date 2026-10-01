using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Cache;
using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Редактирование личного списка: базы, папки, порядок, импорт и выгрузка.</summary>
public sealed partial class MainWindowViewModel
{
    private const string ListTitle = "Список баз";

    /// <summary>Добавлять записи можно в личный список — и только в режиме «Все базы».</summary>
    public bool CanEditList => _store is not null && IsAllBasesMode;

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task AddBaseAsync()
    {
        var editor = CreateBaseEditor(new InfoBaseDraft { FolderPath = TargetFolderPath() }, isNew: true, existing: null);
        if (!await _dialogs.EditInfoBaseAsync(editor) || editor.Result is not { } draft)
        {
            return;
        }

        string? key = null;
        if (await EditListAsync(
                document => key = SelectionKeyOf(PersonalListEditor.AddBase(document, draft)),
                $"База «{draft.Name}» добавлена.",
                () => key))
        {
            await ApplyPendingLaunchSettingsAsync(editor, key);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task AddFolderAsync()
    {
        var parent = TargetFolderPath();
        var name = await _dialogs.PromptAsync("Новая папка", $"Название папки в «{parent}»:", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await EditListAsync(
            document => PersonalListEditor.AddFolder(document, parent, name),
            $"Папка «{name.Trim()}» создана.",
            () => FolderSelectionKey(FolderPaths.Combine(parent, name)));
    }

    /// <summary>F2: форма базы или переименование папки. Базу из общего списка предлагается скопировать.</summary>
    [RelayCommand(CanExecute = nameof(HasEditableSelection))]
    private async Task EditAsync()
    {
        if (SelectedInfoBase is { } target)
        {
            if (target.InfoBase.IsReadOnly)
            {
                if (await _dialogs.ConfirmAsync(
                        ListTitle,
                        $"«{target.Name}» — из общего списка, изменить её нельзя. Скопировать базу в личный список, чтобы настроить копию?",
                        "Скопировать"))
                {
                    await CopyToPersonalAsync();
                }

                return;
            }

            var editor = CreateBaseEditor(InfoBaseDraft.From(target.InfoBase), isNew: false, existing: target);
            if (!await _dialogs.EditInfoBaseAsync(editor) || editor.Result is not { } draft)
            {
                return;
            }

            var key = target.InfoBase.Id is not null
                ? target.InfoBase.IdentityKey
                : "conn:" + draft.BuildConnection().ToNormalizedKey();
            if (await EditListAsync(
                    document => PersonalListEditor.UpdateBase(document, EntryRef.Of(target.InfoBase), draft),
                    $"База «{draft.Name}» сохранена.",
                    () => key))
            {
                await ApplyPendingLaunchSettingsAsync(editor, key);
            }

            return;
        }

        if (SelectedFolder is { IsEditable: true } folder)
        {
            var name = await _dialogs.PromptAsync("Переименование папки", "Новое название папки:", folder.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == folder.Name)
            {
                return;
            }

            var parent = ParentPath(folder.Path);
            var newPath = FolderPaths.Combine(parent, name);
            if (await EditListAsync(
                    document => PersonalListEditor.RenameFolder(document, FolderRef(folder), name),
                    $"Папка переименована в «{name.Trim()}».",
                    () => FolderSelectionKey(newPath)))
            {
                MoveFolderParameters(folder.Path, newPath);
            }
        }
    }

    /// <summary>Правка записи как текста: все ключи, включая неизвестные лаунчеру.</summary>
    [RelayCommand(CanExecute = nameof(HasEditableSelection))]
    private async Task EditAsTextAsync()
    {
        var (target, section) = SelectedEntry();
        if (target is null || section is null)
        {
            await _dialogs.ShowMessageAsync(ListTitle, "У этой записи нет своей строки в личном списке — изменить её как текст нельзя.");
            return;
        }

        var text = await _dialogs.EditTextAsync(
            "Запись списка баз",
            "Строка [Название] и строки Ключ=Значение. Ключи, которые лаунчер не знает, сохраняются как есть. Сохранить — Ctrl+Enter.",
            V8iSections.ToText(section).Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        if (text is null)
        {
            return;
        }

        await EditListAsync(
            document => PersonalListEditor.ReplaceText(document, target, text),
            "Запись сохранена.",
            CurrentSelectionKey);
    }

    [RelayCommand(CanExecute = nameof(CanReorderOrDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedInfoBase is { } infoBase)
        {
            if (infoBase.InfoBase.IsReadOnly)
            {
                await _dialogs.ShowMessageAsync(ListTitle, $"«{infoBase.Name}» — из общего списка, удалить её отсюда нельзя.");
                return;
            }

            // Кэш — до удаления из списка: после него база пропадёт из каталога.
            await CacheScanTask;
            var cache = _cacheReport.For(infoBase.InfoBase)?.In(local: true, roaming: _settings.Settings.Cache.IncludeRoaming).ToList() ?? [];
            var cacheSize = cache.Count > 0 ? $" ({ByteSize.Format(cache.Sum(d => d.SizeBytes))})" : string.Empty;
            var (accepted, deleteCache) = await _dialogs.ConfirmWithOptionAsync(
                ListTitle,
                $"Удалить «{infoBase.Name}» из списка баз?\nСама база (её данные) не удаляется.",
                "Удалить",
                $"Удалить временные файлы информационной базы{cacheSize}",
                optionChecked: true);
            if (!accepted)
            {
                return;
            }

            var removed = await EditListAsync(
                document => PersonalListEditor.DeleteBase(document, EntryRef.Of(infoBase.InfoBase)),
                $"«{infoBase.Name}» удалена из списка.",
                () => null);

            // Временные файлы — тем же способом, что «Очистить кэш» (корзина или насовсем, открытая база пропускается),
            // но без второго вопроса: согласие дано флажком.
            if (removed && deleteCache && cache.Count > 0 && CanManageCache)
            {
                var listStatus = StatusText;
                if (await CleanCacheAsync(cache, $"временные файлы «{infoBase.Name}»", confirm: false) is not null)
                {
                    StatusText = listStatus + " " + StatusText;
                }
            }

            return;
        }

        if (SelectedFolder is { IsEditable: true } folder)
        {
            var count = _catalog?.PersonalList?.Document is { } personal
                ? PersonalListEditor.CountDescendants(personal, folder.Path)
                : 0;
            var question = count == 0
                ? $"Удалить папку «{folder.Name}»?"
                : $"Удалить папку «{folder.Name}» вместе со всем содержимым ({count} {Plural(count, "запись", "записи", "записей")})?\nСами базы и их файлы не удаляются — только записи в списке.";
            if (await _dialogs.ConfirmAsync(ListTitle, question, "Удалить"))
            {
                await EditListAsync(
                    document => PersonalListEditor.DeleteFolder(document, FolderRef(folder)),
                    $"Папка «{folder.Name}» удалена.",
                    () => null);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanReorderOrDelete))]
    private Task MoveUpAsync() => MoveByAsync(-1);

    [RelayCommand(CanExecute = nameof(CanReorderOrDelete))]
    private Task MoveDownAsync() => MoveByAsync(+1);

    /// <summary>
    /// Упорядочить по наименованию содержимое выбранной папки (у выделенной базы — папки, где она лежит):
    /// порядок записывается в список баз, так что виден и при своём порядке, и в штатном стартере.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSortFolderByName))]
    private async Task SortFolderByNameAsync()
    {
        if (SortTargetFolder() is not { } folder)
        {
            return;
        }

        var key = CurrentSelectionKey();
        var count = 0;
        var title = folder == FolderPaths.Root ? "корне списка" : $"папке «{folder}»";
        await EditListAsync(
            document => count = PersonalListEditor.SortFolderByName(document, folder),
            () => count > 0
                ? $"Записи в {title} упорядочены по наименованию."
                : $"В {title} нет записей личного списка — упорядочивать нечего.",
            () => key);
    }

    private string? SortTargetFolder() =>
        SelectedFolder is { Kind: FolderKind.Regular } folder ? folder.Path : SelectedInfoBase?.InfoBase.FolderPath;

    private bool CanSortFolderByName() => _store is not null && IsAllBasesMode && SortTargetFolder() is not null;

    /// <summary>Копия базы из общего списка в личном — чтобы изменить её настройки.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyToPersonal))]
    private async Task CopyToPersonalAsync()
    {
        if (SelectedInfoBase is not { } source)
        {
            return;
        }

        string? key = null;
        await EditListAsync(
            document => key = SelectionKeyOf(PersonalListEditor.CopyBase(document, source.InfoBase)),
            $"Копия «{source.Name}» добавлена в личный список.",
            () => key);
    }

    [RelayCommand(CanExecute = nameof(CanEditList))]
    private async Task ImportAsync()
    {
        var path = await _files.OpenListFileAsync("Загрузить базы из файла");
        if (path is null)
        {
            return;
        }

        V8iDocument source;
        try
        {
            source = await V8iDocument.LoadAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось прочитать {path}: {ex.Message}");
            return;
        }

        ImportResult? result = null;
        await EditListAsync(
            document => result = PersonalListEditor.Import(document, source),
            () => $"Из файла добавлено записей: {result?.Added ?? 0}; пропущено (уже есть в списке): {result?.Skipped ?? 0}.",
            CurrentSelectionKey);
    }

    /// <summary>Выгрузить выделенную базу или папку со всем содержимым в файл .v8i.</summary>
    /// <summary>Файл — один из общих списков (не личный) текущего каталога.</summary>
    internal bool IsSharedListFile(string path)
    {
        if (_catalog is null)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string? Full(string p)
        {
            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        var target = Full(path);
        return target is not null && _catalog.Lists.Any(l =>
            l.Source.Kind == ListSourceKind.Common && string.Equals(Full(l.Source.Location), target, comparison));
    }

    [RelayCommand(CanExecute = nameof(HasAnySelection))]
    private async Task ExportAsync()
    {
        var (name, sections) = SelectedSectionsForExport();
        if (sections.Count == 0)
        {
            return;
        }

        var path = await _files.SaveListFileAsync("Сохранить ссылку в файл", name + ".v8i");
        if (path is null)
        {
            return;
        }

        // Общие списки лаунчер не меняет — в том числе выгрузкой поверх такого файла.
        if (IsSharedListFile(path))
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"{path} — общий список баз из 1cestart.cfg. Лаунчер общие списки не изменяет: выберите другой файл.");
            return;
        }

        try
        {
            await PersonalListEditor.Export(sections).SaveAsync(path);
            StatusText = $"Ссылка сохранена в файл (записей: {sections.Count}): {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось сохранить {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Перетаскивание в дереве: на папку — в конец папки, на базу — перед ней, на «Избранное» — в избранное.
    /// </summary>
    public async Task MoveNodeAsync(TreeNodeViewModel source, TreeNodeViewModel target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(source, target))
        {
            return;
        }

        if (target is FolderNodeViewModel { Kind: FolderKind.Favorites } && source is BaseNodeViewModel favoriteSource)
        {
            if (!favoriteSource.Base.IsFavorite)
            {
                ToggleFavorite(favoriteSource.Base);
            }

            return;
        }

        EntryRef? moving = source switch
        {
            BaseNodeViewModel { Base.InfoBase.IsReadOnly: false } b => EntryRef.Of(b.Base.InfoBase),
            FolderNodeViewModel { IsEditable: true } f => FolderRef(f),
            _ => null,
        };
        if (moving is null)
        {
            await _dialogs.ShowMessageAsync(ListTitle, "Перемещать можно только записи личного списка.");
            return;
        }

        (string Folder, EntryRef? Before)? destination = target switch
        {
            FolderNodeViewModel { Kind: FolderKind.Regular } folder => (folder.Path, null),
            BaseNodeViewModel b when IsInRegularFolder(b) => (b.Base.InfoBase.FolderPath, b.Base.InfoBase.IsReadOnly ? null : EntryRef.Of(b.Base.InfoBase)),
            _ => null,
        };
        if (destination is not { } place)
        {
            return;
        }

        var key = source is FolderNodeViewModel movedFolder
            ? FolderSelectionKey(FolderPaths.Combine(place.Folder, movedFolder.Name))
            : ((BaseNodeViewModel)source).Base.InfoBase.IdentityKey;
        var byName = IsSortedByName;
        var moved = await EditListAsync(
            document => PersonalListEditor.Move(document, moving, place.Folder, place.Before, sortByNameFirst: byName),
            $"«{source.Name}» перемещена в «{place.Folder}».",
            () => key);
        KeepCustomOrderAfterMove(moved && byName);
        if (moved && source is FolderNodeViewModel folderSource)
        {
            MoveFolderParameters(folderSource.Path, FolderPaths.Combine(place.Folder, folderSource.Name));
        }
    }

    private bool HasEditableSelection() =>
        _store is not null && (SelectedInfoBase is not null || SelectedFolder is { IsEditable: true });

    /// <summary>Удалять и переставлять — только в режиме «Все базы»: в недавних порядок — по времени запуска, в избранном — по имени.</summary>
    private bool CanReorderOrDelete() => HasEditableSelection() && IsAllBasesMode;

    private bool HasAnySelection() => SelectedInfoBase is not null || SelectedFolder is not null;

    private bool CanCopyToPersonal() => _store is not null && SelectedInfoBase is { InfoBase.IsReadOnly: true };

    private async Task MoveByAsync(int delta)
    {
        var (target, _) = SelectedEntry();
        if (target is null)
        {
            return;
        }

        var key = CurrentSelectionKey();
        var moved = false;
        var byName = IsSortedByName;
        var saved = await EditListAsync(
            document => moved = PersonalListEditor.MoveBy(document, target, delta, sortByNameFirst: byName),
            () => moved ? "Порядок изменён." : "Дальше двигать некуда.",
            () => key);
        KeepCustomOrderAfterMove(saved && moved && byName);
    }

    /// <summary>
    /// Перестановка при сортировке по наименованию: алфавитный порядок уже записан в список баз вместе с ней,
    /// дальше дерево показывает свой порядок — тот же, но с перестановкой.
    /// </summary>
    private void KeepCustomOrderAfterMove(bool movedWhileSortedByName)
    {
        if (movedWhileSortedByName)
        {
            IsSortedByName = false;
            StatusText += " Включён свой порядок — он сохранён в списке баз.";
        }
    }

    /// <summary>Выделенная запись личного списка и её секция в загруженном документе.</summary>
    private (EntryRef? Target, V8iSection? Section) SelectedEntry()
    {
        if (SelectedInfoBase is { InfoBase.IsReadOnly: false } infoBase)
        {
            return (EntryRef.Of(infoBase.InfoBase), infoBase.InfoBase.Section);
        }

        if (SelectedFolder is { IsEditable: true } folder)
        {
            return (FolderRef(folder), folder.Record?.Section);
        }

        return (null, null);
    }

    private (string Name, List<V8iSection> Sections) SelectedSectionsForExport()
    {
        if (SelectedInfoBase is { } infoBase)
        {
            return (infoBase.Name, [infoBase.InfoBase.Section]);
        }

        if (SelectedFolder is { } folder && _catalog is not null)
        {
            var prefix = folder.Path + "/";
            bool Inside(string path) =>
                string.Equals(path, folder.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            var sections = new List<V8iSection>();
            if (folder.Record is { } record)
            {
                sections.Add(record.Section);
            }

            sections.AddRange(_catalog.Folders.Where(f => Inside(f.FolderPath)).Select(f => f.Section));
            sections.AddRange(_catalog.InfoBases.Where(b => Inside(b.FolderPath)).Select(b => b.Section));
            return (folder.Name, sections);
        }

        return (string.Empty, []);
    }

    private Task<bool> EditListAsync(Action<V8iDocument> edit, string successMessage, Func<string?> selectionKey) =>
        EditListAsync(edit, () => successMessage, selectionKey);

    /// <summary>
    /// Применяет правку к личному списку и показывает результат. Ошибки — сообщением;
    /// если запись изменили или удалили в другой программе, список перечитывается.
    /// </summary>
    private async Task<bool> EditListAsync(Action<V8iDocument> edit, Func<string> successMessage, Func<string?> selectionKey)
    {
        if (_store is null || _catalog is null)
        {
            return false;
        }

        try
        {
            var document = await _store.EditAsync(edit);
            ShowCatalog(_catalog.WithPersonalDocument(document), selectionKey());
            StatusText = successMessage();
            return true;
        }
        catch (ListEditException ex)
        {
            await _dialogs.ShowMessageAsync(ListTitle, ex.Message);
            if (ex.RequiresReload)
            {
                await ReloadPersonalListAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogEditFailed(_logger, _store.FilePath, ex);
            await _dialogs.ShowMessageAsync(ListTitle, $"Не удалось сохранить список баз: {ex.Message}");
        }

        return false;
    }

    /// <summary>Перечитать только личный список (после чужой правки); общие списки не трогаем.</summary>
    private async Task ReloadPersonalListAsync()
    {
        if (_store is null || _catalog is null)
        {
            return;
        }

        try
        {
            var document = await _store.LoadAsync() ?? new V8iDocument();
            ShowCatalog(_catalog.WithPersonalDocument(document), CurrentSelectionKey());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogEditFailed(_logger, _store.FilePath, ex);
            StatusText = "Не удалось перечитать список баз: " + ex.Message;
        }
    }

    private void OnListFileChanged(object? sender, ListFileKind kind)
    {
        // Событие приходит из потока наблюдателя — обрабатываем в потоке интерфейса.
        if (_uiContext is null)
        {
            _ = HandleListFileChangedAsync(kind);
        }
        else
        {
            _uiContext.Post(_ => _ = HandleListFileChangedAsync(kind), null);
        }
    }

    internal async Task HandleListFileChangedAsync(ListFileKind kind)
    {
        if (IsLoading)
        {
            return;
        }

        if (kind == ListFileKind.StarterConfig)
        {
            await ReloadAsync();
            StatusText = "Настройки стартера 1С изменились — списки перечитаны.";
            return;
        }

        // Свою запись не считаем чужим изменением.
        if (_store is null || await _store.CurrentFingerprintAsync() == _store.LastWrittenFingerprint)
        {
            return;
        }

        await ReloadPersonalListAsync();
        StatusText = "Список баз изменён другой программой — обновлён.";
    }

    /// <summary>Куда добавлять: в выделенную папку или в папку выделенной базы.</summary>
    private string TargetFolderPath() =>
        SelectedFolder is { IsEditable: true } folder ? folder.Path
        : SelectedFolder is { } anyFolder ? anyFolder.Path
        : SelectedInfoBase?.InfoBase.FolderPath ?? FolderPaths.Root;

    private List<string> AllFolderPaths()
    {
        if (_catalog is null)
        {
            return [FolderPaths.Root];
        }

        return _catalog.Folders.Select(f => f.FullPath)
            .Concat(_catalog.InfoBases.Select(b => b.FolderPath))
            .Prepend(FolderPaths.Root)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsInRegularFolder(BaseNodeViewModel node) =>
        TreeItems.OfType<FolderNodeViewModel>().All(f => f.Kind == FolderKind.Regular || !f.Children.Contains(node));

    private static FolderEntryRef FolderRef(FolderNodeViewModel folder) =>
        folder.Record is { } record ? EntryRef.Of(record) : EntryRef.OfImpliedFolder(folder.Path);

    private static string SelectionKeyOf(V8iSection section) =>
        new InfoBase(section, new ListSource(ListSourceKind.Personal, string.Empty)).IdentityKey;

    private static string ParentPath(string path)
    {
        var segments = FolderPaths.Split(path);
        return segments.Length <= 1 ? FolderPaths.Root : "/" + string.Join('/', segments[..^1]);
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        return mod100 is >= 11 and <= 14 ? many : mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Не удалось записать или прочитать список баз {Path}")]
    private static partial void LogEditFailed(ILogger logger, string path, Exception exception);
}
