using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Services;

/// <summary>
/// Оттенок светлой или тёмной темы: цвета фона окон, панелей и полей. Свои цвета лаунчера — полупрозрачные (чёрные в
/// светлой теме, белые в тёмной: подложка строк, заголовок списка, плоские кнопки), поэтому сами подстраиваются под фон.
/// Кисти — неизменяемые: список оттенков общий (статический), а обычные кисти привязаны к потоку, где созданы.
/// </summary>
/// <param name="Value">Значение настройки (<see cref="LightShade"/> или <see cref="DarkShade"/>).</param>
/// <param name="Name">Название в настройках.</param>
/// <param name="IsDark">Оттенок тёмной темы.</param>
/// <param name="Region">Фон окон.</param>
/// <param name="ChromeLow">Края (заголовок, полосы прокрутки).</param>
/// <param name="ChromeMedium">Панели, выпадающие списки.</param>
/// <param name="ChromeMediumLow">Поля ввода (в тёмной теме).</param>
/// <param name="ChromeHigh">Рамки и выделение элементов.</param>
public sealed record ShadePalette(int Value, string Name, bool IsDark, Color Region, Color ChromeLow, Color ChromeMedium, Color ChromeMediumLow, Color ChromeHigh)
{
    public IBrush RegionBrush { get; } = new ImmutableSolidColorBrush(Region);

    /// <summary>Текст в превью.</summary>
    public IBrush TextBrush { get; } = new ImmutableSolidColorBrush(IsDark ? Color.Parse("#EDEDED") : Color.Parse("#1A1A1A"));

    /// <summary>Кнопки и поиск в превью — как плоские кнопки лаунчера: лёгкая заливка поверх фона.</summary>
    public IBrush ButtonBrush { get; } = new ImmutableSolidColorBrush(Ink(IsDark, IsDark ? (byte)0x24 : (byte)0x14));

    public IBrush StripeBrush { get; } = new ImmutableSolidColorBrush(Ink(IsDark, IsDark ? (byte)0x13 : (byte)0x0B));

    public IBrush HeaderBrush { get; } = new ImmutableSolidColorBrush(Ink(IsDark, IsDark ? (byte)0x12 : (byte)0x0D));

    public IBrush BorderBrush { get; } = new ImmutableSolidColorBrush(Ink(IsDark, 0x30));

    /// <summary>Выделенная строка — как в списке баз.</summary>
    public IBrush SelectionBrush { get; } = new ImmutableSolidColorBrush(IsDark ? Color.Parse("#1F5A99") : Color.Parse("#99C9EF"));

    /// <summary>Поле поиска в превью: в светлой теме поля белые на тонированном фоне.</summary>
    public IBrush FieldBrush { get; } = new ImmutableSolidColorBrush(IsDark ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

    private static Color Ink(bool dark, byte alpha) => dark ? Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF) : Color.FromArgb(alpha, 0x00, 0x00, 0x00);
}

/// <summary>
/// Оттенки светлой и тёмной темы и их применение: Fluent получает палитры (кроме «Белого» и «Чёрного» — там стандартные).
/// </summary>
public static class ThemeShades
{
    // Оттенки, которые сейчас в теме Fluent; тема из разметки (при запуске) — стандартная: «Белый» и «Чёрный».
    private static (LightShade Light, DarkShade Dark) _applied = (LightShade.White, DarkShade.Black);

    /// <summary>Оттенки светлой темы в порядке <see cref="LightShade"/>: так они идут в настройках.</summary>
    public static IReadOnlyList<ShadePalette> Light { get; } =
    [
        new((int)LightShade.White, "Белый", false, Color.Parse("#FFFFFF"), Color.Parse("#F2F2F2"), Color.Parse("#E6E6E6"), Color.Parse("#F2F2F2"), Color.Parse("#CCCCCC")),
        new((int)LightShade.Warm, "Тёплый", false, Color.Parse("#F7F5EF"), Color.Parse("#EFECE3"), Color.Parse("#E8E5DA"), Color.Parse("#F1EEE6"), Color.Parse("#D6D2C4")),
        new((int)LightShade.Gray, "Светло-серый", false, Color.Parse("#F3F3F3"), Color.Parse("#EBEBEB"), Color.Parse("#E3E3E3"), Color.Parse("#EDEDED"), Color.Parse("#CFCFCF")),
        new((int)LightShade.Cool, "Голубоватый", false, Color.Parse("#F2F5F9"), Color.Parse("#E9EEF4"), Color.Parse("#E1E7EF"), Color.Parse("#EBF0F6"), Color.Parse("#CBD5E1")),
    ];

    /// <summary>Оттенки тёмной темы в порядке <see cref="DarkShade"/>.</summary>
    public static IReadOnlyList<ShadePalette> Dark { get; } =
    [
        new((int)DarkShade.Black, "Чёрный", true, Color.Parse("#000000"), Color.Parse("#171717"), Color.Parse("#1F1F1F"), Color.Parse("#2B2B2B"), Color.Parse("#333333")),
        new((int)DarkShade.Graphite, "Графит", true, Color.Parse("#262624"), Color.Parse("#1F1E1D"), Color.Parse("#30302E"), Color.Parse("#3A3A37"), Color.Parse("#4A4945")),
        new((int)DarkShade.Neutral, "Нейтральный серый", true, Color.Parse("#2B2B2B"), Color.Parse("#232323"), Color.Parse("#333333"), Color.Parse("#3C3C3C"), Color.Parse("#4D4D4D")),
        new((int)DarkShade.Slate, "Сине-серый", true, Color.Parse("#22272E"), Color.Parse("#1C2128"), Color.Parse("#2D333B"), Color.Parse("#373E47"), Color.Parse("#444C56")),
    ];

    public static ShadePalette Of(LightShade shade) => Light.FirstOrDefault(p => p.Value == (int)shade) ?? Light[0];

    public static ShadePalette Of(DarkShade shade) => Dark.FirstOrDefault(p => p.Value == (int)shade) ?? Dark[1];

    /// <summary>
    /// Применить оттенки: тема Fluent заменяется новой с палитрами. Палитры уже загруженной темы Fluent не перечитывает,
    /// а замена темы перекрашивает и открытые окна.
    /// </summary>
    public static void Apply(Application application, LightShade light, DarkShade dark)
    {
        ArgumentNullException.ThrowIfNull(application);
        var current = application.Styles.OfType<FluentTheme>().FirstOrDefault();
        if (current is null || (light, dark) == _applied)
        {
            return;
        }

        var theme = new FluentTheme { DensityStyle = current.DensityStyle };
        if (Palette(light) is { } lightPalette)
        {
            theme.Palettes[ThemeVariant.Light] = lightPalette;
        }

        if (Palette(dark) is { } darkPalette)
        {
            theme.Palettes[ThemeVariant.Dark] = darkPalette;
        }

        _applied = (light, dark);
        application.Styles[application.Styles.IndexOf(current)] = theme;
    }

    /// <summary>Палитра светлой темы Fluent; <c>null</c> — стандартная («Белый»). Поля и списки остаются белыми.</summary>
    internal static ColorPaletteResources? Palette(LightShade shade)
    {
        if (shade == LightShade.White)
        {
            return null;
        }

        var p = Of(shade);
        return new ColorPaletteResources
        {
            RegionColor = p.Region,
            AltHigh = Colors.White,
            ChromeLow = p.ChromeLow,
            ChromeMedium = p.ChromeMedium,
            ChromeMediumLow = p.ChromeMediumLow,
            ChromeHigh = p.ChromeHigh,
        };
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
