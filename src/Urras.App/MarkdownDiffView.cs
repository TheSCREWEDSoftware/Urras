using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Markdown;

namespace Urras.App;

// Markdown rendered as a page: the new version alone, or old and new side by side with changed blocks marked
public sealed class MarkdownDiffView : UserControl
{
    private const double BASE_FONT_SIZE = 14;
    private const double CODE_FONT_SIZE = 13;
    private const double LIST_INDENT = 18;

    private static readonly double[] HeadingSizes = { 26, 21, 18, 16, 15, 14 };
    private static readonly FontFamily MonoFont =
        new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace");
    private static readonly IBrush LinkBrush = Brush("#58A6FF");
    private static readonly IBrush MutedBrush = Brush("#8B949E");
    private static readonly IBrush CodeBackground = Brush("#1AFFFFFF");
    private static readonly IBrush QuoteBar = Brush("#3D444D");
    private static readonly IBrush RuleBrush = Brush("#3D444D");
    private static readonly IBrush AddedBackground = Brush("#2257AB5A");
    private static readonly IBrush RemovedBackground = Brush("#22E5534B");
    private static readonly IBrush AddedBar = Brush("#57AB5A");
    private static readonly IBrush RemovedBar = Brush("#E5534B");

    private readonly Grid _columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };

    public MarkdownDiffView()
    {
        Content = new ScrollViewer { Content = _columns, Padding = new Thickness(16, 8) };
    }

    // Either side may be null, for a new or deleted file
    public void Show(byte[]? before, byte[]? after)
    {
        _columns.Children.Clear();
        List<MarkdownBlock> oldBlocks = MarkdownDocument.Parse(Text(before));
        List<MarkdownBlock> newBlocks = MarkdownDocument.Parse(Text(after));
        (HashSet<int> removed, HashSet<int> added) = MarkdownDocument.Changes(oldBlocks, newBlocks);

        bool sideBySide = before != null && after != null && (removed.Count > 0 || added.Count > 0);
        if (!sideBySide)
        {
            Control page = Page(after != null ? newBlocks : oldBlocks, new HashSet<int>(), null, null);
            Grid.SetColumnSpan(page, 3);
            _columns.Children.Add(page);
            return;
        }

        Control left = Page(oldBlocks, removed, RemovedBackground, RemovedBar);
        Control right = Page(newBlocks, added, AddedBackground, AddedBar);
        Grid.SetColumn(right, 2);
        _columns.Children.Add(left);
        _columns.Children.Add(right);
    }

    public void Clear()
    {
        _columns.Children.Clear();
    }

    private static string Text(byte[]? bytes)
    {
        return bytes == null ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    private static Control Page(List<MarkdownBlock> blocks, HashSet<int> changed, IBrush? background, IBrush? bar)
    {
        StackPanel page = new StackPanel { Spacing = 8 };
        for (int i = 0; i < blocks.Count; ++i)
        {
            Control block = Block(blocks[i]);
            if (!changed.Contains(i))
            {
                page.Children.Add(block);
                continue;
            }

            page.Children.Add(new Border
            {
                Background = background,
                BorderBrush = bar,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 4),
                Child = block
            });
        }

        if (blocks.Count == 0)
            page.Children.Add(new TextBlock { Text = "(empty)", Foreground = MutedBrush });
        return page;
    }

    private static Control Block(MarkdownBlock block)
    {
        switch (block.Kind)
        {
            case BlockKind.Heading:
            {
                int level = Math.Clamp(block.Level, 1, HeadingSizes.Length);
                TextBlock heading = Paragraph(block.Spans, HeadingSizes[level - 1]);
                heading.FontWeight = FontWeight.SemiBold;
                heading.Margin = new Thickness(0, block.Level <= 2 ? 8 : 4, 0, 0);
                return heading;
            }
            case BlockKind.Code:
                return new Border
                {
                    Background = CodeBackground,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 8),
                    Child = new TextBlock { Text = block.Text, FontFamily = MonoFont, FontSize = CODE_FONT_SIZE }
                };
            case BlockKind.Quote:
            {
                TextBlock quote = Paragraph(block.Spans, BASE_FONT_SIZE);
                quote.Foreground = MutedBrush;
                return new Border
                {
                    BorderBrush = QuoteBar,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 2),
                    Child = quote
                };
            }
            case BlockKind.ListItem:
            {
                DockPanel item = new DockPanel { Margin = new Thickness(block.Level * LIST_INDENT, 0, 0, 0) };
                TextBlock marker = new TextBlock
                {
                    Text = block.Marker == "-" ? "•" : block.Marker,
                    Width = LIST_INDENT,
                    FontSize = BASE_FONT_SIZE
                };
                DockPanel.SetDock(marker, Dock.Left);
                item.Children.Add(marker);
                item.Children.Add(Paragraph(block.Spans, BASE_FONT_SIZE));
                return item;
            }
            case BlockKind.Table:
                return Table(block);
            case BlockKind.Rule:
                return new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 6) };
            default:
                return Paragraph(block.Spans, BASE_FONT_SIZE);
        }
    }

    private static Control Table(MarkdownBlock block)
    {
        int columns = block.Rows.Max(row => row.Count);
        Grid grid = new Grid();
        for (int column = 0; column < columns; ++column)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int row = 0; row < block.Rows.Count; ++row)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < block.Rows[row].Count; ++column)
            {
                TextBlock cell = Paragraph(block.Rows[row][column], BASE_FONT_SIZE);
                if (row == 0)
                    cell.FontWeight = FontWeight.SemiBold;
                Border border = new Border
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 4),
                    Child = cell
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
            }
        }

        return new Border
        {
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Child = grid,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    // Styled runs; links show their address on hover, images their alt text and address
    private static TextBlock Paragraph(List<MarkdownSpan> spans, double size)
    {
        TextBlock text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size };
        InlineCollection inlines = new InlineCollection();
        List<string> links = new List<string>();
        foreach (MarkdownSpan span in spans)
        {
            Run run = new Run(span.Style.HasFlag(SpanStyle.Image) ? "[image: " + span.Text + "]" : span.Text);
            if (span.Style.HasFlag(SpanStyle.Bold))
                run.FontWeight = FontWeight.Bold;
            if (span.Style.HasFlag(SpanStyle.Italic))
                run.FontStyle = FontStyle.Italic;
            if (span.Style.HasFlag(SpanStyle.Strike))
                run.TextDecorations = TextDecorations.Strikethrough;
            if (span.Style.HasFlag(SpanStyle.Code))
            {
                run.FontFamily = MonoFont;
                run.Background = CodeBackground;
            }

            if (span.Style.HasFlag(SpanStyle.Link) || span.Style.HasFlag(SpanStyle.Image))
            {
                run.Foreground = LinkBrush;
                if (span.Url != null)
                    links.Add(span.Text + ": " + span.Url);
            }

            inlines.Add(run);
        }

        text.Inlines = inlines;

        if (links.Count > 0)
            ToolTip.SetTip(text, string.Join("\n", links));
        return text;
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}
