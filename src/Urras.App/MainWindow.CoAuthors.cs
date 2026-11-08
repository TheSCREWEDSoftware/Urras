using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Urras.Core.Accounts;
using Urras.Core.Git;

namespace Urras.App;

// Co-authors from the history, typed as Name <email>, or found as @username with an account
public partial class MainWindow
{
    // Waits for a pause in typing before searching a platform
    private const int TYPING_PAUSE_MILLISECONDS = 300;
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
            _ = AddTypedCoAuthorAsync();
        };
        CoAuthorBox.SelectionChanged += (_, _) =>
        {
            if (CoAuthorBox.SelectedItem is not string text)
                return;
            if (_platformMatches.TryGetValue(text, out CoAuthor? found))
                AddCoAuthor(found);
            else if (CoAuthor.Parse(text) is { } person)
                AddCoAuthor(person);
        };

        // Suggestions: people from the history, and with an account, @username searches on the platform
        CoAuthorBox.AsyncPopulator = SuggestCoAuthorsAsync;
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

    private readonly Dictionary<string, CoAuthor> _platformMatches = new Dictionary<string, CoAuthor>();

    private async Task<IEnumerable<object>> SuggestCoAuthorsAsync(string? text, CancellationToken cancellation)
    {
        string query = text?.Trim() ?? string.Empty;
        if (!query.StartsWith('@') || _accounts == null)
            return _knownPeople.Select(person => person.ToString())
                               .Where(person => person.Contains(query, StringComparison.OrdinalIgnoreCase))
                               .Cast<object>().ToList();

        try
        {
            // Waits for a pause in typing before asking the platform
            await Task.Delay(TYPING_PAUSE_MILLISECONDS, cancellation);
            List<CoAuthor> found = await _accounts.SearchCoAuthorsAsync(query);
            List<object> labels = new List<object>();
            foreach (CoAuthor person in found)
            {
                string label = person.Label + (person.Name != person.Username ? "  ·  " + person.Name : string.Empty);
                _platformMatches[label] = person;
                labels.Add(label);
            }

            return labels;
        }
        catch (Exception exception) when (exception is TaskCanceledException or PlatformException)
        {
            return Array.Empty<object>();
        }
    }

    private async Task AddTypedCoAuthorAsync()
    {
        string text = CoAuthorBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return;

        if (text.StartsWith('@'))
        {
            await AddPlatformCoAuthorAsync(text);
            return;
        }

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

    private async Task AddPlatformCoAuthorAsync(string login)
    {
        if (_accounts == null || _accounts.Accounts.Count == 0)
        {
            SetStatus("Add a GitHub account (Accounts…) to find co-authors by @username.", true);
            return;
        }

        try
        {
            CoAuthor? person = await _accounts.FindCoAuthorAsync(login);
            if (person == null)
                SetStatus("No user " + login + " on GitHub.", true);
            else
                AddCoAuthor(person);
        }
        catch (PlatformException exception)
        {
            SetStatus("Couldn't look up " + login + ": " + exception.Message, true);
        }
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
                Content = "×",
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
            content.Children.Add(new AvatarView
            {
                Email = person.Email,
                PersonName = person.Name,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
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
