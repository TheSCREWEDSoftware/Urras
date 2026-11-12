using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Draws one row of the commit graph: the lanes passing through, the lines to parents, and the commit's dot
public sealed class GraphView : Control
{
    public const double LANE_WIDTH = 14;
    // With many branches the lanes close up so the summaries keep their room
    public const double NARROW_LANE_WIDTH = 10;
    public const int NARROW_FROM_LANES = 10;
    // The ring round each commit, which the author's picture sits inside
    public const double NODE_RADIUS = 9;
    public const double AVATAR_SIZE = 14;
    private const double LINE_THICKNESS = 2;
    // Lanes that only pass a row are drawn faint, so the lines that touch its commit stand out
    private const double PASSING_OPACITY = 0.4;

    // One colour per lane, repeating; bright enough to read on the dark list
    private static readonly IBrush[] LaneBrushes =
    {
        Brush("#E8B931"), Brush("#E5534B"), Brush("#539BF5"), Brush("#57AB5A"), Brush("#B083F0"),
        Brush("#F0883E"), Brush("#39C5CF"), Brush("#E275AD")
    };


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

    // Lane spacing for the whole list, set from its widest row so every row's lanes line up
    public static double LaneWidth { get; set; } = LANE_WIDTH;

    public static IBrush LaneBrush(int lane)
    {
        return LaneBrushes[lane % LaneBrushes.Length];
    }

    // Spacing for a history whose widest row uses this many lanes
    public static double LaneWidthFor(int lanes)
    {
        return lanes >= NARROW_FROM_LANES ? NARROW_LANE_WIDTH : LANE_WIDTH;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Room for the ring round the last lane's commit as well as the lane itself
        return Row == null ? default : new Size(Math.Max(Row.Width * LaneWidth, X(Row.Lane) + NODE_RADIUS) +
                                                LaneWidth / 2, 0);
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
                if (line.FromLane == row.Lane)
                {
                    context.DrawLine(pen, from, to);
                    continue;
                }

                using (context.PushOpacity(PASSING_OPACITY))
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

        // A ring in the lane's colour, filled with the panel colour; the author's picture is laid over it
        Point centre = new Point(X(row.Lane), height / 2);
        Pen ring = new Pen(LaneBrush(row.Lane), LINE_THICKNESS);
        if (row.IsMerge)
            ring.DashStyle = new DashStyle(new double[] { 2, 1.5 }, 0);
        context.DrawEllipse(ThemeBrushes.Get(ThemeColours.SURFACE), ring, centre, NODE_RADIUS, NODE_RADIUS);
    }

    public static double X(int lane)
    {
        return LaneWidth / 2 + lane * LaneWidth;
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}
