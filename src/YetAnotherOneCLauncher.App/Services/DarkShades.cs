using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>
/// Оттенок тёмной темы: цвета фона окон, панелей и полей. Свои цвета лаунчера в тёмной теме — полупрозрачные белые
/// (подложка строк, заголовок списка, плоские кнопки), поэтому сами подстраиваются под любой фон.
/// </summary>
/// <param name="Name">Название в настройках.</param>
/// <param name="Region">Фон окон.</param>
/// <param name="ChromeLow">Тёмные края (заголовок, полосы прокрутки).</param>
/// <param name="ChromeMedium">Панели, выпадающие списки.</param>
/// <param name="ChromeMediumLow">Поля ввода.</param>
/// <param name="ChromeHigh">Рамки и выделение элементов.</param>
// Кисти — неизменяемые: список оттенков общий (статический), а обычные кисти привязаны к потоку, где созданы.
public sealed record DarkShadePalette(DarkShade Shade, string Name, Color Region, Color ChromeLow, Color ChromeMedium, Color ChromeMediumLow, Color ChromeHigh)
{
    public IBrush RegionBrush { get; } = new ImmutableSolidColorBrush(Region);

    /// <summary>Кнопки и поля в превью — как плоские кнопки лаунчера: лёгкая белая заливка поверх фона.</summary>
    public IBrush ButtonBrush { get; } = new ImmutableSolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    public IBrush StripeBrush { get; } = new ImmutableSolidColorBrush(Color.FromArgb(0x13, 0xFF, 0xFF, 0xFF));

    public IBrush HeaderBrush { get; } = new ImmutableSolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));

    public IBrush BorderBrush { get; } = new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
}

/// <summary>Оттенки тёмной темы и их применение: Fluent получает палитру тёмной темы (кроме «Чёрного» — там своя).</summary>
public static class DarkShades
{
    // Оттенок, который сейчас в теме Fluent; тема из разметки (при запуске) — стандартная, «Чёрный».
    private static DarkShade _applied = DarkShade.Black;

    /// <summary>В порядке <see cref="DarkShade"/>: так они идут в настройках.</summary>
    public static IReadOnlyList<DarkShadePalette> All { get; } =
    [
        new(DarkShade.Black, "Чёрный", Color.Parse("#000000"), Color.Parse("#171717"), Color.Parse("#1F1F1F"), Color.Parse("#2B2B2B"), Color.Parse("#333333")),
        new(DarkShade.Graphite, "Графит", Color.Parse("#262624"), Color.Parse("#1F1E1D"), Color.Parse("#30302E"), Color.Parse("#3A3A37"), Color.Parse("#4A4945")),
        new(DarkShade.Neutral, "Нейтральный серый", Color.Parse("#2B2B2B"), Color.Parse("#232323"), Color.Parse("#333333"), Color.Parse("#3C3C3C"), Color.Parse("#4D4D4D")),
        new(DarkShade.Slate, "Сине-серый", Color.Parse("#22272E"), Color.Parse("#1C2128"), Color.Parse("#2D333B"), Color.Parse("#373E47"), Color.Parse("#444C56")),
    ];

    public static DarkShadePalette Of(DarkShade shade) => All.FirstOrDefault(p => p.Shade == shade) ?? All[1];

    /// <summary>
    /// Применить оттенок: тема Fluent заменяется новой с палитрой тёмной темы. Палитру уже загруженной темы Fluent
    /// не перечитывает, а замена темы перекрашивает и открытые окна.
    /// </summary>
    public static void Apply(Application application, DarkShade shade)
    {
        ArgumentNullException.ThrowIfNull(application);
        var current = application.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (current is null || shade == _applied)
        {
            return;
        }

        var theme = new FluentTheme { DensityStyle = current.DensityStyle };
        if (Palette(shade) is { } palette)
        {
            theme.Palettes[ThemeVariant.Dark] = palette;
        }

        _applied = shade;
        application.Styles[application.Styles.IndexOf(current)] = theme;
    }

    /// <summary>Палитра тёмной темы Fluent; <c>null</c> — стандартная («Чёрный»).</summary>
    internal static ColorPaletteResources? Palette(DarkShade shade)
    {
        if (shade == DarkShade.Black)
        {
            return null;
        }

        var p = Of(shade);
        Color Alpha(byte alpha) => Color.FromArgb(alpha, p.Region.R, p.Region.G, p.Region.B);
        return new ColorPaletteResources
        {
            RegionColor = p.Region,
            AltHigh = p.Region,
            AltMediumHigh = Alpha(0xCC),
            AltMedium = Alpha(0x99),
            AltMediumLow = Alpha(0x66),
            AltLow = Alpha(0x33),
            ChromeAltLow = Color.Parse("#CCCCCC"),
            ChromeLow = p.ChromeLow,
            ChromeMedium = p.ChromeMedium,
            ChromeMediumLow = p.ChromeMediumLow,
            ChromeHigh = p.ChromeHigh,
        };
    }
}
