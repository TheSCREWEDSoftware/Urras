using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Urras.Core.Markdown;

namespace Urras.App;

// Markdown, HTML or a CSV table as a page: the new version alone, or both side by side with changes marked
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

    // Characters of a changed block's text shown when hovering its mark
    private const int SNIPPET_LENGTH = 60;

    private static string Snippet(Control block)
    {
        string text = string.Join(" ", block.GetVisualDescendants().OfType<TextBlock>()
                                            .Select(textBlock => textBlock.Inlines?.Text ?? textBlock.Text)
                                            .Where(part => !string.IsNullOrWhiteSpace(part)));
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > SNIPPET_LENGTH ? text[..SNIPPET_LENGTH] + "…" : text.Length > 0 ? text : "a picture";
    }

    // Space kept above the first change when the preview scrolls to it
    private const double JUMP_MARGIN = 60;

    // Pictures wider than this are shrunk to fit
    private const double MAX_IMAGE_WIDTH = 900;

    // Loads a picture the page refers to; newSide is false for the old version of the file
    public delegate Task<byte[]?> ImageLoader(string source, bool newSide);

    private readonly Grid _columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
    private readonly TextBlock _note = new TextBlock
    {
        Text = "Text and structure only: no styles, scripts or layout.",
        Foreground = MutedBrush,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _browser = new Button { Content = "Open in browser", Classes = { "subtle" } };
    private readonly Button _fullScreen = new Button { Content = "Full screen", Classes = { "subtle" } };
    private byte[]? _before;
    private byte[]? _after;
    private string _path = string.Empty;
    private ImageLoader? _loader;
    private bool _newSide;
    private readonly ScrollViewer _scroll = new ScrollViewer { Padding = new Thickness(16, 8) };
    private readonly PreviewMarks _marks = new PreviewMarks { Width = 8, Margin = new Thickness(4, 0, 4, 0) };
    // Changed blocks, and whether each was added (new side) or removed, for the marks and the first change
    private readonly List<(Control Block, bool Added)> _changes = new List<(Control, bool)>();

    // Showing a file scrolls to its first change; set from Options
    public bool JumpToFirstChange { get; set; } = true;
    private int _generation;

    public MarkdownDiffView() : this(true)
    {
    }

    // A full-screen copy has no button to go full screen again
    public MarkdownDiffView(bool canGoFullScreen)
    {
        ToolTip.SetTip(_browser, "Shows this version of the page in your browser, from a temporary copy");
        ToolTip.SetTip(_fullScreen, "Shows the preview in a window of its own (Esc closes it)");
        _browser.Click += (_, _) => OpenInBrowser();
        _fullScreen.Click += (_, _) => OpenFullScreen();
        _fullScreen.IsVisible = canGoFullScreen;

        DockPanel toolbar = new DockPanel { Margin = new Thickness(16, 6, 16, 0) };
        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(_browser);
        buttons.Children.Add(_fullScreen);
        DockPanel.SetDock(buttons, Dock.Right);
        toolbar.Children.Add(buttons);
        toolbar.Children.Add(_note);

        DockPanel root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        _scroll.Content = _columns;
        // The marks are a column beside the page: hover names the change, a click scrolls to it
        Grid body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_marks, 1);
        body.Children.Add(_scroll);
        body.Children.Add(_marks);
        root.Children.Add(body);
        _marks.MarkClicked += offset => _scroll.Offset = new Vector(0, Math.Max(0, offset - JUMP_MARGIN));
        Content = root;
    }

    // Either side may be null, for a new or deleted file; the path says how to read it, the loader finds pictures
    public void Show(byte[]? before, byte[]? after, string path = "README.md", ImageLoader? loader = null)
    {
        _before = before;
        _after = after;
        _path = path;
        _loader = loader;
        ++_generation;
        _columns.Children.Clear();
        _changes.Clear();
        _marks.Show(Array.Empty<PreviewMarks.Mark>());
        PreviewKind kind = PreviewFiles.KindOf(path);
        bool html = kind == PreviewKind.Html;
        _note.IsVisible = html;
        _browser.IsVisible = html && (after ?? before) != null;

        List<MarkdownBlock> oldBlocks = PreviewFiles.Parse(kind, path, Text(before));
        List<MarkdownBlock> newBlocks = PreviewFiles.Parse(kind, path, Text(after));
        (HashSet<int> removed, HashSet<int> added) = MarkdownDocument.Changes(oldBlocks, newBlocks);

        bool sideBySide = before != null && after != null && (removed.Count > 0 || added.Count > 0);
        if (!sideBySide)
        {
            _newSide = after != null;
            Control page = Page(after != null ? newBlocks : oldBlocks, new HashSet<int>(), null, null);
            Grid.SetColumnSpan(page, 3);
            _columns.Children.Add(page);
            AfterLayout();
            return;
        }

        _newSide = false;
        Control left = Page(oldBlocks, removed, RemovedBackground, RemovedBar);
        _newSide = true;
        Control right = Page(newBlocks, added, AddedBackground, AddedBar);
        Grid.SetColumn(right, 2);
        _columns.Children.Add(left);
        _columns.Children.Add(right);
        AfterLayout();
    }

    // Once laid out: marks where the changes are, and the view scrolled to the first one
    private void AfterLayout()
    {
        int generation = _generation;
        _scroll.Offset = default;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (generation != _generation || _columns.Bounds.Height <= 0)
                return;
            double total = _columns.Bounds.Height;
            List<PreviewMarks.Mark> marks = new List<PreviewMarks.Mark>();
            double first = double.MaxValue;
            foreach ((Control block, bool added) in _changes)
            {
                if (block.TranslatePoint(new Point(0, 0), _columns) is not { } top)
                    continue;
                marks.Add(new PreviewMarks.Mark(top.Y / total, (top.Y + block.Bounds.Height) / total, added, top.Y,
                                                (added ? "Added: " : "Removed: ") + Snippet(block)));
                first = Math.Min(first, top.Y);
            }

            _marks.Show(marks);
            // A few lines of what comes before stay in view above the change
            if (JumpToFirstChange && first < double.MaxValue)
                _scroll.Offset = new Vector(0, Math.Max(0, first - JUMP_MARGIN));
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OpenInBrowser()
    {
        byte[]? contents = _after ?? _before;
        if (contents == null)
            return;
        // A copy in its own folder, as files next to the page (styles, pictures) aren't there anyway
        string folder = Path.Combine(Path.GetTempPath(), "Urras", "preview");
        Directory.CreateDirectory(folder);
        string copy = Path.Combine(folder, Path.GetFileName(_path));
        File.WriteAllBytes(copy, contents);
        SystemShell.OpenFile(copy);
    }

    private void OpenFullScreen()
    {
        MarkdownDiffView view = new MarkdownDiffView(false);
        Window window = new Window
        {
            Title = "Preview: " + Path.GetFileName(_path),
            WindowState = WindowState.Maximized,
            Content = view
        };
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
                window.Close();
        };
        view.Show(_before, _after, _path, _loader);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    public void Clear()
    {
        ++_generation;
        _columns.Children.Clear();
    }

    private static string Text(byte[]? bytes)
    {
        return bytes == null ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    private Control Page(List<MarkdownBlock> blocks, HashSet<int> changed, IBrush? background, IBrush? bar)
    {
        StackPanel page = new StackPanel { Spacing = 8 };
        for (int i = 0; i < blocks.Count; ++i)
        {
            // Rows of a CSV table go in one grid, so their columns line up, with changed rows marked
            if (blocks[i].Marker == DelimitedTable.ROW_MARKER)
            {
                int end = i;
                while (end < blocks.Count && blocks[end].Marker == DelimitedTable.ROW_MARKER)
                    ++end;
                page.Children.Add(Rows(blocks, i, end, changed, background));
                i = end - 1;
                continue;
            }

            Control block = Block(blocks[i]);
            if (!changed.Contains(i))
            {
                page.Children.Add(block);
                continue;
            }

            Border changedBlock = new Border
            {
                Background = background,
                BorderBrush = bar,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(8, 4),
                Child = block
            };
            _changes.Add((changedBlock, _newSide));
            page.Children.Add(changedBlock);
        }

        if (blocks.Count == 0)
            page.Children.Add(new TextBlock { Text = "(empty)", Foreground = MutedBrush });
        return page;
    }

    private Control Block(MarkdownBlock block)
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

    private Control Rows(List<MarkdownBlock> blocks, int start, int end, HashSet<int> changed,
                                IBrush? background)
    {
        MarkdownBlock table = new MarkdownBlock
        {
            Kind = BlockKind.Table,
            Rows = blocks.Skip(start).Take(end - start).SelectMany(block => block.Rows).ToList()
        };
        HashSet<int> marked = changed.Where(index => index >= start && index < end).Select(index => index - start)
                                     .ToHashSet();
        return Table(table, marked, background, blocks[start].Level == 0);
    }

    private Control Table(MarkdownBlock block)
    {
        return Table(block, new HashSet<int>(), null, true);
    }

    private Control Table(MarkdownBlock block, HashSet<int> marked, IBrush? background, bool header)
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
                if (row == 0 && header)
                    cell.FontWeight = FontWeight.SemiBold;
                Border border = new Border
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 4),
                    Background = marked.Contains(row) ? background : null,
                    Child = cell
                };
                if (column == 0 && marked.Contains(row))
                    _changes.Add((border, _newSide));
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
    private TextBlock Paragraph(List<MarkdownSpan> spans, double size)
    {
        TextBlock text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size };
        InlineCollection inlines = new InlineCollection();
        List<string> links = new List<string>();
        foreach (MarkdownSpan span in spans)
        {
            if (span.Style.HasFlag(SpanStyle.Image) && _loader != null && !string.IsNullOrEmpty(span.Url))
            {
                inlines.Add(new InlineUIContainer(Picture(span)));
                continue;
            }

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

    // The picture once it has loaded; until then, or if it can't be shown (such as an SVG), its description
    private Control Picture(MarkdownSpan span)
    {
        Border holder = new Border
        {
            Child = new TextBlock { Text = "[image: " + span.Text + "]", Foreground = LinkBrush }
        };
        ToolTip.SetTip(holder, span.Text + ": " + span.Url);
        int generation = _generation;
        bool newSide = _newSide;
        ImageLoader loader = _loader!;
        _ = LoadPictureAsync();
        return holder;

        async Task LoadPictureAsync()
        {
            byte[]? bytes;
            try
            {
                bytes = await loader(span.Url!, newSide);
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or
                                                  TaskCanceledException or UnauthorizedAccessException)
            {
                return;
            }

            if (bytes == null || generation != _generation)
                return;
            try
            {
                using MemoryStream stream = new MemoryStream(bytes);
                Avalonia.Media.Imaging.Bitmap bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
                holder.Child = new Image
                {
                    Source = bitmap,
                    Width = Math.Min(span.Width ?? bitmap.Size.Width, MAX_IMAGE_WIDTH),
                    Stretch = Stretch.Uniform
                };
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                                  NotSupportedException or IOException)
            {
                // Not a picture Avalonia can draw; the description stays
            }
        }
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}

// A column beside the preview marking added (green) and removed (red) blocks; hovering names one, a click goes there
public sealed class PreviewMarks : Control
{
    private const double MIN_MARK_HEIGHT = 3;
    private const double MARK_OPACITY = 0.8;

    // Where a block is, as fractions of the page, its offset in the page, and what hovering it says
    public sealed record Mark(double Top, double Bottom, bool Added, double Offset, string Label);

    private IReadOnlyList<Mark> _marks = Array.Empty<Mark>();

    public PreviewMarks()
    {
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
    }

    // The page offset of a clicked mark's block
    public event Action<double>? MarkClicked;

    public void Show(IReadOnlyList<Mark> marks)
    {
        _marks = marks;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    private (double Top, double Bottom) Span(Mark mark)
    {
        double top = Math.Floor(mark.Top * Bounds.Height);
        return (top, Math.Max(top + MIN_MARK_HEIGHT, Math.Ceiling(mark.Bottom * Bounds.Height)));
    }

    private Mark? MarkAt(double y)
    {
        return _marks.FirstOrDefault(mark =>
        {
            (double top, double bottom) = Span(mark);
            return y >= top - 1 && y <= bottom + 1;
        });
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ToolTip.SetTip(this, MarkAt(e.GetPosition(this).Y)?.Label);
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (MarkAt(e.GetPosition(this).Y) is { } mark)
            MarkClicked?.Invoke(mark.Offset);
    }

    public override void Render(DrawingContext context)
    {
        // A transparent background makes the whole column take the pointer, not just the marks
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        IBrush added = ThemeBrushes.Get(Urras.Core.Settings.ThemeColours.ADDED);
        IBrush removed = ThemeBrushes.Get(Urras.Core.Settings.ThemeColours.REMOVED);
        using (context.PushOpacity(MARK_OPACITY))
        {
            foreach (Mark mark in _marks)
            {
                // The old page's changes on the left half, the new page's on the right, as the pages sit
                (double top, double bottom) = Span(mark);
                double half = Bounds.Width / 2;
                context.FillRectangle(mark.Added ? added : removed,
                                      new Rect(mark.Added ? half : 0, top, half, bottom - top), (float)(half / 2));
            }
        }
    }
}
