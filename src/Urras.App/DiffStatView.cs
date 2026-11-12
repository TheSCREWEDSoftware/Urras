using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// "+406 −553" and five squares in green and red, as GitHub shows how much a change adds and removes
public sealed class DiffStatView : StackPanel
{
    private const double SQUARE_SIZE = 9;
    private const double SQUARE_GAP = 2;

    private readonly TextBlock _added = new TextBlock { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _removed = new TextBlock { FontWeight = FontWeight.SemiBold };
    private readonly StackPanel _squares = new StackPanel { Orientation = Orientation.Horizontal, Spacing = SQUARE_GAP,
                                                            VerticalAlignment = VerticalAlignment.Center };

    public DiffStatView()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(8, 0);
        IsVisible = false;
        Children.Add(_added);
        Children.Add(_removed);
        Children.Add(_squares);
    }

    // Counts the diff git printed; nothing is shown for a diff without added or removed lines
    public void Show(string? diff)
    {
        (int added, int removed) = DiffStats.Count(diff);
        IsVisible = added + removed > 0;
        if (!IsVisible)
            return;

        _added.Text = "+" + added.ToString("N0");
        _added.Foreground = ThemeBrushes.Get(ThemeColours.ADDED);
        _removed.Text = "−" + removed.ToString("N0");
        _removed.Foreground = ThemeBrushes.Get(ThemeColours.REMOVED);
        ToolTip.SetTip(this, $"{added:N0} lines added, {removed:N0} removed");

        (int green, int red) = DiffStats.Squares(added, removed);
        _squares.Children.Clear();
        for (int square = 0; square < DiffStats.SQUARES; ++square)
        {
            string key = square < green ? ThemeColours.ADDED
                       : square < green + red ? ThemeColours.REMOVED
                                              : ThemeColours.BORDER;
            _squares.Children.Add(new Border
            {
                Width = SQUARE_SIZE,
                Height = SQUARE_SIZE,
                CornerRadius = new CornerRadius(2),
                Background = ThemeBrushes.Get(key)
            });
        }
    }

    public void Clear()
    {
        IsVisible = false;
    }
}
