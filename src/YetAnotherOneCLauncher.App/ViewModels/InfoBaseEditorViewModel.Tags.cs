using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Поле «Метки» в форме базы: метки плашками с крестиком и «+ Добавить» — выбор из имеющихся или новая
/// (хранятся в настройках лаунчера, список баз не меняется; сохраняются вместе с формой).
/// </summary>
public sealed partial class InfoBaseEditorViewModel
{
    private List<string> _tags = [];

    /// <summary>Все метки — для выбора и цвета плашек.</summary>
    public IReadOnlyList<TagDefinition> TagCatalog { get; init; } = [];

    /// <summary>Метки базы при открытии формы — чтобы понять, меняли ли их.</summary>
    public IReadOnlyList<string> InitialTags
    {
        get;
        init
        {
            field = value;
            _tags = [.. value];
        }
    } = [];

    /// <summary>Метки в форме.</summary>
    public IReadOnlyList<string> Tags => _tags;

    /// <summary>Плашки: цвет — из каталога, новая метка (ещё не создана) — серая.</summary>
    public IReadOnlyList<TagChipViewModel> TagChips =>
    [
        .. _tags.Select(name => new TagChipViewModel(
            TagCatalog.FirstOrDefault(t => TagNames.Same(t.Name, name)) ?? new TagDefinition { Name = name, Color = TagColor.Gray })),
    ];

    public bool HasTags => _tags.Count > 0;

    /// <summary>«+ Добавить»: имеющиеся метки, которых ещё нет в форме, и ввод новой.</summary>
    public TagPickerViewModel TagPicker => field ??= new TagPickerViewModel(() => TagCatalog, () => _tags, AddTag);

    public bool IsTagsChanged =>
        !_tags.Order(TagNames.Comparer).SequenceEqual(InitialTags.Order(TagNames.Comparer), TagNames.Comparer);

    /// <summary>Крестик на плашке.</summary>
    [RelayCommand]
    private void RemoveTag(string? name)
    {
        if (name is not null && _tags.RemoveAll(t => TagNames.Same(t, name)) > 0)
        {
            OnTagsChanged();
        }
    }

    private void AddTag(string name)
    {
        if (TagNames.Normalize(name) is { } tag && !_tags.Contains(tag, TagNames.Comparer))
        {
            _tags.Add(tag);
            OnTagsChanged();
        }
    }

    private void OnTagsChanged()
    {
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagChips));
        OnPropertyChanged(nameof(HasTags));
    }
}
