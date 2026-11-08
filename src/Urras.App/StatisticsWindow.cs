using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// Who committed, when, and which files change most, for the current branch
public sealed class StatisticsWindow : Window
{
    private const double WINDOW_WIDTH = 900;
    private const double WINDOW_HEIGHT = 760;
    private const double BAR_WIDTH = 360;
    private const double COLUMN_HEIGHT = 90;
    private const int TOP_CONTRIBUTORS = 15;
    private const int HOUR_LABEL_STEP = 3;

    private static readonly string[] Periods = { "All time", "Last year", "Last 3 months", "Last month" };
    private static readonly string?[] Since = { null, "1 year ago", "3 months ago", "1 month ago" };
    private static readonly string[] Weekdays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
    private static readonly IBrush BarBrush = new SolidColorBrush(Color.Parse("#539BF5"));
    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.Parse("#57AB5A"));
    private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.Parse("#E5534B"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8B949E"));

    private readonly GitRepository _repository;
    private readonly StackPanel _content = new StackPanel { Spacing = 18 };
    private readonly ComboBox _period;

    public StatisticsWindow(GitRepository repository, string name)
    {
        _repository = repository;
        Title = "Statistics: " + name;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _period = new ComboBox { ItemsSource = Periods, SelectedIndex = 0, Width = 160 };
        _period.SelectionChanged += async (_, _) => await LoadAsync();
        DockPanel top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(_period, Dock.Right);
        top.Children.Add(_period);
        top.Children.Add(new TextBlock { Text = "Current branch, merges left out", Foreground = MutedBrush,
                                         VerticalAlignment = VerticalAlignment.Center });

        DockPanel root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = _content });
        Content = root;
        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = "Counting…", Foreground = MutedBrush });
        RepositoryStatistics stats = await _repository.GetStatisticsAsync(Since[Math.Max(0, _period.SelectedIndex)]);
        _content.Children.Clear();
        if (stats.Commits == 0)
        {
            _content.Children.Add(new TextBlock { Text = "No commits in this period." });
            return;
        }

        string span = stats.First!.Value.ToString("yyyy-MM-dd") + " to " + stats.Last!.Value.ToString("yyyy-MM-dd");
        _content.Children.Add(new TextBlock
        {
            Text = $"{stats.Commits:N0} commits by {stats.Contributors.Count} people, {span}.  " +
                   $"+{stats.Added:N0}  −{stats.Removed:N0} lines",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });

        _content.Children.Add(Section("Contributors", Bars(
            stats.Contributors.Take(TOP_CONTRIBUTORS).Select(person => (person.Name, person.Commits,
                                                          $"{person.Commits:N0} commits  +{person.Added:N0} " +
                                                          $"−{person.Removed:N0}")))));
        _content.Children.Add(Section("Commits per month",
                                      Columns(stats.ByMonth.Select(month => (month.Month[2..], month.Commits)))));
        Grid when = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,2*") };
        Control weekdays = Section("Day of the week", Columns(Weekdays.Select((day, i) => (day, stats.ByWeekday[i]))));
        IEnumerable<(string, int)> byHour = stats.ByHour.Select((count, hour) =>
            (hour % HOUR_LABEL_STEP == 0 ? hour.ToString() : string.Empty, count));
        Control hours = Section("Hour of the day", Columns(byHour));
        Grid.SetColumn(hours, 2);
        when.Children.Add(weekdays);
        when.Children.Add(hours);
        _content.Children.Add(when);
        _content.Children.Add(Section("Most changed files", Bars(
            stats.Files.Select(file => (file.Path, file.Commits,
                                        $"{file.Commits:N0} commits  +{file.Added:N0} −{file.Removed:N0}")))));
    }

    private static Control Section(string title, Control body)
    {
        StackPanel panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(body);
        return panel;
    }

    // Horizontal bars: a label, a bar sized against the largest value, and the numbers
    private static Control Bars(IEnumerable<(string Label, int Value, string Detail)> rows)
    {
        List<(string Label, int Value, string Detail)> items = rows.ToList();
        int max = Math.Max(1, items.Count == 0 ? 1 : items.Max(item => item.Value));
        StackPanel panel = new StackPanel { Spacing = 3 };
        foreach ((string label, int value, string detail) in items)
        {
            Grid row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,Auto,*") };
            TextBlock name = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis,
                                             VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(name, label);
            Border bar = new Border
            {
                Background = BarBrush,
                Width = Math.Max(2, BAR_WIDTH * value / max),
                Height = 14,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 0)
            };
            TextBlock numbers = new TextBlock { Text = detail, Foreground = MutedBrush, FontSize = 12,
                                                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(numbers, 2);
            row.Children.Add(name);
            row.Children.Add(bar);
            row.Children.Add(numbers);
            panel.Children.Add(row);
        }

        return panel;
    }

    // Vertical columns with a label under each and the count on hover
    private static Control Columns(IEnumerable<(string Label, int Value)> values)
    {
        List<(string Label, int Value)> items = values.ToList();
        int max = Math.Max(1, items.Count == 0 ? 1 : items.Max(item => item.Value));
        Grid grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
        for (int i = 0; i < items.Count; ++i)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Border column = new Border
            {
                Background = BarBrush,
                Height = Math.Max(1, COLUMN_HEIGHT * items[i].Value / max),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(1, 0),
                MinHeight = 1
            };
            Panel slot = new Panel { Height = COLUMN_HEIGHT };
            slot.Children.Add(column);
            ToolTip.SetTip(slot, items[i].Label + ": " + items[i].Value);
            TextBlock label = new TextBlock { Text = items[i].Label, FontSize = 10, Foreground = MutedBrush,
                                              HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(slot, i);
            Grid.SetColumn(label, i);
            Grid.SetRow(label, 1);
            grid.Children.Add(slot);
            grid.Children.Add(label);
        }

        return grid;
    }
}
