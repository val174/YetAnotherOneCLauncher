using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.Core.Edt;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Поле «Проект 1C:EDT» в форме базы: связь базы с проектом EDT Start (хранится в настройках лаунчера, в профиле базы).
/// Если сам EDT связал проект с этой базой — подсказка «Привязать».
/// </summary>
public sealed partial class InfoBaseEditorViewModel
{
    /// <summary>Проекты EDT Start; пусто — поля нет.</summary>
    public IReadOnlyList<EdtProject> EdtProjects { get; init; } = [];

    /// <summary>Выбор проекта из списка названий; индекс или <c>null</c> — отмена. Задаёт главное окно.</summary>
    public Func<IReadOnlyList<string>, Task<int?>>? EdtProjectChooser { get; init; }

    /// <summary>Проект, с которым EDT связал эту базу (по её ID); <c>null</c> — не связал.</summary>
    public EdtProject? SuggestedEdtProject { get; init; }

    /// <summary>Связь при открытии формы — чтобы понять, меняли ли её.</summary>
    public string? InitialEdtProjectId
    {
        get;
        init
        {
            field = value;
            EdtProjectId = value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EdtProject), nameof(EdtProjectText), nameof(HasEdtProject), nameof(ShowEdtSuggestion), nameof(EdtSuggestionText))]
    [NotifyCanExecuteChangedFor(nameof(ClearEdtProjectCommand))]
    public partial string? EdtProjectId { get; set; }

    public bool ShowEdtProjectField => EdtProjects.Count > 0 || EdtProjectId is not null;

    public EdtProject? EdtProject => EdtProjects.FirstOrDefault(p => string.Equals(p.Id, EdtProjectId, StringComparison.OrdinalIgnoreCase));

    public string EdtProjectText => EdtProject?.Name ?? (EdtProjectId is null ? "Не выбран" : "Проект не найден в 1C:EDT Start");

    public bool HasEdtProject => EdtProjectId is not null;

    /// <summary>Связь изменили в форме — сохранить после «Сохранить».</summary>
    public bool IsEdtProjectChanged => !string.Equals(EdtProjectId, InitialEdtProjectId, StringComparison.OrdinalIgnoreCase);

    public bool ShowEdtSuggestion => SuggestedEdtProject is { } suggested && !string.Equals(suggested.Id, EdtProjectId, StringComparison.OrdinalIgnoreCase);

    public string EdtSuggestionText => SuggestedEdtProject is { } suggested
        ? $"1C:EDT связывает эту базу с проектом «{suggested.Name}»."
        : string.Empty;

    [RelayCommand]
    private async Task ChooseEdtProjectAsync()
    {
        if (EdtProjectChooser is null || EdtProjects.Count == 0)
        {
            return;
        }

        if (await EdtProjectChooser([.. EdtProjects.Select(p => $"{p.Name} — {p.Workspace}")]) is { } index && index >= 0 && index < EdtProjects.Count)
        {
            EdtProjectId = EdtProjects[index].Id;
        }
    }

    [RelayCommand(CanExecute = nameof(HasEdtProject))]
    private void ClearEdtProject() => EdtProjectId = null;

    /// <summary>«Привязать»: взять проект, с которым базу связал сам EDT.</summary>
    [RelayCommand]
    private void AcceptEdtSuggestion() => EdtProjectId = SuggestedEdtProject?.Id;
}
