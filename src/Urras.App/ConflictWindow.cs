using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// Resolves a conflicted file one conflict at a time: both sides next to each other, a choice per conflict, and
// the result below each one. Closes with true when the file should be saved.
public sealed class ConflictWindow : Window
{
    private const double WINDOW_WIDTH = 1100;
    private const double WINDOW_HEIGHT = 760;
    private const int CONTEXT_LINES = 3;

    private static readonly FontFamily MonoFont =
        new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace");
    private static readonly IBrush OursBrush = Brush("#26539BF5");
    private static readonly IBrush TheirsBrush = Brush("#26F0883E");
    private static readonly IBrush ResultBrush = Brush("#1A57AB5A");
    private static readonly IBrush UnresolvedBrush = Brush("#26E5534B");
    private static readonly IBrush MutedBrush = Brush("#8B949E");
    private static readonly IBrush FrameBrush = Brush("#33FFFFFF");

    private readonly ConflictFile _file;
    private readonly string _oursName;
    private readonly string _theirsName;
    private readonly string _oursShort;
    private readonly string _theirsShort;
    private readonly StackPanel _blocks = new StackPanel { Spacing = 10 };
    private readonly TextBlock _progress = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _save = new Button { Content = "Save and mark resolved", IsDefault = true };

    public ConflictWindow(string path, ConflictFile file, bool rebase)
    {
        _file = file;
        Title = "Resolve conflicts: " + path;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // In a rebase git's "ours" is the branch being rebased onto, and "theirs" is your commit
        _oursName = (rebase ? "Rebasing onto" : "Current branch") + Suffix(file.OursLabel);
        _theirsName = (rebase ? "Your commit" : "Incoming") + Suffix(file.TheirsLabel);
        _oursShort = rebase ? "base" : "current";
        _theirsShort = rebase ? "your commit" : "incoming";

        Button allOurs = new Button { Content = "Use " + (rebase ? "the base" : "current") + " for all" };
        Button allTheirs = new Button { Content = "Use " + (rebase ? "your commits" : "incoming") + " for all" };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        allOurs.Click += (_, _) => ChooseAll(ConflictChoice.Ours);
        allTheirs.Click += (_, _) => ChooseAll(ConflictChoice.Theirs);
        _save.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);

        StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(allOurs);
        left.Children.Add(allTheirs);
        left.Children.Add(_progress);
        StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(_save);
        right.Children.Add(cancel);
        DockPanel footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(right);
        footer.Children.Add(left);

        DockPanel root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _blocks });
        Content = root;
        Rebuild();
    }

    private static string Suffix(string label)
    {
        return label.Length == 0 ? string.Empty : " (" + label + ")";
    }

    private void ChooseAll(ConflictChoice choice)
    {
        _file.ChooseAll(choice);
        Rebuild();
    }

    private void Rebuild()
    {
        _blocks.Children.Clear();
        int total = _file.Conflicts.Count();
        int number = 0;
        for (int i = 0; i < _file.Blocks.Count; ++i)
        {
            ConflictBlock block = _file.Blocks[i];
            if (block.IsConflict)
                _blocks.Children.Add(ConflictView(block, ++number, total));
            else
                _blocks.Children.Add(CommonView(block.Common!, i == 0, i == _file.Blocks.Count - 1));
        }

        int resolved = _file.Conflicts.Count(block => block.Choice != ConflictChoice.Unresolved);
        _progress.Text = $"{resolved} of {total} conflicts resolved";
        _save.IsEnabled = _file.IsResolved;
    }

    // Lines around the conflicts; long stretches are shortened to a few lines next to each conflict
    private static Control CommonView(List<string> lines, bool first, bool last)
    {
        List<string> shown = lines;
        int before = first ? 0 : CONTEXT_LINES;
        int after = last ? 0 : CONTEXT_LINES;
        if (lines.Count > before + after + 1)
        {
            shown = lines.Take(before).ToList();
            shown.Add($"… {lines.Count - before - after} unchanged lines …");
            shown.AddRange(lines.Skip(lines.Count - after));
        }

        TextBlock text = Code(shown);
        text.Foreground = MutedBrush;
        return text;
    }

    private Control ConflictView(ConflictBlock block, int number, int total)
    {
        StackPanel choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        choices.Children.Add(new TextBlock
        {
            Text = $"Conflict {number} of {total}",
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        AddChoice(choices, block, ConflictChoice.Ours, "Use " + _oursShort);
        AddChoice(choices, block, ConflictChoice.Theirs, "Use " + _theirsShort);
        AddChoice(choices, block, ConflictChoice.OursThenTheirs, "Both, " + _oursShort + " first");
        AddChoice(choices, block, ConflictChoice.TheirsThenOurs, "Both, " + _theirsShort + " first");
        if (block.Choice != ConflictChoice.Unresolved)
            AddChoice(choices, block, ConflictChoice.Unresolved, "Undo");

        Grid sides = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        Control ours = Side(_oursName, block.Ours!, OursBrush, block.Choice is ConflictChoice.Ours
                                                                   or ConflictChoice.OursThenTheirs
                                                                   or ConflictChoice.TheirsThenOurs);
        Control theirs = Side(_theirsName, block.Theirs!, TheirsBrush, block.Choice is ConflictChoice.Theirs
                                                                           or ConflictChoice.OursThenTheirs
                                                                           or ConflictChoice.TheirsThenOurs);
        Grid.SetColumn(theirs, 2);
        sides.Children.Add(ours);
        sides.Children.Add(theirs);

        bool resolved = block.Choice != ConflictChoice.Unresolved;
        List<string> result = block.ResolvedLines.ToList();
        Control resultView = Side("Result", resolved ? result : new List<string> { "Choose which side to keep." },
                                  resolved ? ResultBrush : UnresolvedBrush, false);

        StackPanel panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(choices);
        panel.Children.Add(sides);
        panel.Children.Add(resultView);
        return new Border
        {
            BorderBrush = FrameBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = panel
        };
    }

    private void AddChoice(StackPanel panel, ConflictBlock block, ConflictChoice choice, string text)
    {
        Button button = new Button { Content = text, Padding = new Thickness(8, 2) };
        if (block.Choice == choice && choice != ConflictChoice.Unresolved)
            button.Classes.Add("accent");
        button.Click += (_, _) =>
        {
            block.Choice = choice;
            Rebuild();
        };
        panel.Children.Add(button);
    }

    private static Control Side(string title, List<string> lines, IBrush background, bool chosen)
    {
        StackPanel panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title + (chosen ? "  ✓" : string.Empty),
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2)
        });
        TextBlock code = Code(lines.Count == 0 ? new List<string> { "(nothing)" } : lines);
        panel.Children.Add(new Border { Background = background, Padding = new Thickness(6, 4), Child = code });
        return panel;
    }

    private static TextBlock Code(IEnumerable<string> lines)
    {
        return new TextBlock
        {
            Text = string.Join('\n', lines.Select(line => line.TrimEnd('\r').Replace("\t", "    "))),
            FontFamily = MonoFont,
            TextWrapping = TextWrapping.NoWrap
        };
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}
