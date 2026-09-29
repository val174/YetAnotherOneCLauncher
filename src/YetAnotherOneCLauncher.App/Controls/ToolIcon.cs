using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>
/// Значок из линий, нарисованный в выбранном стиле (docs/ICON-VARIANTS.md): цветной контур («Стиль 1»)
/// или знак на цветной плашке («Стиль 2»). Стиль наследуется от окна через <see cref="IconStyleProperty"/>;
/// геометрия задаётся в координатах 16×16 и масштабируется под размер элемента (по умолчанию <see cref="DefaultSize"/>).
/// </summary>
public sealed class ToolIcon : Control
{
    /// <summary>Размер значка, если у элемента не задан свой.</summary>
    public const double DefaultSize = 18;

    private const double GridSize = 16;
    private const double PlateGlyphScale = 0.72;
    private const double PlateDarkGlyphLuminance = 0.55;
    private const double PlateCornerRadius = 3.5;
    private const double BoldFactor = 1.6;

    private static readonly IBrush DarkGlyph = new ImmutableSolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));

    /// <summary>Стиль значков: задаётся на окне и наследуется всеми значками внутри.</summary>
    public static readonly AttachedProperty<IconStyle> IconStyleProperty =
        AvaloniaProperty.RegisterAttached<ToolIcon, Control, IconStyle>("IconStyle", IconStyle.Outline, inherits: true);

    /// <summary>Линии знака.</summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<ToolIcon, Geometry?>(nameof(Data));

    /// <summary>Линии потолще: зубцы шестерёнки.</summary>
    public static readonly StyledProperty<Geometry?> BoldDataProperty =
        AvaloniaProperty.Register<ToolIcon, Geometry?>(nameof(BoldData));

    /// <summary>Часть знака, закрашенная сплошь: половина круга у темы «как в системе».</summary>
    public static readonly StyledProperty<Geometry?> SolidDataProperty =
        AvaloniaProperty.Register<ToolIcon, Geometry?>(nameof(SolidData));

    /// <summary>Цвет значка по смыслу кнопки.</summary>
    public static readonly StyledProperty<IBrush?> BrushProperty =
        AvaloniaProperty.Register<ToolIcon, IBrush?>(nameof(Brush));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ToolIcon, double>(nameof(StrokeThickness), 1.3);

    static ToolIcon()
    {
        AffectsRender<ToolIcon>(IconStyleProperty, DataProperty, BoldDataProperty, SolidDataProperty, BrushProperty, StrokeThicknessProperty);
    }

    public static IconStyle GetIconStyle(Control control) => control.GetValue(IconStyleProperty);

    public static void SetIconStyle(Control control, IconStyle value) => control.SetValue(IconStyleProperty, value);

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Geometry? BoldData
    {
        get => GetValue(BoldDataProperty);
        set => SetValue(BoldDataProperty, value);
    }

    public Geometry? SolidData
    {
        get => GetValue(SolidDataProperty);
        set => SetValue(SolidDataProperty, value);
    }

    public IBrush? Brush
    {
        get => GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DefaultSize, DefaultSize);

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
        {
            return;
        }

        // Квадрат по центру элемента, координаты знака — 0..16.
        var scale = size / GridSize;
        var offset = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);
        using var grid = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));

        var accent = Brush ?? Brushes.Gray;
        if (GetValue(IconStyleProperty) == IconStyle.Plate)
        {
            context.DrawRectangle(accent, null, new RoundedRect(new Rect(0, 0, GridSize, GridSize), PlateCornerRadius));
            var inset = GridSize * (1 - PlateGlyphScale) / 2;
            var glyph = GlyphOnPlate(accent);
            using (context.PushTransform(Matrix.CreateScale(PlateGlyphScale, PlateGlyphScale) * Matrix.CreateTranslation(inset, inset)))
            {
                // Линии толще, чтобы после уменьшения знак не стал тоньше, чем в «Стиле 1».
                DrawGlyph(context, glyph, StrokeThickness / PlateGlyphScale * 0.95);
            }
        }
        else
        {
            DrawGlyph(context, accent, StrokeThickness);
        }
    }

    private void DrawGlyph(DrawingContext context, IBrush brush, double thickness)
    {
        if (SolidData is { } solid)
        {
            context.DrawGeometry(brush, null, solid);
        }

        if (Data is { } data)
        {
            context.DrawGeometry(null, CreatePen(brush, thickness), data);
        }

        if (BoldData is { } bold)
        {
            context.DrawGeometry(null, CreatePen(brush, thickness * BoldFactor), bold);
        }
    }

    /// <summary>
    /// Знак на плашке: белый на насыщенном цвете (светлая тема), почти чёрный на светлом (кисти тёмной темы бледнее).
    /// </summary>
    private static IBrush GlyphOnPlate(IBrush plate)
    {
        if (plate is not ISolidColorBrush { Color: var c })
        {
            return Brushes.White;
        }

        var luminance = ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255;
        return luminance > PlateDarkGlyphLuminance ? DarkGlyph : Brushes.White;
    }

    private static Pen CreatePen(IBrush brush, double thickness) =>
        new(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
}
