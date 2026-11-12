using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Urras.Core.Settings;

namespace Urras.App;

// Marks over the diff's scroll bar where lines were added (green), removed (red) or changed (both), like VS Code
public sealed class DiffOverview : Control
{
    // At least this tall, so a single changed line in a long file can still be seen
    private const double MIN_MARK_HEIGHT = 2;
    private const double MARK_OPACITY = 0.85;

    // Row height in the list; a diff shorter than the list puts each mark beside its row, not spread to the bottom
    private const double DEFAULT_ROW_HEIGHT = 16;

    private IReadOnlyList<DiffRow> _rows = Array.Empty<DiffRow>();

    public double RowHeight { get; set; } = DEFAULT_ROW_HEIGHT;

    public void Show(IReadOnlyList<DiffRow>? rows)
    {
        _rows = rows ?? Array.Empty<DiffRow>();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        int count = _rows.Count;
        if (count == 0 || Bounds.Height <= 0)
            return;

        IBrush added = ThemeBrushes.Get(ThemeColours.ADDED);
        IBrush removed = ThemeBrushes.Get(ThemeColours.REMOVED);
        IBrush changed = ThemeBrushes.Get(ThemeColours.ATTENTION);
        double rowHeight = Math.Min(Bounds.Height / count, RowHeight);
        double height = Math.Max(MIN_MARK_HEIGHT, rowHeight);
        using (context.PushOpacity(MARK_OPACITY))
        {
            for (int index = 0; index < count; ++index)
            {
                DiffRow row = _rows[index];
                IBrush? brush = row.IsAdded && row.IsRemoved ? changed
                              : row.IsAdded ? added
                              : row.IsRemoved ? removed
                                              : null;
                if (brush != null)
                    context.FillRectangle(brush, new Rect(0, index * rowHeight, Bounds.Width, height));
            }
        }
    }
}
