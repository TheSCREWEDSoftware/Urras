using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Urras.App;

// Writes the tree guides beside a repository row in plain text: | for a line going on, | - - - for a branch
public sealed class TreeGuides : Decorator
{
    private const double DEFAULT_FONT_SIZE = 14;
    private const double GAP = 3;
    private const int MIN_DASHES = 1;
    private const string PIPE = "|";
    private const string DASH = " -";

    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#A5ADB8"));
    // The app's own font, so the ASCII guides show on every system; widths are measured, not assumed
    private static readonly Typeface GuideFont = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private RepositoryNode? _node;

    // As big as the row's name, so the guides read as clearly as the text beside them
    private double _fontSize = DEFAULT_FONT_SIZE;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _node = DataContext as RepositoryNode;
        InvalidateVisual();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The guides reach into the rows' indentation, which moves without this control's own size changing
        InvalidateVisual();
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RepositoryNode? node = _node;
        TreeViewItem? item = this.FindAncestorOfType<TreeViewItem>();
        if (node?.Parent == null || item == null)
            return;

        _fontSize = this.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.FontSize ?? DEFAULT_FONT_SIZE;
        List<double> lines = TextLineMiddles();
        double nameY = lines[0];
        List<double> below = lines.Skip(1).ToList();

        // The items holding this node, its parent, its grandparent and so on
        List<TreeViewItem> items = new List<TreeViewItem> { item };
        for (TreeViewItem? above = item.FindAncestorOfType<TreeViewItem>(); above != null;
             above = above.FindAncestorOfType<TreeViewItem>())
            items.Add(above);

        // This row's own branch under the parent's arrow: |-- by the name, | below it while siblings follow
        if (ChevronMiddle(items.ElementAtOrDefault(1)) is double parentX)
        {
            Branch(context, parentX, OwnChevronLeft(item) - GAP, nameY);
            if (!node.IsLastChild)
                Pipes(context, parentX, below);
        }

        // Ancestors with more siblings below keep their | going past this row
        RepositoryNode? ancestor = node.Parent;
        for (int level = 2; ancestor?.Parent != null && level < items.Count; level++)
        {
            if (!ancestor.IsLastChild && ChevronMiddle(items[level]) is double x)
                Pipes(context, x, lines);
            ancestor = ancestor.Parent;
        }
    }

    // Halfway down each line of text in the row: the name, then the details
    private List<double> TextLineMiddles()
    {
        List<double> middles = this.GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.TranslatePoint(new Point(0, text.Bounds.Height / 2), this)?.Y)
            .OfType<double>().ToList();
        if (middles.Count == 0)
            middles.Add(Bounds.Height / 2);
        return middles;
    }

    // An item's expand arrow, which comes before its children's arrows
    private static ToggleButton? Chevron(TreeViewItem? item)
    {
        return item?.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault();
    }

    private double? ChevronMiddle(TreeViewItem? item)
    {
        return Chevron(item) is { } chevron ? Box(chevron).Center.X : null;
    }

    // Where the line across stops: at this row's arrow when it shows one, otherwise at the name
    private double OwnChevronLeft(TreeViewItem item)
    {
        return Chevron(item) is { IsEffectivelyVisible: true } chevron ? Box(chevron).Left : GAP;
    }

    private Rect Box(Visual visual)
    {
        Point corner = visual.TranslatePoint(default, this) ?? default;
        return new Rect(corner, visual.Bounds.Size);
    }

    // | - - - from under the parent's arrow, with spaced dashes up to where the row begins
    private void Branch(DrawingContext context, double x, double end, double y)
    {
        FormattedText pipe = Text(PIPE);
        double dashWidth = Text(DASH).Width;
        int dashes = Math.Max(MIN_DASHES, (int)Math.Floor((end - x - pipe.Width / 2) / dashWidth));
        Write(context, PIPE + string.Concat(Enumerable.Repeat(DASH, dashes)), x - pipe.Width / 2, y);
    }

    private void Pipes(DrawingContext context, double x, IEnumerable<double> lines)
    {
        double half = Text(PIPE).Width / 2;
        foreach (double y in lines)
            Write(context, PIPE, x - half, y);
    }

    private void Write(DrawingContext context, string text, double left, double middle)
    {
        FormattedText formatted = Text(text);
        context.DrawText(formatted, new Point(left, middle - formatted.Height / 2));
    }

    private FormattedText Text(string text)
    {
        return new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                 GuideFont, _fontSize, GuideBrush);
    }
}
