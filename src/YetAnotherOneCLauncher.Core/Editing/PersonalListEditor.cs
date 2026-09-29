using System.Globalization;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Editing;

/// <summary>Итог импорта.</summary>
public sealed record ImportResult(int Added, int Skipped);

/// <summary>
/// Правки документа личного списка. Меняются только затронутые строки; остальное остаётся байт в байт.
/// Новые записи пишутся с тем же набором и порядком ключей, что у штатного стартера.
/// </summary>
/// <remarks>
/// Все методы работают с заново прочитанным документом и находят записи по <see cref="EntryRef"/>,
/// поэтому правка корректно ложится поверх изменений, сделанных другой программой.
/// </remarks>
public static class PersonalListEditor
{
    /// <summary>Шаг порядковых номеров, как у штатного стартера.</summary>
    public const long OrderStep = 16384;

    private const string CopySuffix = " (копия)";

    public static V8iSection AddBase(V8iDocument document, InfoBaseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureValid(draft);

        var folder = FolderPaths.Normalize(draft.FolderPath);
        var section = new V8iSection(draft.Name.Trim());
        section.Set(V8iKeys.Connect, draft.BuildConnection().ToString());
        section.Set(V8iKeys.Id, NewId());
        section.Set(V8iKeys.OrderInList, Format(NextOrderInList(document)));
        section.Set(V8iKeys.Folder, folder);
        section.Set(V8iKeys.OrderInTree, Format(NextOrderInTree(document, folder)));
        section.Set(V8iKeys.External, "0");
        section.Set(V8iKeys.ClientConnectionSpeed, "Normal");
        section.Set(V8iKeys.App, AppValue(draft.App));
        SetOptional(section, V8iKeys.WindowsAuthentication, WaValue(draft.WindowsAuthentication));
        SetOptional(section, V8iKeys.Version, draft.Version);
        SetOptional(section, V8iKeys.AdditionalParameters, draft.AdditionalParameters);

        document.Sections.Add(section);
        return section;
    }

    public static void UpdateBase(V8iDocument document, BaseEntryRef target, InfoBaseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureValid(draft);
        var section = FindBase(document, target);

        var name = draft.Name.Trim();
        if (name != section.Name)
        {
            section.Name = name;
        }

        // Строку подключения переписываем, только если она действительно изменилась: сохраняем исходное написание.
        var current = ConnectionString.Parse(section.Get(V8iKeys.Connect));
        var updated = draft.BuildConnection();
        if (current.ToString() != updated.ToString())
        {
            section.Set(V8iKeys.Connect, updated.ToString());
        }

        var folder = FolderPaths.Normalize(draft.FolderPath);
        if (FolderPaths.Normalize(section.Get(V8iKeys.Folder)) != folder)
        {
            section.Set(V8iKeys.Folder, folder);
            section.Set(V8iKeys.OrderInTree, Format(NextOrderInTree(document, folder, except: section)));
        }

        if (ParseApp(section.Get(V8iKeys.App)) != draft.App)
        {
            section.Set(V8iKeys.App, AppValue(draft.App));
        }

        SetOptional(section, V8iKeys.WindowsAuthentication, WaValue(draft.WindowsAuthentication));
        SetOptional(section, V8iKeys.Version, draft.Version);
        SetOptional(section, V8iKeys.AdditionalParameters, draft.AdditionalParameters);
    }

    public static void DeleteBase(V8iDocument document, BaseEntryRef target)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Sections.Remove(FindBase(document, target));
    }

    public static V8iSection AddFolder(V8iDocument document, string parentPath, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        var folderName = ValidateFolderName(name);
        var parent = FolderPaths.Normalize(parentPath);
        var fullPath = FolderPaths.Combine(parent, folderName);
        if (FindFolder(document, fullPath) is not null)
        {
            throw new ListEditException(ListEditErrorKind.Invalid, $"Папка «{fullPath}» уже есть.");
        }

        var section = new V8iSection(folderName);
        section.Set(V8iKeys.Id, NewId());
        section.Set(V8iKeys.OrderInList, Format(NextOrderInList(document)));
        section.Set(V8iKeys.Folder, parent);
        section.Set(V8iKeys.OrderInTree, Format(NextOrderInTree(document, parent)));
        section.Set(V8iKeys.External, "0");
        document.Sections.Add(section);
        return section;
    }

    /// <summary>Переименовывает папку и исправляет пути у всего вложенного.</summary>
    public static void RenameFolder(V8iDocument document, FolderEntryRef target, string newName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var folderName = ValidateFolderName(newName);
        var section = FindFolderChecked(document, target);
        var parent = ParentOf(target.FullPath);
        var newPath = FolderPaths.Combine(parent, folderName);
        if (string.Equals(newPath, target.FullPath, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(newPath, target.FullPath, StringComparison.OrdinalIgnoreCase) && FindFolder(document, newPath) is not null)
        {
            throw new ListEditException(ListEditErrorKind.Invalid, $"Папка «{newPath}» уже есть.");
        }

        if (section is not null)
        {
            section.Name = folderName;
        }

        ReplaceFolderPrefix(document, target.FullPath, newPath);
    }

    /// <summary>Сколько записей (баз и папок) внутри папки, на любой глубине.</summary>
    public static int CountDescendants(V8iDocument document, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = FolderPaths.Normalize(folderPath);
        return document.Sections.Count(s => IsInside(FolderPaths.Normalize(s.Get(V8iKeys.Folder)), path));
    }

    /// <summary>Удаляет папку вместе со всем содержимым.</summary>
    public static void DeleteFolder(V8iDocument document, FolderEntryRef target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var section = FindFolderChecked(document, target);
        document.Sections.RemoveAll(s =>
            ReferenceEquals(s, section) || IsInside(FolderPaths.Normalize(s.Get(V8iKeys.Folder)), target.FullPath));
    }

    /// <summary>
    /// Перемещает запись в папку <paramref name="targetFolder"/> перед <paramref name="before"/> (или в конец)
    /// и перенумеровывает порядок записей личного списка в этой папке.
    /// </summary>
    /// <param name="sortByNameFirst">
    /// Сначала записать порядок по наименованию (<see cref="SortByName"/>): перестановку сделали в дереве,
    /// показанном по алфавиту, — «перед» относится к нему, а не к прежнему своему порядку.
    /// </param>
    public static void Move(V8iDocument document, EntryRef target, string targetFolder, EntryRef? before = null, bool sortByNameFirst = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var destination = FolderPaths.Normalize(targetFolder);

        V8iSection? section;
        if (target is FolderEntryRef folderRef)
        {
            if (IsInside(destination, folderRef.FullPath) || destination == folderRef.FullPath)
            {
                throw new ListEditException(ListEditErrorKind.Invalid, "Нельзя переместить папку в саму себя или во вложенную папку.");
            }

            section = FindFolderChecked(document, folderRef);
            var newPath = FolderPaths.Combine(destination, folderRef.Name);
            if (newPath != folderRef.FullPath)
            {
                if (FindFolder(document, newPath) is not null)
                {
                    throw new ListEditException(ListEditErrorKind.Invalid, $"Папка «{newPath}» уже есть.");
                }

                ReplaceFolderPrefix(document, folderRef.FullPath, newPath);
            }

            if (sortByNameFirst)
            {
                SortByName(document);
            }

            if (section is null)
            {
                return; // папки нет записью — порядок задать не у чего
            }
        }
        else
        {
            section = FindBase(document, (BaseEntryRef)target);
            if (sortByNameFirst)
            {
                SortByName(document);
            }
        }

        section.Set(V8iKeys.Folder, destination);

        var siblings = SiblingsInOrder(document, destination).Where(s => !ReferenceEquals(s, section)).ToList();
        var index = before is null ? -1 : siblings.FindIndex(s => Matches(s, before));
        siblings.Insert(index < 0 ? siblings.Count : index, section);
        Renumber(siblings);
    }

    /// <summary>Сдвигает запись на <paramref name="delta"/> позиций среди записей личного списка в той же папке.</summary>
    /// <param name="sortByNameFirst">Сначала записать порядок по наименованию — см. <see cref="Move"/>.</param>
    /// <returns><c>false</c>, если сдвигать некуда.</returns>
    public static bool MoveBy(V8iDocument document, EntryRef target, int delta, bool sortByNameFirst = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var section = target is FolderEntryRef f
            ? FindFolderChecked(document, f) ?? throw new ListEditException(ListEditErrorKind.Invalid, "У этой папки нет своей записи в списке — её место определяется базами внутри.")
            : FindBase(document, (BaseEntryRef)target);
        if (sortByNameFirst)
        {
            SortByName(document);
        }

        var siblings = SiblingsInOrder(document, FolderPaths.Normalize(section.Get(V8iKeys.Folder)));
        var index = siblings.IndexOf(section);
        var newIndex = Math.Clamp(index + delta, 0, siblings.Count - 1);
        if (newIndex == index)
        {
            return false;
        }

        siblings.RemoveAt(index);
        siblings.Insert(newIndex, section);
        Renumber(siblings);
        return true;
    }

    /// <summary>Заменяет запись текстом, отредактированным вручную: заголовок <c>[Имя]</c> и строки ключей.</summary>
    public static void ReplaceText(V8iDocument document, EntryRef target, string sectionText)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var parsed = V8iDocument.Parse(sectionText ?? string.Empty);
        if (parsed.Sections.Count != 1 || parsed.Preamble.Any(l => l.ToString().Trim().Length > 0))
        {
            throw new ListEditException(ListEditErrorKind.Invalid, "Текст должен содержать ровно одну запись: строку [Название] и строки Ключ=Значение.");
        }

        var replacement = parsed.Sections[0];
        if (string.IsNullOrWhiteSpace(replacement.Name))
        {
            throw new ListEditException(ListEditErrorKind.Invalid, "Укажите название в квадратных скобках.");
        }

        var section = target is FolderEntryRef folder
            ? FindFolderChecked(document, folder) ?? throw new ListEditException(ListEditErrorKind.Invalid, "У этой папки нет своей записи в списке.")
            : FindBase(document, (BaseEntryRef)target);

        document.Sections[document.Sections.IndexOf(section)] = replacement;
    }

    /// <summary>Копирует базу (например, из общего списка) в личный список под новым ID.</summary>
    public static V8iSection CopyBase(V8iDocument document, InfoBase source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        var copy = Clone(source.Section);
        copy.Name = source.Name + CopySuffix;
        copy.Set(V8iKeys.Id, NewId());
        copy.Set(V8iKeys.OrderInList, Format(NextOrderInList(document)));
        copy.Set(V8iKeys.OrderInTree, Format(NextOrderInTree(document, source.FolderPath)));
        document.Sections.Add(copy);
        return copy;
    }

    /// <summary>
    /// Добавляет записи другого списка. Базы, которые уже есть (по ID или подключению), и существующие папки пропускаются.
    /// </summary>
    public static ImportResult Import(V8iDocument document, V8iDocument source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        var personal = new ListSource(ListSourceKind.Personal, string.Empty);
        var existingKeys = document.Sections
            .Where(s => !V8iSections.IsFolder(s))
            .SelectMany(s => IdentityKeys(new InfoBase(s, personal)))
            .ToHashSet(StringComparer.Ordinal);

        int added = 0, skipped = 0;
        foreach (var section in source.Sections.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
        {
            if (V8iSections.IsFolder(section))
            {
                var path = new InfoBaseFolder(section, personal).FullPath;
                if (FindFolder(document, path) is not null)
                {
                    skipped++;
                    continue;
                }
            }
            else
            {
                var keys = IdentityKeys(new InfoBase(section, personal)).ToList();
                if (keys.Any(existingKeys.Contains))
                {
                    skipped++;
                    continue;
                }

                existingKeys.UnionWith(keys);
            }

            document.Sections.Add(Clone(section));
            added++;
        }

        return new ImportResult(added, skipped);
    }

    /// <summary>Новый документ с копиями записей — для выгрузки в файл .v8i.</summary>
    public static V8iDocument Export(IEnumerable<V8iSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var document = new V8iDocument();
        document.Sections.AddRange(sections.Select(Clone));
        return document;
    }

    /// <summary>Находит базу; проверяет, что её не меняли после загрузки.</summary>
    public static V8iSection FindBase(V8iDocument document, BaseEntryRef target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var personal = new ListSource(ListSourceKind.Personal, string.Empty);
        var candidates = document.Sections
            .Where(s => !V8iSections.IsFolder(s))
            .Where(s =>
            {
                var infoBase = new InfoBase(s, personal);
                return target.Id is not null
                    ? string.Equals(infoBase.Id?.Trim(), target.Id, StringComparison.OrdinalIgnoreCase)
                    : infoBase.Id is null && infoBase.ConnectionKey == target.ConnectionKey && infoBase.Name == target.Name;
            })
            .ToList();

        if (candidates.Count == 0)
        {
            throw new ListEditException(
                ListEditErrorKind.NotFound,
                $"База «{target.Name}» не найдена в личном списке — возможно, её удалили в другой программе.");
        }

        return CheckUnchanged(candidates, target);
    }

    private static V8iSection? FindFolder(V8iDocument document, string fullPath)
    {
        var personal = new ListSource(ListSourceKind.Personal, string.Empty);
        var path = FolderPaths.Normalize(fullPath);
        return document.Sections.FirstOrDefault(s =>
            V8iSections.IsFolder(s)
            && string.Equals(new InfoBaseFolder(s, personal).FullPath, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Запись папки; <c>null</c> — папка есть только в путях баз.</summary>
    private static V8iSection? FindFolderChecked(V8iDocument document, FolderEntryRef target)
    {
        var section = FindFolder(document, target.FullPath);
        if (section is null)
        {
            if (target.ExpectedText is not null)
            {
                throw new ListEditException(
                    ListEditErrorKind.NotFound,
                    $"Папка «{target.FullPath}» не найдена в личном списке — возможно, её удалили в другой программе.");
            }

            if (CountDescendants(document, target.FullPath) == 0)
            {
                throw new ListEditException(ListEditErrorKind.NotFound, $"Папка «{target.FullPath}» не найдена в личном списке.");
            }

            return null;
        }

        return target.ExpectedText is null ? section : CheckUnchanged([section], target);
    }

    private static V8iSection CheckUnchanged(List<V8iSection> candidates, EntryRef target)
    {
        if (target.ExpectedText is null)
        {
            return candidates[0];
        }

        return candidates.Find(s => V8iSections.ToText(s) == target.ExpectedText)
               ?? throw new ListEditException(
                   ListEditErrorKind.Conflict,
                   $"Запись «{target.Name}» изменена другой программой после загрузки списка. Список будет обновлён — повторите действие.");
    }

    private static bool Matches(V8iSection section, EntryRef target)
    {
        var personal = new ListSource(ListSourceKind.Personal, string.Empty);
        return target switch
        {
            FolderEntryRef folder => V8iSections.IsFolder(section)
                                     && string.Equals(new InfoBaseFolder(section, personal).FullPath, folder.FullPath, StringComparison.OrdinalIgnoreCase),
            BaseEntryRef baseRef => !V8iSections.IsFolder(section) && new InfoBase(section, personal) is var infoBase
                                    && (baseRef.Id is not null
                                        ? string.Equals(infoBase.Id?.Trim(), baseRef.Id, StringComparison.OrdinalIgnoreCase)
                                        : infoBase.ConnectionKey == baseRef.ConnectionKey && infoBase.Name == baseRef.Name),
            _ => false,
        };
    }

    /// <summary>Записи личного списка в папке — в порядке показа (как в <see cref="Catalog.CatalogTreeBuilder"/>).</summary>
    private static List<V8iSection> SiblingsInOrder(V8iDocument document, string folder)
    {
        var personal = new ListSource(ListSourceKind.Personal, string.Empty);
        return document.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Name) && FolderPaths.Normalize(s.Get(V8iKeys.Folder)) == folder)
            .OrderBy(s => new InfoBaseFolder(s, personal).OrderInTree ?? double.MaxValue)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Записывает в <c>OrderInTree</c> порядок по наименованию — во всех папках сразу: сначала папки, затем базы,
    /// по алфавиту (как <see cref="Catalog.CatalogTreeBuilder.CompareByName"/>). После этого свой порядок совпадает
    /// с тем, что было видно при сортировке по наименованию, и дальше его можно менять перестановками.
    /// </summary>
    public static void SortByName(V8iDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var personal = new ListSource(ListSourceKind.Personal, string.Empty);
        var groups = document.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => FolderPaths.Normalize(s.Get(V8iKeys.Folder)), StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            Renumber(group
                .OrderBy(s => V8iSections.IsFolder(s) ? 0 : 1)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => new InfoBaseFolder(s, personal).OrderInTree ?? double.MaxValue)
                .ToList());
        }
    }

    private static void Renumber(List<V8iSection> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Set(V8iKeys.OrderInTree, Format((i + 1) * OrderStep));
        }
    }

    private static void ReplaceFolderPrefix(V8iDocument document, string oldPath, string newPath)
    {
        foreach (var section in document.Sections)
        {
            var folder = FolderPaths.Normalize(section.Get(V8iKeys.Folder));
            if (IsInside(folder, oldPath))
            {
                section.Set(V8iKeys.Folder, newPath + folder[oldPath.Length..]);
            }
        }
    }

    /// <summary>Путь <paramref name="path"/> — это сама папка <paramref name="folder"/> или что-то внутри неё.</summary>
    private static bool IsInside(string path, string folder) =>
        folder == FolderPaths.Root
        || path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    private static string ParentOf(string fullPath)
    {
        var segments = FolderPaths.Split(fullPath);
        return segments.Length <= 1 ? FolderPaths.Root : "/" + string.Join('/', segments[..^1]);
    }

    private static string ValidateFolderName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ListEditException(ListEditErrorKind.Invalid, "Укажите название папки.");
        }

        if (trimmed.Contains('/', StringComparison.Ordinal) || trimmed.Any(c => c is '\r' or '\n'))
        {
            throw new ListEditException(ListEditErrorKind.Invalid, "Название папки не может содержать «/» и перевод строки.");
        }

        return trimmed;
    }

    private static IEnumerable<string> IdentityKeys(InfoBase infoBase)
    {
        if (infoBase.Id is not null)
        {
            yield return infoBase.IdentityKey;
        }

        yield return infoBase.ConnectionKey;
    }

    private static V8iSection Clone(V8iSection source)
    {
        var copy = new V8iSection(source.Name);
        copy.Lines.AddRange(source.Lines.Select(l => IniLine.Parse(l.ToString())));
        return copy;
    }

    private static long NextOrderInList(V8iDocument document) =>
        NextAfter(document.Sections.Select(s => s.Get(V8iKeys.OrderInList)));

    private static long NextOrderInTree(V8iDocument document, string folder, V8iSection? except = null) =>
        NextAfter(document.Sections
            .Where(s => !ReferenceEquals(s, except) && FolderPaths.Normalize(s.Get(V8iKeys.Folder)) == FolderPaths.Normalize(folder))
            .Select(s => s.Get(V8iKeys.OrderInTree)));

    /// <summary>Следующий порядок после всех существующих — целый, даже если у соседей дробный.</summary>
    private static long NextAfter(IEnumerable<string?> values) =>
        (long)Math.Floor(Math.Max(0, values.Select(CatalogEntry.ParseOrder).Max() ?? 0)) + OrderStep;

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string NewId() => Guid.NewGuid().ToString("D");

    private static void EnsureValid(InfoBaseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = draft.Validate();
        if (errors.Count > 0)
        {
            throw new ListEditException(ListEditErrorKind.Invalid, string.Join(Environment.NewLine, errors));
        }
    }

    /// <summary>Пустое значение удаляет ключ, непустое — записывает.</summary>
    private static void SetOptional(V8iSection section, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            section.Remove(key);
        }
        else
        {
            section.Set(key, value.Trim());
        }
    }

    private static string AppValue(ClientApp app) => app switch
    {
        ClientApp.ThinClient => "ThinClient",
        ClientApp.ThickClient => "ThickClient",
        ClientApp.WebClient => "WebClient",
        _ => "Auto",
    };

    private static ClientApp ParseApp(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "thinclient" => ClientApp.ThinClient,
        "thickclient" => ClientApp.ThickClient,
        "webclient" => ClientApp.WebClient,
        _ => ClientApp.Auto,
    };

    private static string? WaValue(bool? value) => value switch
    {
        true => "1",
        false => "0",
        null => null,
    };
}
