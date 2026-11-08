using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// The coloured letter in front of a changed file (M, A, D, R…), explained in its tooltip
public sealed class ChangeKindBadge : Border
{
    public static readonly StyledProperty<FileChange?> ChangeProperty =
        AvaloniaProperty.Register<ChangeKindBadge, FileChange?>(nameof(Change));

    private readonly TextBlock _letter = new TextBlock
    {
        FontFamily = new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace"),
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    public ChangeKindBadge()
    {
        Width = 16;
        Height = 16;
        CornerRadius = new CornerRadius(3);
        VerticalAlignment = VerticalAlignment.Center;
        Child = _letter;
    }

    public FileChange? Change
    {
        get => GetValue(ChangeProperty);
        set => SetValue(ChangeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ChangeProperty)
            return;

        FileChange? file = Change;
        _letter.Text = file?.KindLetter ?? string.Empty;
        ToolTip.SetTip(this, file?.KindDescription);
        Color colour = Color.Parse(ColourOf(file?.Kind));
        _letter.Foreground = new SolidColorBrush(colour);
        Background = new SolidColorBrush(Color.FromArgb(0x33, colour.R, colour.G, colour.B));
    }

    private static string ColourOf(ChangeKind? kind)
    {
        return kind switch
        {
            ChangeKind.Added or ChangeKind.Untracked => "#57AB5A",
            ChangeKind.Deleted => "#E5534B",
            ChangeKind.Renamed or ChangeKind.Copied => "#539BF5",
            ChangeKind.Conflicted => "#F0883E",
            _ => "#E8B931"
        };
    }
}
