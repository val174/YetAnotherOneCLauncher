using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Поле «Метки» в форме базы: метки через запятую (хранятся в настройках лаунчера, список баз не меняется);
/// кнопка «▾» — поставить или снять имеющуюся метку.
/// </summary>
public sealed partial class InfoBaseEditorViewModel
{
    /// <summary>Все метки — для выбора в списке «▾».</summary>
    public IReadOnlyList<string> TagChoices { get; init; } = [];

    public bool HasTagChoices => TagChoices.Count > 0;

    /// <summary>Метки базы при открытии формы — чтобы понять, меняли ли их.</summary>
    public IReadOnlyList<string> InitialTags
    {
        get;
        init
        {
            field = value;
            TagsText = string.Join(", ", value);
        }
    } = [];

    [ObservableProperty]
    public partial string TagsText { get; set; } = string.Empty;

    /// <summary>Метки из поля: через запятую или точку с запятой, без повторов.</summary>
    public IReadOnlyList<string> Tags =>
        [.. TagsText.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(TagNames.Normalize)
            .OfType<string>()
            .Distinct(TagNames.Comparer)];

    public bool IsTagsChanged =>
        !Tags.Order(TagNames.Comparer).SequenceEqual(InitialTags.Order(TagNames.Comparer), TagNames.Comparer);

    /// <summary>Список «▾»: есть метка в поле — убрать, нет — дописать.</summary>
    [RelayCommand]
    private void ToggleTagChoice(string? name)
    {
        if (TagNames.Normalize(name) is not { } tag)
        {
            return;
        }

        var tags = Tags.ToList();
        if (tags.RemoveAll(t => TagNames.Same(t, tag)) == 0)
        {
            tags.Add(tag);
        }

        TagsText = string.Join(", ", tags);
    }
}
