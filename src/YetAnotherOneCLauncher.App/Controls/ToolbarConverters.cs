using Avalonia;
using Avalonia.Data.Converters;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>Преобразования для панели главного окна.</summary>
public static class ToolbarConverters
{
    /// <summary>Ширина сегмента тумблера режимов (как в стилях MainWindow.axaml).</summary>
    public const double SwitchSegmentWidth = 48;

    /// <summary>Сдвиг плашки тумблера «Все базы / Недавние / Избранное» под выбранный режим.</summary>
    public static FuncValueConverter<BaseListFilter, Thickness> SwitchThumbOffset { get; } =
        new(filter => new Thickness((int)filter * SwitchSegmentWidth, 0, 0, 0));
}
