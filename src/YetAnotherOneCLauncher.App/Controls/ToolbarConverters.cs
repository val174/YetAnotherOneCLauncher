using Avalonia;
using Avalonia.Data.Converters;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>Преобразования для панели главного окна.</summary>
public static class ToolbarConverters
{
    /// <summary>Ширина сегмента тумблера режимов (как в стилях MainWindow.axaml).</summary>
    public const double SwitchSegmentWidth = 48;

    /// <summary>
    /// Сдвиг плашки тумблера «Все базы / Проекты 1C:EDT / Недавние / Избранное» под выбранное положение — по номеру
    /// среди видимых положений (без проектов EDT их три).
    /// </summary>
    public static FuncValueConverter<int, Thickness> SwitchThumbOffset { get; } =
        new(position => new Thickness(position * SwitchSegmentWidth, 0, 0, 0));
}
