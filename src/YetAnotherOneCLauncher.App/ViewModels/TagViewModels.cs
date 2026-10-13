using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.ViewModels;

/// <summary>Плашка метки: в строке списка и в свойствах базы.</summary>
/// <param name="tag">Метка.</param>
/// <param name="owner">База, у которой метка стоит (крестик «снять метку»); <c>null</c> — метка из каталога.</param>
public sealed class TagChipViewModel(TagDefinition tag, InfoBaseViewModel? owner = null)
{
    public InfoBaseViewModel? Owner => owner;

    public string Name => tag.Name;

    public TagColor Color => tag.Color;

    public bool ShowInRow => tag.ShowInRow;

    public override string ToString() => Name;
}

/// <summary>Метка в строке отбора: щелчок включает её в отбор (все выбранные — условие «И»).</summary>
public sealed partial class TagFilterViewModel(TagDefinition tag, int count) : ObservableObject
{
    public string Name => tag.Name;

    public TagColor Color => tag.Color;

    /// <summary>Сколько баз с этой меткой.</summary>
    public int Count => count;

    public string Text => $"{Name} · {Count}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public override string ToString() => Name;
}

/// <summary>Пункт подменю «Метки»: метка с галочкой или команда («Новая метка…», «Управление метками…»).</summary>
public sealed class TagMenuItemViewModel
{
    /// <summary>Разделитель: Avalonia рисует пункт с заголовком «-» чертой.</summary>
    public static TagMenuItemViewModel Separator { get; } = new() { Header = "-" };

    public string Header { get; init; } = string.Empty;

    /// <summary>Метка; <c>null</c> — пункт-команда.</summary>
    public TagColor? Color { get; init; }

    public bool IsTag => Color is not null;

    /// <summary>Цвет кружка у пункта (у команд кружок скрыт).</summary>
    public TagColor SwatchColor => Color ?? TagColor.Gray;

    /// <summary>Метка стоит у базы (у всех баз папки).</summary>
    public bool IsChecked { get; init; }

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public override string ToString() => Header;
}
