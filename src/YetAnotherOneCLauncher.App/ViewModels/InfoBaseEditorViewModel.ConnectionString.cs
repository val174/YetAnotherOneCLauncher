using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>
/// Поле «Строка подключения» при добавлении существующей базы: по вставленному тексту
/// (<see cref="ConnectionStringDetector"/>) заполняются расположение, каталог, адрес публикации,
/// кластер и имя базы в кластере, а пустое название — именем каталога, базы или публикации.
/// </summary>
public sealed partial class InfoBaseEditorViewModel
{
    // Название, которое подставила строка подключения: пока его не меняли, следующая строка его заменяет.
    private string? _suggestedName;

    // Ключи вставленной строки подключения (например, wsn) — сохраняются вместе с базой.
    private ConnectionString? _pastedConnection;

    /// <summary>Поле есть только при добавлении существующей базы.</summary>
    public bool ShowConnectionStringField => IsNew && !IsCreateMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionHint))]
    public partial string ConnectionText { get; set; } = string.Empty;

    /// <summary>Что определено по строке подключения, или почему не получилось.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionHint))]
    public partial string ConnectionHint { get; private set; } = string.Empty;

    /// <summary>Строку разобрать не удалось или в ней не хватает данных — подсказка как предупреждение.</summary>
    [ObservableProperty]
    public partial bool IsConnectionHintWarning { get; private set; }

    public bool HasConnectionHint => ConnectionHint.Length > 0;

    partial void OnConnectionTextChanged(string value)
    {
        var detected = ConnectionStringDetector.Detect(value);
        _pastedConnection = null;
        ConnectionHint = detected.Kind == ConnectionKind.None ? string.Empty : detected.Description;
        IsConnectionHintWarning = !detected.IsRecognized || detected.IsIncomplete;
        if (!detected.IsRecognized)
        {
            return;
        }

        KindIndex = Array.IndexOf(Kinds, detected.Kind);
        switch (detected.Kind)
        {
            case ConnectionKind.File:
                FilePath = detected.FilePath;
                break;
            case ConnectionKind.Server:
                Server = detected.Server;
                InfobaseName = detected.InfobaseName;
                break;
            case ConnectionKind.Web:
                WebUrl = detected.WebUrl;
                break;
        }

        _pastedConnection = detected.Connection;
        if (detected.SuggestedName.Length > 0 && (string.IsNullOrWhiteSpace(Name) || Name == _suggestedName))
        {
            Name = detected.SuggestedName;
            _suggestedName = detected.SuggestedName;
        }
    }
}
