using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Строка таблицы «Инструменты».</summary>
public sealed class AdminToolRow
{
    public AdminToolRow(AdminTool tool, IAdminToolIconSource? icons)
    {
        Tool = tool;
        Icon = AdminToolIconViewModel.For(tool.Icon, tool.Target, icons);
    }

    public AdminTool Tool { get; }

    public string Name => Tool.Name;

    public string Target => Tool.Target;

    public AdminToolIconViewModel Icon { get; }

    /// <summary>Как выбран значок: подсказка в колонке «Значок».</summary>
    public string IconText => AdminToolsViewModel.IconTitle(Tool.Icon, Tool.Target);
}

/// <summary>Пункт меню выбора значка.</summary>
/// <param name="Icon">Значение <see cref="AdminTool.Icon"/>; <c>null</c> — автоматически.</param>
public sealed record AdminToolIconChoice(string? Icon, string Title, AdminToolIconViewModel Preview);

/// <summary>
/// Вкладка настроек «Средства администрирования»: таблица «Инструменты» (имя, строка запуска, значок), добавление,
/// изменение, удаление и порядок. Изменения применяются кнопкой «Сохранить» окна настроек.
/// </summary>
public sealed partial class AdminToolsViewModel : ObservableObject
{
    private readonly IAdminToolIconSource? _icons;
    private readonly IFileDialogService? _files;

    public AdminToolsViewModel(IEnumerable<AdminTool> tools, IAdminToolIconSource? icons = null, IFileDialogService? files = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _icons = icons;
        _files = files;
        foreach (var tool in tools)
        {
            Rows.Add(new AdminToolRow(tool, icons));
        }

        NewIconView = AdminToolIconViewModel.For(null, string.Empty, null);
    }

    public ObservableCollection<AdminToolRow> Rows { get; } = [];

    /// <summary>Инструменты в текущем порядке — результат вкладки.</summary>
    public List<AdminTool> Tools => Rows.Select(r => r.Tool).ToList();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(EditCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial AdminToolRow? SelectedRow { get; set; }

    /// <summary>Инструмент, который сейчас изменяется в полях ввода; <c>null</c> — поля для нового.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(FormTitle), nameof(ApplyText))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial AdminToolRow? EditingRow { get; set; }

    public bool IsEditing => EditingRow is not null;

    public string FormTitle => IsEditing ? "Изменение инструмента" : "Новый инструмент";

    public string ApplyText => IsEditing ? "Применить" : "Добавить";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial string NewTarget { get; set; } = string.Empty;

    /// <summary>Значок нового или изменяемого инструмента (<see cref="AdminTool.Icon"/>); <c>null</c> — автоматически.</summary>
    [ObservableProperty]
    public partial string? NewIcon { get; set; }

    /// <summary>Как выглядит выбранный значок в форме.</summary>
    [ObservableProperty]
    public partial AdminToolIconViewModel NewIconView { get; private set; }

    public string NewIconText => IconTitle(NewIcon, NewTarget);

    /// <summary>Почему инструмент не принят или значок не загружен; пусто — всё в порядке.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>Пункты меню значка: «Автоматически» и встроенные значки (свой — отдельной командой).</summary>
    public IReadOnlyList<AdminToolIconChoice> IconChoices =>
    [
        new(null, "Автоматически", AdminToolIconViewModel.For(null, NewTarget, _icons)),
        .. BuiltInToolIcon.All.Select(i => new AdminToolIconChoice(AdminToolIcon.BuiltIn(i.Id), i.Title, AdminToolIconViewModel.ForBuiltIn(i.Id))),
    ];

    /// <summary>Подпись выбранного значка.</summary>
    public static string IconTitle(string? icon, string target)
    {
        if (AdminToolIcon.BuiltInId(icon) is { } id)
        {
            return BuiltInToolIcon.All.FirstOrDefault(i => i.Id == id)?.Title ?? id;
        }

        if (AdminToolIcon.FileName(icon) is not null)
        {
            return "Свой значок";
        }

        return AdminToolTarget.KindOf(target) == AdminToolKind.WebService
            ? "Значок сайта"
            : "Значок программы";
    }

    partial void OnNewNameChanged(string value) => ErrorText = string.Empty;

    partial void OnNewTargetChanged(string value)
    {
        ErrorText = string.Empty;
        UpdateNewIconView();
    }

    partial void OnNewIconChanged(string? value) => UpdateNewIconView();

    // В форме значок сайта не скачивается на каждое нажатие клавиши: подобранный сам — у программы сразу, у сервиса — по умолчанию.
    private void UpdateNewIconView()
    {
        var source = NewIcon is null && AdminToolTarget.KindOf(NewTarget) == AdminToolKind.WebService ? null : _icons;
        NewIconView = AdminToolIconViewModel.For(NewIcon, NewTarget.Trim(), source);
        OnPropertyChanged(nameof(NewIconText));
        OnPropertyChanged(nameof(IconChoices));
    }

    /// <summary>Добавить новый инструмент или применить изменение выбранного.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var name = NewName.Trim();
        if (Rows.Any(r => r != EditingRow && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = "Инструмент с таким именем уже есть.";
            return;
        }

        var row = new AdminToolRow(new AdminTool { Name = name, Target = NewTarget.Trim(), Icon = NewIcon }, _icons);
        if (EditingRow is { } editing)
        {
            Rows[Rows.IndexOf(editing)] = row; // на прежнем месте
        }
        else
        {
            Rows.Add(row);
        }

        SelectedRow = row;
        ClearForm();
    }

    private bool CanApply() => NewName.Trim().Length > 0 && NewTarget.Trim().Length > 0;

    /// <summary>Выбранный инструмент — в поля ввода для изменения.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        EditingRow = row;
        NewName = row.Name;
        NewTarget = row.Target;
        NewIcon = row.Tool.Icon;
    }

    /// <summary>Отказаться от изменения: поля снова для нового инструмента.</summary>
    [RelayCommand]
    private void CancelEdit() => ClearForm();

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)]; // чтобы удалять подряд
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(+1);

    /// <summary>Выбрать программу для строки запуска.</summary>
    [RelayCommand]
    private async Task BrowseProgramAsync()
    {
        if (_files is null)
        {
            return;
        }

        string[] patterns = OperatingSystem.IsWindows() ? ["*.exe", "*.msc", "*.cmd", "*.bat", "*.lnk"] : ["*"];
        if (await _files.OpenFileAsync("Программа", "Программы", patterns) is { } path)
        {
            NewTarget = path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
            if (NewName.Trim().Length == 0)
            {
                NewName = Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    /// <summary>Значок из меню: «Автоматически» (<c>null</c>) или встроенный.</summary>
    [RelayCommand]
    private void ChooseIcon(string? icon) => NewIcon = icon;

    /// <summary>Загрузить свою картинку (PNG, ICO, JPEG, BMP): копируется в каталог значков лаунчера.</summary>
    [RelayCommand]
    private async Task ImportIconAsync()
    {
        if (_files is null || _icons is null)
        {
            return;
        }

        if (await _files.OpenFileAsync("Значок", "Картинки", ["*.png", "*.ico", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"]) is not { } path)
        {
            return;
        }

        if (_icons.Import(path) is { } fileName)
        {
            NewIcon = AdminToolIcon.File(fileName);
            ErrorText = string.Empty;
        }
        else
        {
            ErrorText = "Файл не открывается как картинка. Подойдут PNG, ICO, JPEG, BMP, GIF, WebP.";
        }
    }

    private bool HasSelection() => SelectedRow is not null;

    // Изменяемый сейчас не удаляется: сначала применить или отменить изменение.
    private bool CanDelete() => HasSelection() && SelectedRow != EditingRow;

    private bool CanMoveUp() => SelectedRow is { } row && Rows.IndexOf(row) > 0;

    private bool CanMoveDown() => SelectedRow is { } row && Rows.IndexOf(row) < Rows.Count - 1;

    private void Move(int offset)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        Rows.Move(index, index + offset);
        SelectedRow = row;
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void ClearForm()
    {
        EditingRow = null;
        NewName = string.Empty;
        NewTarget = string.Empty;
        NewIcon = null;
        ErrorText = string.Empty;
    }
}
