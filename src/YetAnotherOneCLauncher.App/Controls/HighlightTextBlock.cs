using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>Текст с подсвеченными фрагментами — для совпадений в результатах поиска.</summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<TextSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<HighlightTextBlock, IReadOnlyList<TextSegment>?>(nameof(Segments));

    // Полупрозрачный жёлтый читается и на светлой, и на тёмной теме.
    private static readonly IBrush MatchBackground = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xC1, 0x07));

    static HighlightTextBlock()
    {
        SegmentsProperty.Changed.AddClassHandler<HighlightTextBlock>((control, _) => control.Rebuild());
    }

    public IReadOnlyList<TextSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        foreach (var segment in Segments ?? [])
        {
            inlines.Add(segment.IsMatch
                ? new Run(segment.Text) { FontWeight = FontWeight.Bold, Background = MatchBackground }
                : new Run(segment.Text));
        }

        Inlines = inlines;
    }
}
