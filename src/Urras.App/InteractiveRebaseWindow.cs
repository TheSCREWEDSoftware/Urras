using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// Plans an interactive rebase; closes with true when the plan in Steps should run
public sealed class InteractiveRebaseWindow : Window
{
    private const double WINDOW_WIDTH = 980;
    private const double WINDOW_HEIGHT = 620;
    private const double DRAG_DISTANCE = 6;
    private const string DRAG_FORMAT = "urras-rebase-step";

    private static readonly string[] ActionNames = { "Pick", "Reword", "Edit", "Squash", "Fixup", "Drop" };
    private static readonly IBrush[] ActionBrushes =
    {
        Brush("#57AB5A"), Brush("#E8B931"), Brush("#539BF5"), Brush("#B083F0"), Brush("#B083F0"), Brush("#E5534B")
    };
    private static readonly IBrush MutedBrush = Brush("#8B949E");

    private readonly ListBox _list;
    private readonly TextBlock _problem;
    private readonly CheckBox _backup;

    public InteractiveRebaseWindow(string branch, CommitInfo onto, IEnumerable<CommitInfo> commits, bool hasMerges)
    {
        Title = "Interactive rebase";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Shown newest first, like History; git wants oldest first, which Steps gives back
        Steps = new ObservableCollection<RebaseStep>(commits.Reverse().Select(commit => new RebaseStep(commit)));

        TextBlock header = new TextBlock
        {
            Text = $"Rebase {branch} onto {onto.ShortHash}  {onto.Subject}",
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4)
        };
        TextBlock help = new TextBlock
        {
            Text = "Newest at the top. Squash and Fixup join a commit into the one below it. Drag commits to " +
                   "reorder them, or use the buttons or Alt+Up and Alt+Down." +
                   (hasMerges ? " Merge commits in this range are flattened away by the rebase." : string.Empty),
            TextWrapping = TextWrapping.Wrap,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _list = new ListBox { ItemsSource = Steps, ItemTemplate = new FuncDataTemplate<RebaseStep>(Row) };
        DragDrop.SetAllowDrop(_list, true);
        _list.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DRAG_FORMAT)
                                                                               ? DragDropEffects.Move
                                                                               : DragDropEffects.None);
        _list.AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(e));
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down))
                return;
            e.Handled = true;
            Move(e.Key == Key.Up ? -1 : 1);
        };

        StackPanel side = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0), Width = 150 };
        side.Children.Add(SideButton("Move up", () => Move(-1)));
        side.Children.Add(SideButton("Move down", () => Move(1)));
        side.Children.Add(SideButton("Edit message…", EditMessageAsync));
        side.Children.Add(SideButton("Reset all to Pick", ResetAll));

        _problem = new TextBlock { Foreground = ActionBrushes[5], TextWrapping = TextWrapping.Wrap };
        _backup = new CheckBox { Content = "Create a backup tag first (urras-backup/…)", IsChecked = true };
        Button start = new Button { Content = "Start rebase", IsDefault = true };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        start.Click += (_, _) => Start();
        cancel.Click += (_, _) => Close(false);

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(start);
        buttons.Children.Add(cancel);
        DockPanel footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_backup);

        DockPanel root = new DockPanel { Margin = new Thickness(12) };
        foreach (Control top in new Control[] { header, help })
        {
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
        }

        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(_problem, Dock.Bottom);
        DockPanel.SetDock(side, Dock.Right);
        root.Children.Add(footer);
        root.Children.Add(_problem);
        root.Children.Add(side);
        root.Children.Add(_list);
        Content = root;
    }

    public ObservableCollection<RebaseStep> Steps { get; }
    public bool CreateBackup => _backup.IsChecked == true;

    // Oldest first, the order `git rebase -i` uses
    public List<RebaseStep> Plan => Steps.Reverse().ToList();

    private Control Row(RebaseStep step, INameScope scope)
    {
        ComboBox action = new ComboBox
        {
            ItemsSource = ActionNames,
            SelectedIndex = (int)step.Action,
            Width = 110,
            Foreground = ActionBrushes[(int)step.Action]
        };
        bool dropped = step.Action == RebaseAction.Drop;
        bool joined = step.JoinsPrevious;
        TextBlock subject = new TextBlock
        {
            Text = (joined ? "↳ " : string.Empty) + (step.Message?.Split('\n')[0] ?? step.Commit.Subject) +
                   (step.Message != null ? "  (new message)" : string.Empty),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextDecorations = dropped ? TextDecorations.Strikethrough : null,
            Opacity = dropped ? 0.5 : 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(joined ? 24 : 8, 0, 8, 0)
        };
        action.SelectionChanged += async (_, _) =>
        {
            RebaseAction chosen = (RebaseAction)Math.Max(0, action.SelectedIndex);
            if (chosen == step.Action)
                return;

            step.Action = chosen;
            if (chosen == RebaseAction.Reword && step.Message == null)
                await EditMessageAsync(step);
            Refresh(step);
        };

        Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,160,80,130"),
                               Background = Brushes.Transparent };
        Point? pressedAt = null;
        grid.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed)
                pressedAt = e.GetPosition(grid);
        };
        grid.PointerReleased += (_, _) => pressedAt = null;
        grid.PointerMoved += async (_, e) =>
        {
            if (pressedAt is not { } start || Distance(start, e.GetPosition(grid)) < DRAG_DISTANCE)
                return;

            pressedAt = null;
            DataObject data = new DataObject();
            data.Set(DRAG_FORMAT, Steps.IndexOf(step));
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        };
        AddCell(grid, action, 0);
        AddCell(grid, subject, 1);
        AddCell(grid, Muted(step.Commit.Author), 2);
        AddCell(grid, Muted(step.Commit.ShortHash), 3);
        AddCell(grid, Muted(step.Commit.DateText), 4);
        return grid;
    }

    // Dropping a row on another moves it to that row's place
    private void OnDrop(DragEventArgs e)
    {
        if (e.Data.Get(DRAG_FORMAT) is not int from || from < 0 || from >= Steps.Count)
            return;

        RebaseStep? target = (e.Source as StyledElement)?.DataContext as RebaseStep;
        int to = target == null ? Steps.Count - 1 : Steps.IndexOf(target);
        if (to < 0 || to == from)
            return;

        Steps.Move(from, to);
        _list.SelectedIndex = to;
        Validate();
    }

    private static double Distance(Point a, Point b)
    {
        return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    private static void AddCell(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static TextBlock Muted(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = MutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
    }

    private static Button SideButton(string text, Action action)
    {
        Button button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button SideButton(string text, Func<Task> action)
    {
        Button button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void Move(int direction)
    {
        int index = _list.SelectedIndex;
        int target = index + direction;
        if (index < 0 || target < 0 || target >= Steps.Count)
            return;

        Steps.Move(index, target);
        _list.SelectedIndex = target;
        Validate();
    }

    private async Task EditMessageAsync()
    {
        if (_list.SelectedItem is RebaseStep step)
        {
            await EditMessageAsync(step);
            Refresh(step);
        }
    }

    private async Task EditMessageAsync(RebaseStep step)
    {
        string? message = await Dialogs.MessageAsync(this, "Commit message", step.Message ?? step.Commit.Subject);
        if (message == null)
            return;

        step.Message = message;
        if (step.Action == RebaseAction.Pick)
            step.Action = RebaseAction.Reword;
    }

    private void ResetAll()
    {
        foreach (RebaseStep step in Steps)
        {
            step.Action = RebaseAction.Pick;
            step.Message = null;
        }

        Refresh(null);
    }

    // Rows are rebuilt so the action colour, strikethrough and indent follow the change
    private void Refresh(RebaseStep? select)
    {
        int index = select == null ? _list.SelectedIndex : Steps.IndexOf(select);
        _list.ItemsSource = null;
        _list.ItemsSource = Steps;
        _list.SelectedIndex = index;
        Validate();
    }

    private bool Validate()
    {
        string? problem = RebaseTodo.Validate(Plan);
        _problem.Text = problem ?? string.Empty;
        return problem == null;
    }

    private void Start()
    {
        if (Validate())
            Close(true);
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}
