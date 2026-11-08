using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// Draws one row of the commit graph: the lanes passing through, the lines to parents, and the commit's dot
public sealed class GraphView : Control
{
    public const double LANE_WIDTH = 14;
    private const double DOT_RADIUS = 4;
    private const double LINE_THICKNESS = 2;

    // One colour per lane, repeating; bright enough to read on the dark list
    private static readonly IBrush[] LaneBrushes =
    {
        Brush("#E8B931"), Brush("#E5534B"), Brush("#539BF5"), Brush("#57AB5A"), Brush("#B083F0"),
        Brush("#F0883E"), Brush("#39C5CF"), Brush("#E275AD")
    };

    private static readonly IBrush MergeFill = Brush("#1E1E1E");

    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<GraphView, GraphRow?>(nameof(Row));

    static GraphView()
    {
        AffectsRender<GraphView>(RowProperty);
        AffectsMeasure<GraphView>(RowProperty);
    }

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    public static IBrush LaneBrush(int lane)
    {
        return LaneBrushes[lane % LaneBrushes.Length];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return Row == null ? default : new Size(Row.Width * LANE_WIDTH + LANE_WIDTH / 2, 0);
    }

    public override void Render(DrawingContext context)
    {
        GraphRow? row = Row;
        if (row == null)
            return;

        double height = Bounds.Height;
        foreach (GraphLine line in row.Lines)
        {
            Point from = new Point(X(line.FromLane), line.FromY * height);
            Point to = new Point(X(line.ToLane), line.ToY * height);
            Pen pen = new Pen(LaneBrush(line.Colour), LINE_THICKNESS);
            if (line.FromLane == line.ToLane)
            {
                context.DrawLine(pen, from, to);
                continue;
            }

            // Lanes that change sideways bend smoothly, vertical at both ends
            StreamGeometry curve = new StreamGeometry();
            using (StreamGeometryContext path = curve.Open())
            {
                double middle = (from.Y + to.Y) / 2;
                path.BeginFigure(from, false);
                path.CubicBezierTo(new Point(from.X, middle), new Point(to.X, middle), to);
                path.EndFigure(false);
            }

            context.DrawGeometry(null, pen, curve);
        }

        // Merge commits get a hollow dot, like most Git clients
        Point centre = new Point(X(row.Lane), height / 2);
        IBrush brush = LaneBrush(row.Lane);
        if (row.IsMerge)
            context.DrawEllipse(MergeFill, new Pen(brush, LINE_THICKNESS), centre, DOT_RADIUS, DOT_RADIUS);
        else
            context.DrawEllipse(brush, null, centre, DOT_RADIUS, DOT_RADIUS);
    }

    private static double X(int lane)
    {
        return LANE_WIDTH / 2 + lane * LANE_WIDTH;
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}
