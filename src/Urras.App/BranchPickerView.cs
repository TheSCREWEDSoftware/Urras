using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// The branch picker, like GitHub Desktop's: filter, default/recent/other branches with ages, pull requests, merge
public sealed class BranchPickerView : UserControl
{
    private const double PICKER_WIDTH = 460;
    private const double LIST_HEIGHT = 460;
    private const double HEADER_FONT_SIZE = 12;
    private const double SECTION_GAP = 10;
    private const double ROW_GAP = 2;

    private static IBrush MutedBrush => ThemeBrushes.MutedText;
    private static readonly Thickness RowPadding = new Thickness(8, 5);

    private readonly TextBox _filter = new TextBox { Watermark = "Filter" };
    private readonly StackPanel _rows = new StackPanel { Spacing = ROW_GAP };
    private readonly StackPanel _pullRows = new StackPanel { Spacing = ROW_GAP };
    private readonly TabControl _tabs = new TabControl();
    private readonly TabItem _pullTab = new TabItem { Header = "Pull requests" };
    private readonly Button _merge = new Button
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 0)
    };

    private IReadOnlyList<BranchInfo> _branches = Array.Empty<BranchInfo>();
    private IReadOnlyList<string> _recent = Array.Empty<string>();
    private IReadOnlyDictionary<string, DateTimeOffset> _dates = new Dictionary<string, DateTimeOffset>();
    private string? _defaultBranch;

    public BranchPickerView()
    {
        Button newBranch = new Button { Content = "New branch", Margin = new Thickness(8, 0, 0, 0) };
        newBranch.Click += (_, _) => NewBranchRequested?.Invoke();
        _merge.Click += (_, _) => MergeRequested?.Invoke();

        DockPanel top = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
        DockPanel.SetDock(newBranch, Dock.Right);
        top.Children.Add(newBranch);
        top.Children.Add(_filter);

        DockPanel branches = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_merge, Dock.Bottom);
        branches.Children.Add(top);
        branches.Children.Add(_merge);
        branches.Children.Add(new ScrollViewer { Content = _rows, MaxHeight = LIST_HEIGHT });

        _pullTab.Content = new ScrollViewer { Content = _pullRows, MaxHeight = LIST_HEIGHT };
        _tabs.Items.Add(new TabItem { Header = "Branches", Content = branches });
        _tabs.Items.Add(_pullTab);
        _tabs.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_tabs.SelectedItem, _pullTab))
                PullRequestsRequested?.Invoke();
        };

        _filter.TextChanged += (_, _) => Rebuild();
        // Enter takes the first branch shown, so typing a few letters and Enter switches
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            BranchInfo? first = BranchLists.Sections(_branches, _defaultBranch, _recent, _filter.Text)
                                           .SelectMany(section => section.Branches)
                                           .FirstOrDefault(branch => !branch.IsCurrent);
            if (first != null)
                BranchChosen?.Invoke(first);
            e.Handled = true;
        };

        Content = new Border { Width = PICKER_WIDTH, Padding = new Thickness(8), Child = _tabs };
    }

    public event Action<BranchInfo>? BranchChosen;
    public event Action? NewBranchRequested;
    public event Action? MergeRequested;
    public event Action? PullRequestsRequested;
    public event Action<PlatformPullRequest>? PullRequestChosen;

    public void Show(IReadOnlyList<BranchInfo> branches, string? defaultBranch, IReadOnlyList<string> recent,
                     IReadOnlyDictionary<string, DateTimeOffset> dates)
    {
        _branches = branches;
        _defaultBranch = defaultBranch;
        _recent = recent;
        _dates = dates;
        string current = branches.FirstOrDefault(branch => branch.IsCurrent)?.Name ?? "the current branch";
        _merge.Content = "Choose a branch to merge into " + current;
        _tabs.SelectedIndex = 0;
        _filter.Text = string.Empty;
        Rebuild();
        _filter.Focus();
    }

    // The pull requests, or a message saying why there are none to show
    public void ShowPullRequests(string heading, IReadOnlyList<PlatformPullRequest> pulls, string? message)
    {
        _pullRows.Children.Clear();
        _pullTab.Header = pulls.Count > 0 ? "Pull requests (" + pulls.Count + ")" : "Pull requests";
        _pullRows.Children.Add(Header(heading));
        if (message != null || pulls.Count == 0)
            _pullRows.Children.Add(Muted(message ?? "No open pull requests."));

        foreach (PlatformPullRequest pull in pulls)
        {
            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = pull.Title,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(Muted("#" + pull.Number + " by " + pull.Author +
                                    (pull.IsDraft ? " · Draft" : string.Empty)));
            Button row = RowButton(text);
            ToolTip.SetTip(row, "Check out " + pull.SourceBranch);
            row.Click += (_, _) => PullRequestChosen?.Invoke(pull);
            _pullRows.Children.Add(row);
        }
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        List<BranchSection> sections = BranchLists.Sections(_branches, _defaultBranch, _recent, _filter.Text);
        if (sections.Count == 0)
            _rows.Children.Add(Muted("No branches match."));

        DateTimeOffset now = DateTimeOffset.Now;
        foreach (BranchSection section in sections)
        {
            _rows.Children.Add(Header(section.Title));
            foreach (BranchInfo branch in section.Branches)
            {
                Grid content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                content.Children.Add(new TextBlock
                {
                    Text = branch.Name + (branch.IsCurrent ? "  (current)" : string.Empty),
                    FontWeight = branch.IsCurrent ? FontWeight.SemiBold : FontWeight.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (_dates.TryGetValue(branch.Name, out DateTimeOffset date))
                {
                    TextBlock when = Muted(RelativeTime.Describe(date, now));
                    when.Margin = new Thickness(12, 0, 0, 0);
                    Grid.SetColumn(when, 1);
                    content.Children.Add(when);
                }

                Button row = RowButton(content);
                row.IsEnabled = !branch.IsCurrent;
                row.Click += (_, _) => BranchChosen?.Invoke(branch);
                _rows.Children.Add(row);
            }
        }
    }

    private static Button RowButton(Control content)
    {
        return new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = RowPadding
        };
    }

    private static TextBlock Header(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = HEADER_FONT_SIZE,
            Margin = new Thickness(4, SECTION_GAP, 0, 2)
        };
    }

    private static TextBlock Muted(string text)
    {
        return new TextBlock { Text = text, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap };
    }
}
