using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// The small icon in front of a changed file: an outlined square with a dot (modified), plus (new), minus
// (deleted), arrow (renamed or copied) or exclamation mark (conflicted). Its tooltip names the change.
public sealed class ChangeKindBadge : Control
{
    private const double SIZE = 14;
    private const double STROKE = 1.5;

    public static readonly StyledProperty<FileChange?> ChangeProperty =
        AvaloniaProperty.Register<ChangeKindBadge, FileChange?>(nameof(Change));

    static ChangeKindBadge()
    {
        AffectsRender<ChangeKindBadge>(ChangeProperty);
    }

    public ChangeKindBadge()
    {
        Width = SIZE;
        Height = SIZE;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public FileChange? Change
    {
        get => GetValue(ChangeProperty);
        set => SetValue(ChangeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChangeProperty)
            ToolTip.SetTip(this, Change?.KindDescription);
    }

    public override void Render(DrawingContext context)
    {
        FileChange? file = Change;
        if (file == null)
            return;

        IBrush brush = new SolidColorBrush(Color.Parse(ColourOf(file.Kind)));
        Pen pen = new Pen(brush, STROKE);
        double half = STROKE / 2;
        context.DrawRectangle(null, pen, new Rect(half, half, SIZE - STROKE, SIZE - STROKE), 2.5, 2.5);

        const double middle = SIZE / 2;
        const double arm = 3;
        switch (file.Kind)
        {
            case ChangeKind.Added or ChangeKind.Untracked:
            {
                context.DrawLine(pen, new Point(middle - arm, middle), new Point(middle + arm, middle));
                context.DrawLine(pen, new Point(middle, middle - arm), new Point(middle, middle + arm));
                break;
            }
            case ChangeKind.Deleted:
                context.DrawLine(pen, new Point(middle - arm, middle), new Point(middle + arm, middle));
                break;
            case ChangeKind.Renamed or ChangeKind.Copied:
            {
                context.DrawLine(pen, new Point(middle - arm, middle), new Point(middle + arm, middle));
                context.DrawLine(pen, new Point(middle + arm, middle), new Point(middle, middle - arm + 0.5));
                context.DrawLine(pen, new Point(middle + arm, middle), new Point(middle, middle + arm - 0.5));
                break;
            }
            case ChangeKind.Conflicted:
            {
                context.DrawLine(pen, new Point(middle, middle - arm), new Point(middle, middle + 0.5));
                context.DrawEllipse(brush, null, new Point(middle, middle + arm - 0.5), 0.9, 0.9);
                break;
            }
            default:
                context.DrawEllipse(brush, null, new Point(middle, middle), 2, 2);
                break;
        }
    }

    private static string ColourOf(ChangeKind kind)
    {
        return kind switch
        {
            ChangeKind.Added or ChangeKind.Untracked => "#3FB950",
            ChangeKind.Deleted => "#F85149",
            ChangeKind.Renamed or ChangeKind.Copied => "#58A6FF",
            ChangeKind.Conflicted => "#F0883E",
            _ => "#D29922"
        };
    }
}
