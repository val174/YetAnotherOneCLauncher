using Avalonia;
using Avalonia.Controls;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>
/// Элементы в строку, пока помещаются целиком; те, что не влезли, не показываются (а не обрезаются посередине).
/// Метки в строке списка баз: места после наименования бывает мало.
/// </summary>
public sealed class FitPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FitPanel, double>(nameof(Spacing), 4);

    public FitPanel() => ClipToBounds = true;

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Насколько может вырасти элемент под указателем (у метки появляется крестик) и всё равно остаться: иначе метка у края
    /// то пропадала бы, то появлялась.
    /// </summary>
    private const double HoverGrowth = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            var next = width + (width > 0 ? Spacing : 0) + child.DesiredSize.Width;
            if (next - (child.IsPointerOver ? HoverGrowth : 0) > availableSize.Width)
            {
                break;
            }

            width = next;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var fits = true;
        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            fits = fits && x + width - (child.IsPointerOver ? HoverGrowth : 0) <= finalSize.Width + 0.5;
            if (fits)
            {
                child.Arrange(new Rect(x, (finalSize.Height - child.DesiredSize.Height) / 2, width, child.DesiredSize.Height));
                x += width + Spacing;
            }
            else
            {
                // За правым краем: панель обрезает своё содержимое, и метка не видна совсем.
                child.Arrange(new Rect(finalSize.Width + 1, 0, width, child.DesiredSize.Height));
            }
        }

        return finalSize;
    }
}
