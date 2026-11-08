using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Git;

namespace Urras.App;

// Co-authors on the next commit, picked from people in the history or typed as Name <email>.
// When accounts are added, @username will look people up on the connected platform.
public partial class MainWindow
{
    private static readonly IBrush ChipBrush = new SolidColorBrush(Color.Parse("#1F6FEB"));

    private readonly List<CoAuthor> _coAuthors = new List<CoAuthor>();
    private List<CoAuthor> _knownPeople = new List<CoAuthor>();
    private string? _knownPeopleRepository;

    private void InitializeCoAuthors()
    {
        CoAuthorsToggle.Click += async (_, _) => await ShowCoAuthorsAsync(CoAuthorsToggle.IsChecked == true);
        CoAuthorBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            e.Handled = true;
            AddTypedCoAuthor();
        };
        CoAuthorBox.SelectionChanged += (_, _) =>
        {
            if (CoAuthorBox.SelectedItem is string text && CoAuthor.Parse(text) is { } person)
                AddCoAuthor(person);
        };
    }

    private async Task ShowCoAuthorsAsync(bool show)
    {
        CoAuthorsToggle.IsChecked = show;
        CoAuthorsPanel.IsVisible = show;
        if (!show || _repository == null)
            return;

        if (_knownPeopleRepository != _repository.Path)
        {
            _knownPeopleRepository = _repository.Path;
            _knownPeople = await _repository.GetKnownPeopleAsync();
            CoAuthorBox.ItemsSource = _knownPeople.Select(person => person.ToString()).ToList();
        }

        CoAuthorBox.Focus();
    }

    private void AddTypedCoAuthor()
    {
        string text = CoAuthorBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return;

        // A full "Name <email>", or the one known person whose name or email matches what was typed
        CoAuthor? person = CoAuthor.Parse(text);
        if (person == null)
        {
            List<CoAuthor> matches = _knownPeople.Where(known =>
                known.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                known.Email.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            person = matches.Count == 1 ? matches[0] : null;
        }

        if (person == null)
        {
            SetStatus("Type a co-author as Name <email>, or pick someone from the list.", true);
            return;
        }

        AddCoAuthor(person);
    }

    private void AddCoAuthor(CoAuthor person)
    {
        if (!_coAuthors.Any(existing => string.Equals(existing.Email, person.Email,
                                                      StringComparison.OrdinalIgnoreCase)))
            _coAuthors.Add(person);

        CoAuthorBox.Text = string.Empty;
        CoAuthorBox.SelectedItem = null;
        ShowCoAuthorChips();
    }

    // Replaces the co-authors, such as when amending a commit that already has some
    private void SetCoAuthors(IEnumerable<CoAuthor> people)
    {
        _coAuthors.Clear();
        _coAuthors.AddRange(people);
        ShowCoAuthorChips();
        if (_coAuthors.Count > 0)
            _ = ShowCoAuthorsAsync(true);
    }

    private void ShowCoAuthorChips()
    {
        CoAuthorChips.Children.Clear();
        foreach (CoAuthor person in _coAuthors)
        {
            Button remove = new Button
            {
                Content = "✕",
                Padding = new Thickness(4, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            remove.Click += (_, _) =>
            {
                _coAuthors.Remove(person);
                ShowCoAuthorChips();
            };

            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = person.Label,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(remove);
            Border chip = new Border
            {
                Background = ChipBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 2, 1),
                Margin = new Thickness(0, 2, 6, 2),
                Child = content
            };
            ToolTip.SetTip(chip, person.ToString());
            CoAuthorChips.Children.Add(chip);
        }

        CoAuthorsToggle.Content = _coAuthors.Count == 0 ? "Co-authors" : "Co-authors (" + _coAuthors.Count + ")";
    }
}
