using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Метки баз (задача #25): строка отбора под панелью инструментов, подменю «Метки», метки в свойствах базы,
/// окно «Метки». Метки хранятся в настройках лаунчера, отбор по ним не запоминается.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const string TagsTitle = "Метки";

    /// <summary>Выбранные в строке отбора метки: база должна иметь все (условие «И»).</summary>
    private readonly HashSet<string> _tagFilter = new(TagNames.Comparer);

    /// <summary>Метки, которые есть хотя бы у одной базы, — строка отбора.</summary>
    public ObservableCollection<TagFilterViewModel> TagFilters { get; } = [];

    /// <summary>Есть метки у баз — показывается кнопка строки меток.</summary>
    public bool HasTags => TagFilters.Count > 0;

    /// <summary>Строка меток включена кнопкой на панели (запоминается); видна, если метки есть.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTagBarVisible))]
    public partial bool ShowTagBar { get; set; }

    public bool IsTagBarVisible => ShowTagBar && HasTags;

    /// <summary>Включён отбор по меткам: список — плоский, как при поиске.</summary>
    public bool HasTagFilter => _tagFilter.Count > 0;

    /// <summary>Пункты подменю «Метки» для выделенной базы или папки.</summary>
    public IReadOnlyList<TagMenuItemViewModel> TagMenuItems => BuildTagMenu();

    /// <summary>Метки можно ставить: выделена база или папка с базами.</summary>
    public bool HasTagTarget => TagTargets().Count > 0;

    /// <summary>Имена всех меток — подсказки при вводе.</summary>
    public IReadOnlyList<string> TagNameChoices => [.. _settings.UserData.TagCatalog().Select(t => t.Name)];

    /// <summary>Текст поля быстрого ввода метки (кнопка «+» в свойствах).</summary>
    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    partial void OnShowTagBarChanged(bool value)
    {
        if (!_suppressSettingsSync)
        {
            _settings.Settings.Ui.ShowTagBar = value;
            _settings.RequestSave();
        }

        // Скрытая строка не должна прятать базы: отбор снимается вместе с ней.
        if (!value && HasTagFilter)
        {
            ClearTagFilter();
        }
    }

    partial void OnSelectedInfoBaseChanged(InfoBaseViewModel? value) => OnTagTargetChanged();

    partial void OnSelectedFolderChanged(FolderNodeViewModel? value) => OnTagTargetChanged();

    private void OnTagTargetChanged()
    {
        OnPropertyChanged(nameof(TagMenuItems));
        OnPropertyChanged(nameof(HasTagTarget));
        AddTagCommand.NotifyCanExecuteChanged();
        NewTagCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleTagBar() => ShowTagBar = !ShowTagBar;

    /// <summary>Щелчок по метке в строке отбора: включить или выключить её.</summary>
    [RelayCommand]
    private void ToggleTagFilter(TagFilterViewModel? tag)
    {
        if (tag is null)
        {
            return;
        }

        if (!_tagFilter.Remove(tag.Name))
        {
            _tagFilter.Add(tag.Name);
        }

        tag.IsSelected = _tagFilter.Contains(tag.Name);
        OnTagFilterChanged();
    }

    [RelayCommand]
    private void ClearTagFilter()
    {
        _tagFilter.Clear();
        foreach (var tag in TagFilters)
        {
            tag.IsSelected = false;
        }

        OnTagFilterChanged();
    }

    private void OnTagFilterChanged()
    {
        var key = CurrentSelectionKey();
        OnPropertyChanged(nameof(HasTagFilter));
        OnPropertyChanged(nameof(ShowTree));
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(EmptyListText));
        RebuildList();
        Reselect(key);
        StatusText = HasTagFilter
            ? $"Отбор по меткам: {string.Join(" + ", _tagFilter.Order(StringComparer.CurrentCultureIgnoreCase))} — найдено {ListItems.OfType<BaseListItemViewModel>().Count()}."
            : "Отбор по меткам снят.";
    }

    /// <summary>Строка отбора — заново по меткам баз; выбранные метки, которых больше нет, из отбора выпадают.</summary>
    private void RefreshTagFilters()
    {
        var counts = new Dictionary<string, int>(TagNames.Comparer);
        foreach (var name in _bases.SelectMany(b => b.AssignedTags))
        {
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        var hadFilter = HasTagFilter;
        _tagFilter.IntersectWith(counts.Keys);
        TagFilters.Clear();
        foreach (var tag in _settings.UserData.TagCatalog().Where(t => counts.ContainsKey(t.Name)))
        {
            TagFilters.Add(new TagFilterViewModel(tag, counts[tag.Name]) { IsSelected = _tagFilter.Contains(tag.Name) });
        }

        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(IsTagBarVisible));
        OnPropertyChanged(nameof(TagNameChoices));
        if (hadFilter != HasTagFilter)
        {
            OnPropertyChanged(nameof(HasTagFilter));
            OnPropertyChanged(nameof(ShowTree));
            OnPropertyChanged(nameof(ShowList));
        }
    }

    /// <summary>Подходит ли база под отбор по меткам и под слова «#метка» из поиска.</summary>
    private bool MatchesTags(InfoBaseViewModel infoBase, IReadOnlyCollection<string> prefixes) =>
        TagNames.Matches(infoBase.AssignedTags, _tagFilter, prefixes);

    /// <summary>Выделенная база или все базы выделенной папки (с вложенными).</summary>
    private List<InfoBaseViewModel> TagTargets()
    {
        if (SelectedInfoBase is { } infoBase)
        {
            return [infoBase];
        }

        if (SelectedFolder is not { } folder)
        {
            return [];
        }

        var prefix = folder.Path + "/";
        return [.. _bases.Where(b => string.Equals(b.InfoBase.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase)
                                     || b.InfoBase.FolderPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
    }

    private List<TagMenuItemViewModel> BuildTagMenu()
    {
        var targets = TagTargets();
        var items = new List<TagMenuItemViewModel>();
        foreach (var tag in _settings.UserData.TagCatalog())
        {
            items.Add(new TagMenuItemViewModel
            {
                Header = tag.Name,
                Color = tag.Color,
                IsChecked = targets.Count > 0 && targets.All(b => b.AssignedTags.Contains(tag.Name, TagNames.Comparer)),
                Command = ToggleTagCommand,
                CommandParameter = tag.Name,
            });
        }

        if (items.Count > 0)
        {
            items.Add(TagMenuItemViewModel.Separator);
        }

        items.Add(new TagMenuItemViewModel { Header = "Новая метка…", Command = NewTagCommand });
        items.Add(new TagMenuItemViewModel { Header = "Управление метками…", Command = ManageTagsCommand });
        return items;
    }

    /// <summary>
    /// Подменю «Метки»: метка ставится всем выделенным базам; если она уже есть у всех — снимается.
    /// </summary>
    [RelayCommand]
    private void ToggleTag(string? name)
    {
        var targets = TagTargets();
        if (string.IsNullOrWhiteSpace(name) || targets.Count == 0)
        {
            return;
        }

        var on = !targets.All(b => b.AssignedTags.Contains(name, TagNames.Comparer));
        SetTagOn(targets, name, on);
    }

    /// <summary>Быстрый ввод в свойствах: поставить метку (новую — создать).</summary>
    [RelayCommand(CanExecute = nameof(HasTagTarget))]
    private void AddTag(string? name)
    {
        name ??= NewTagText;
        if (TagNames.Normalize(name) is not { } normalized)
        {
            return;
        }

        NewTagText = string.Empty;
        SetTagOn(TagTargets(), normalized, on: true);
    }

    /// <summary>Крестик на метке в свойствах базы.</summary>
    [RelayCommand]
    private void RemoveTag(string? name)
    {
        if (name is not null && SelectedInfoBase is { } infoBase)
        {
            SetTagOn([infoBase], name, on: false);
        }
    }

    [RelayCommand(CanExecute = nameof(HasTagTarget))]
    private async Task NewTagAsync()
    {
        var targets = TagTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var form = new NewTagViewModel(_settings.UserData.TagCatalog(), _settings.UserData.NextTagColor(), $"Метка будет поставлена: {TargetsText(targets)}.");
        if (!await _dialogs.CreateTagAsync(form) || TagNames.Normalize(form.Name) is not { } name)
        {
            return;
        }

        // Новая — с выбранным цветом; у имеющейся цвет свой.
        var created = _settings.UserData.Tag(name) is null;
        _settings.UserData.EnsureTag(name, form.Color);
        SetTagOn(targets, name, on: true);
        if (created)
        {
            StatusText += " Метка создана.";
        }
    }

    /// <summary>«Имя базы» или «N баз».</summary>
    private static string TargetsText(List<InfoBaseViewModel> targets) =>
        targets.Count == 1 ? $"«{targets[0].Name}»" : $"{targets.Count} {Plural(targets.Count, "база", "базы", "баз")}";

    private void SetTagOn(List<InfoBaseViewModel> targets, string name, bool on)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var created = on && _settings.UserData.Tag(name) is null;
        foreach (var infoBase in targets)
        {
            _settings.UserData.SetTag(infoBase.InfoBase, name, on);
            infoBase.RefreshTags();
        }

        var shown = _settings.UserData.Tag(name)?.Name ?? name;
        StatusText = on
            ? $"Метка «{shown}» поставлена: {TargetsText(targets)}." + (created ? " Метка создана." : string.Empty)
            : $"Метка «{shown}» снята: {TargetsText(targets)}.";
        AfterTagsChanged();
    }

    /// <summary>Окно «Метки»: создать, переименовать, удалить, выбрать цвет.</summary>
    [RelayCommand]
    private async Task ManageTagsAsync()
    {
        var counts = _settings.UserData.TagCatalog().ToDictionary(
            t => t.Name, t => _bases.Count(b => b.AssignedTags.Contains(t.Name, TagNames.Comparer)), TagNames.Comparer);
        var editor = new TagsEditorViewModel(_settings.UserData.TagCatalog(), counts);
        if (!await _dialogs.EditTagsAsync(editor))
        {
            return;
        }

        editor.ApplyTo(_settings.UserData);
        foreach (var infoBase in _bases)
        {
            infoBase.RefreshTags();
        }

        StatusText = "Метки сохранены.";
        AfterTagsChanged();
    }

    /// <summary>Метки изменились: сохранить настройки, обновить строку отбора и список.</summary>
    private void AfterTagsChanged()
    {
        _settings.RequestSave();
        var key = CurrentSelectionKey();
        RefreshTagFilters();
        OnPropertyChanged(nameof(TagMenuItems));
        OnPropertyChanged(nameof(EmptyListText));
        RebuildList();
        Reselect(key);
    }

    /// <summary>Метки исходной базы — её копии (дублирование).</summary>
    private void CopyTags(InfoBase source, string? copyKey)
    {
        var tags = _settings.UserData.TagsOf(source);
        if (tags.Count == 0 || _bases.FirstOrDefault(b => b.InfoBase.IdentityKey == copyKey) is not { } copy)
        {
            return;
        }

        _settings.UserData.SetTags(copy.InfoBase, tags);
        copy.RefreshTags();
        _settings.RequestSave();
        AfterTagsChanged();
    }
}
