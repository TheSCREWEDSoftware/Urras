using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// A commit summary with its code (`backticked` text, branch names in merges) in a monospaced box, like GitHub
public class CommitSubjectBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<SubjectPart>?> PartsProperty =
        AvaloniaProperty.Register<CommitSubjectBlock, IReadOnlyList<SubjectPart>?>(nameof(Parts));

    private static readonly FontFamily CodeFont = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.FromArgb(0x40, 0x6E, 0x76, 0x81));

    public IReadOnlyList<SubjectPart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    // Styled like any other text block
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PartsProperty)
            return;

        InlineCollection inlines = new InlineCollection();
        foreach (SubjectPart part in Parts ?? Array.Empty<SubjectPart>())
        {
            Run run = new Run(part.IsCode ? " " + part.Text + " " : part.Text);
            if (part.IsCode)
            {
                run.FontFamily = CodeFont;
                run.Background = CodeBackground;
            }

            inlines.Add(run);
        }

        Inlines = inlines;
    }
}
