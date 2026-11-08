using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Urras.App;

// A piece of text with its own colours; null brushes use the text block's own
public sealed record TextSegment(string Text, IBrush? Foreground = null, IBrush? Background = null);

// A text block whose text is built from coloured segments, for word highlights and whitespace markers in diffs
public class SegmentedTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<TextSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<SegmentedTextBlock, IReadOnlyList<TextSegment>?>(nameof(Segments));

    public IReadOnlyList<TextSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    // Styled like any other text block, so TextBlock.code and similar styles apply
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty)
            Rebuild();
    }

    private void Rebuild()
    {
        InlineCollection inlines = new InlineCollection();
        foreach (TextSegment segment in Segments ?? Array.Empty<TextSegment>())
        {
            Run run = new Run(segment.Text);
            if (segment.Foreground != null)
                run.Foreground = segment.Foreground;
            if (segment.Background != null)
                run.Background = segment.Background;
            inlines.Add(run);
        }

        Inlines = inlines;
    }
}
