using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Search;

namespace Urras.App;

// One thing Quick Launch can do: open a repository, switch branch, run a command
public sealed record QuickItem(string Title, string Kind, Func<Task> Run);

// Ctrl+P: type a few letters of a repository, branch or command and press Enter
public sealed class QuickLaunchWindow : Window
{
    private const double WINDOW_WIDTH = 620;
    private const double LIST_HEIGHT = 360;

    private static IBrush MutedBrush => ThemeBrushes.MutedText;

    private readonly IReadOnlyList<QuickItem> _items;
    private readonly TextBox _search;
    private readonly ListBox _list;

    public QuickLaunchWindow(IReadOnlyList<QuickItem> items)
    {
        _items = items;
        Title = "Quick Launch";
        Width = WINDOW_WIDTH;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _search = new TextBox { Watermark = "Repository, branch or command…" };
        _list = new ListBox { Height = LIST_HEIGHT, ItemTemplate = new FuncDataTemplate<QuickItem>(Row) };
        _search.TextChanged += (_, _) => Filter();
        _list.DoubleTapped += (_, _) => Choose();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => _search.Focus();

        StackPanel root = new StackPanel { Margin = new Thickness(10), Spacing = 8 };
        root.Children.Add(_search);
        root.Children.Add(_list);
        Content = root;
        Filter();
    }

    private static Control Row(QuickItem item, INameScope scope)
    {
        DockPanel row = new DockPanel();
        TextBlock kind = new TextBlock { Text = item.Kind, Foreground = MutedBrush, FontSize = 11,
                                         VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(kind, Dock.Right);
        row.Children.Add(kind);
        row.Children.Add(new TextBlock { Text = item.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        return row;
    }

    private void Filter()
    {
        List<QuickItem> matches = FuzzyMatch.Filter(_items, _search.Text ?? string.Empty, item => item.Title);
        _list.ItemsSource = matches;
        _list.SelectedIndex = matches.Count > 0 ? 0 : -1;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            {
                e.Handled = true;
                Choose();
                break;
            }
            case Key.Escape:
            {
                e.Handled = true;
                Close(null);
                break;
            }
            case Key.Down or Key.Up:
            {
                e.Handled = true;
                int count = _list.ItemCount;
                if (count > 0)
                    _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, count - 1);
                break;
            }
            default:
                break;
        }
    }

    private void Choose()
    {
        if (_list.SelectedItem is QuickItem item)
            Close(item);
    }
}
