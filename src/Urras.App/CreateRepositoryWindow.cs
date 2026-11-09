using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Urras.Core.Accounts;
using Urras.Core.Git;

namespace Urras.App;

// Create a new repository, like GitHub Desktop: name, description, where, README, .gitignore and licence
public sealed class CreateRepositoryWindow : Window
{
    private const double WINDOW_WIDTH = 520;
    private const string NONE = "None";

    private static IBrush MutedBrush => ThemeBrushes.MutedText;

    private readonly GitHubClient _templates;
    private readonly TextBox _name = new TextBox { Watermark = "repository-name" };
    private readonly TextBox _description = new TextBox();
    private readonly TextBox _parent = new TextBox();
    private readonly TextBlock _where = new TextBlock { Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _readme = new CheckBox { Content = "Initialize this repository with a README" };
    private readonly ComboBox _gitIgnore = Combo();
    private readonly ComboBox _license = Combo();
    private readonly TextBlock _problem = new TextBlock { Foreground = Brushes.IndianRed,
                                                          TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _create = new Button { Content = "Create repository", IsDefault = true,
                                                   Classes = { "accent" } };
    private List<(string Key, string Name)> _licenses = new List<(string Key, string Name)>();

    public CreateRepositoryWindow(HttpClient http, string parentFolder)
    {
        _templates = new GitHubClient(http, null);
        Title = "Create a new repository";
        Width = WINDOW_WIDTH;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _parent.Text = parentFolder;

        Button choose = new Button { Content = "Choose…", Margin = new Thickness(6, 0, 0, 0) };
        choose.Click += async (_, _) => await ChooseParentAsync();
        DockPanel pathRow = new DockPanel();
        DockPanel.SetDock(choose, Dock.Right);
        pathRow.Children.Add(choose);
        pathRow.Children.Add(_parent);

        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        _create.Click += async (_, _) => await CreateAsync();
        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(_create);
        buttons.Children.Add(cancel);

        StackPanel root = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        foreach (Control control in new Control[]
                 {
                     Label("Name"), _name, Label("Description"), _description, Label("Local path"), pathRow, _where,
                     _readme, Label("Git ignore"), _gitIgnore, Label("License"), _license,
                     new TextBlock { Text = "The .gitignore and licence lists come from GitHub.",
                                     Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap },
                     _problem, buttons
                 })
            root.Children.Add(control);
        Content = root;

        _name.TextChanged += (_, _) => Update();
        _parent.TextChanged += (_, _) => Update();
        Update();
        Opened += async (_, _) =>
        {
            _name.Focus();
            await LoadTemplatesAsync();
        };
    }

    private string Folder => Path.Combine(_parent.Text?.Trim() ?? string.Empty, _name.Text?.Trim() ?? string.Empty);

    // "The repository will be created at F:\Projects\Game"
    private void Update()
    {
        bool ready = !string.IsNullOrWhiteSpace(_name.Text) && !string.IsNullOrWhiteSpace(_parent.Text);
        _create.IsEnabled = ready;
        _where.Text = ready ? "The repository will be created at " + Folder : string.Empty;
    }

    private async Task ChooseParentAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Choose where the repository goes", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } chosen)
            _parent.Text = chosen;
    }

    private async Task LoadTemplatesAsync()
    {
        try
        {
            List<string> ignores = await _templates.GetGitIgnoreNamesAsync();
            _gitIgnore.ItemsSource = new[] { NONE }.Concat(ignores).ToList();
            _gitIgnore.SelectedIndex = 0;

            _licenses = await _templates.GetLicensesAsync();
            _license.ItemsSource = new[] { NONE }.Concat(_licenses.Select(license => license.Name)).ToList();
            _license.SelectedIndex = 0;
        }
        catch (PlatformException exception)
        {
            ShowProblem("Couldn't load GitHub's templates (" + exception.Message + "). The repository can still be " +
                        "created without them.");
        }
    }

    private async Task CreateAsync()
    {
        string name = _name.Text?.Trim() ?? string.Empty;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowProblem("A repository name can't contain characters such as \\ / : * ? \" < > |.");
            return;
        }

        if (Directory.Exists(Folder) && Directory.EnumerateFileSystemEntries(Folder).Any())
        {
            ShowProblem(Folder + " already exists and isn't empty. Add it with Add local repository instead.");
            return;
        }

        _create.IsEnabled = false;
        try
        {
            string? gitIgnore = _gitIgnore.SelectedIndex > 0 && _gitIgnore.SelectedItem is string ignore
                ? await _templates.GetGitIgnoreAsync(ignore)
                : null;
            string? license = null;
            if (_license.SelectedIndex > 0 && _license.SelectedIndex - 1 < _licenses.Count)
            {
                string template = await _templates.GetLicenseAsync(_licenses[_license.SelectedIndex - 1].Key);
                string owner = await GlobalConfig.GetAsync(GlobalConfig.USER_NAME) ?? string.Empty;
                license = RepositoryCreator.FillLicense(template, owner, DateTime.Now.Year);
            }

            Close(new NewRepository(Folder, name, _description.Text?.Trim() ?? string.Empty,
                                    _readme.IsChecked == true, gitIgnore, license));
        }
        catch (PlatformException exception)
        {
            ShowProblem("Couldn't get the template from GitHub: " + exception.Message);
            _create.IsEnabled = true;
        }
    }

    private void ShowProblem(string text)
    {
        _problem.Text = text;
        _problem.IsVisible = true;
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock { Text = text, Foreground = MutedBrush, Margin = new Thickness(0, 4, 0, 0) };
    }

    private static ComboBox Combo()
    {
        return new ComboBox
        {
            ItemsSource = new[] { NONE },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
