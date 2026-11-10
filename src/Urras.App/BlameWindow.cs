using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Who last changed each line of a file; double-click a line to see its commit
public sealed class BlameWindow : Window
{
    private const double WINDOW_WIDTH = 1100;
    private const double WINDOW_HEIGHT = 720;

    // Every other commit's lines are shaded, visible in dark and light themes alike
    private static IBrush ShadedBrush => ThemeBrushes.Get(ThemeColours.DIFF_EMPTY_SIDE);
    private static IBrush MutedBrush => ThemeBrushes.MutedText;
    private static readonly FontFamily MonoFont =
        new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace");

    public BlameWindow(string title, IReadOnlyList<BlameLine> lines)
    {
        Title = title;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;

        ListBox list = new ListBox { ItemsSource = lines, ItemTemplate = new FuncDataTemplate<BlameLine>(Row) };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is BlameLine { IsUncommitted: false } line)
                CommitRequested?.Invoke(line.Hash);
        };

        TextBlock help = new TextBlock
        {
            Text = "Double-click a line to show its commit in History.",
            Opacity = 0.6,
            Margin = new Thickness(10, 6)
        };
        DockPanel root = new DockPanel();
        DockPanel.SetDock(help, Dock.Bottom);
        root.Children.Add(help);
        root.Children.Add(list);
        Content = root;
    }

    public event Action<string>? CommitRequested;

    private static Control Row(BlameLine line, INameScope scope)
    {
        Grid grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,150,90,50,*"),
            Background = line.IsShaded ? ShadedBrush : null
        };

        Add(grid, 0, line.IsUncommitted ? "not committed" : line.ShortHash, MutedBrush, true);
        Add(grid, 1, line.IsUncommitted ? string.Empty : line.Author, MutedBrush, false);
        Add(grid, 2, line.IsUncommitted ? string.Empty : line.DateText, MutedBrush, false);
        TextBlock number = Add(grid, 3, line.Number.ToString(), MutedBrush, true);
        number.TextAlignment = TextAlignment.Right;
        number.Margin = new Thickness(0, 0, 10, 0);
        Add(grid, 4, line.Text.Replace("\t", "    "), null, true);

        ToolTip.SetTip(grid, line.IsUncommitted ? "Not committed yet" : line.Summary);
        return grid;
    }

    private static TextBlock Add(Grid grid, int column, string text, IBrush? foreground, bool mono)
    {
        TextBlock block = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (foreground != null)
            block.Foreground = foreground;
        if (mono)
            block.FontFamily = MonoFont;

        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        return block;
    }
}
