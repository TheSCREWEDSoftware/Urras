using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// Marks beside the diff where lines were added, removed or changed; hovering names the lines, a click goes there
public sealed class DiffOverview : Control
{
    // At least this tall, so a single changed line in a long file can still be seen and clicked
    private const double MIN_MARK_HEIGHT = 3;
    private const double MARK_OPACITY = 0.8;
    // Row height in the list; a diff shorter than the list puts each mark beside its row, not spread to the bottom
    private const double DEFAULT_ROW_HEIGHT = 16;
    // The file and hunk headers a diff starts with, which are neither added nor removed
    private const int HEADER_ROWS = 6;

    private enum Kind
    {
        Added,
        Removed,
        Changed
    }

    private sealed record Mark(int First, int Last, Kind Kind, string Lines);

    private IReadOnlyList<DiffRow> _rows = Array.Empty<DiffRow>();
    private List<Mark> _marks = new List<Mark>();

    public DiffOverview()
    {
        // A transparent background makes the whole column take the pointer, not just the marks
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double RowHeight { get; set; } = DEFAULT_ROW_HEIGHT;

    // The row a clicked mark starts at
    public event Action<int>? MarkClicked;

    public void Show(IReadOnlyList<DiffRow>? rows)
    {
        _rows = rows ?? Array.Empty<DiffRow>();
        _marks = Marks(_rows);
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    // Rows of the same kind next to each other make one mark; a new or deleted file, all one change, makes none
    private static List<Mark> Marks(IReadOnlyList<DiffRow> rows)
    {
        List<Mark> marks = new List<Mark>();
        int added = rows.Count(row => row.IsAdded);
        int removed = rows.Count(row => row.IsRemoved);
        if ((added == 0 || removed == 0) && added + removed >= rows.Count - HEADER_ROWS)
            return marks;

        int index = 0;
        while (index < rows.Count)
        {
            Kind? kind = KindOf(rows[index]);
            int end = index + 1;
            while (end < rows.Count && KindOf(rows[end]) == kind)
                ++end;
            if (kind is { } found)
                marks.Add(new Mark(index, end - 1, found, Lines(rows, index, end - 1, found)));
            index = end;
        }

        return marks;
    }

    private static Kind? KindOf(DiffRow row)
    {
        return row.IsAdded && row.IsRemoved ? Kind.Changed
             : row.IsAdded ? Kind.Added
             : row.IsRemoved ? Kind.Removed
                             : null;
    }

    // "Lines 12–18 added" or "Line 12 removed (old file)", from the line numbers the rows show
    private static string Lines(IReadOnlyList<DiffRow> rows, int first, int last, Kind kind)
    {
        bool old = kind == Kind.Removed;
        List<string> numbers = Enumerable.Range(first, last - first + 1)
                                         .Select(index => old ? rows[index].OldNumber : rows[index].NewNumber)
                                         .Where(number => number.Length > 0).ToList();
        string what = kind switch
        {
            Kind.Added => "added",
            Kind.Removed => "removed (old file)",
            _ => "changed"
        };
        if (numbers.Count == 0)
            return what;
        return numbers.Count == 1 || numbers[0] == numbers[^1]
            ? $"Line {numbers[0]} {what}"
            : $"Lines {numbers[0]}–{numbers[^1]} {what}";
    }

    private double EffectiveRowHeight => _rows.Count == 0 ? 0 : Math.Min(Bounds.Height / _rows.Count, RowHeight);

    private (double Top, double Bottom) Span(Mark mark)
    {
        double top = Math.Floor(mark.First * EffectiveRowHeight);
        return (top, Math.Max(top + MIN_MARK_HEIGHT, Math.Ceiling((mark.Last + 1) * EffectiveRowHeight)));
    }

    private Mark? MarkAt(double y)
    {
        return _marks.FirstOrDefault(mark =>
        {
            (double top, double bottom) = Span(mark);
            return y >= top - 1 && y <= bottom + 1;
        });
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ToolTip.SetTip(this, MarkAt(e.GetPosition(this).Y)?.Lines);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (MarkAt(e.GetPosition(this).Y) is { } mark)
            MarkClicked?.Invoke(mark.First);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_marks.Count == 0 || Bounds.Height <= 0)
            return;

        using (context.PushOpacity(MARK_OPACITY))
        {
            foreach (Mark mark in _marks)
            {
                string key = mark.Kind switch
                {
                    Kind.Added => ThemeColours.ADDED,
                    Kind.Removed => ThemeColours.REMOVED,
                    _ => ThemeColours.ATTENTION
                };
                (double top, double bottom) = Span(mark);
                context.FillRectangle(ThemeBrushes.Get(key), new Rect(0, top, Bounds.Width, bottom - top),
                                      (float)(Bounds.Width / 2));
            }
        }
    }
}
