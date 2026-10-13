using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Окно «Новая метка» (контекстное меню → «Метки» → «Новая метка…»): наименование и цвет. Если метка с таким именем
/// уже есть, она ставится со своим цветом — цвет меняют в окне «Метки».
/// </summary>
public sealed partial class NewTagViewModel : ObservableObject
{
    private readonly IReadOnlyList<TagDefinition> _catalog;

    public NewTagViewModel(IReadOnlyList<TagDefinition> catalog, TagColor color, string targetText)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Color = color;
        TargetText = targetText;
    }

    /// <summary>Кому ставится метка: «Метка будет поставлена: …».</summary>
    public string TargetText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Existing), nameof(IsExisting), nameof(PreviewName), nameof(PreviewColor))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorIndex), nameof(PreviewColor))]
    public partial TagColor Color { get; set; }

    /// <summary>Номер цвета в палитре (<see cref="TagsEditorViewModel.Colors"/>).</summary>
    public int ColorIndex
    {
        get => (int)Color;
        set
        {
            if (Enum.IsDefined((TagColor)value))
            {
                Color = (TagColor)value;
            }
        }
    }

    /// <summary>Метка с таким именем из каталога.</summary>
    public TagDefinition? Existing => TagNames.Normalize(Name) is { } name ? _catalog.FirstOrDefault(t => TagNames.Same(t.Name, name)) : null;

    public bool IsExisting => Existing is not null;

    /// <summary>Образец «Так будет выглядеть».</summary>
    public string PreviewName => Existing?.Name ?? TagNames.Normalize(Name) ?? "метка";

    public TagColor PreviewColor => Existing?.Color ?? Color;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; set; }

    public bool HasError => ErrorText is not null;

    /// <returns><c>true</c> — наименование задано, можно создавать.</returns>
    public bool Validate()
    {
        ErrorText = TagNames.Normalize(Name) is null ? "Укажите наименование метки." : null;
        return ErrorText is null;
    }

    partial void OnNameChanged(string value) => ErrorText = null;
}
