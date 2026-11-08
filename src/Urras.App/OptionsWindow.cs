using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// Accounts change straight away; everything else is applied by Save, which closes with true
public sealed class OptionsWindow : Window
{
    public const string ACCOUNTS = "Accounts";
    public const string INTEGRATIONS = "Integrations";
    public const string GIT = "Git";
    public const string APPEARANCE = "Appearance";
    public const string PROMPTS = "Prompts";
    public const string ADVANCED = "Advanced";

    private const double WINDOW_WIDTH = 820;
    private const double WINDOW_HEIGHT = 620;
    private const double SIDEBAR_WIDTH = 170;

    private static readonly string[] Sections = { ACCOUNTS, INTEGRATIONS, GIT, APPEARANCE, PROMPTS, ADVANCED };
    private static readonly int[] TabSizes = { 1, 2, 3, 4, 5, 6, 8, 10, 12 };
    private static readonly int[] FetchIntervals = { 0, 5, 10, 15, 30, 60 };
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8B949E"));

    private readonly AppSettings _settings;
    private readonly AccountManager _accounts;
    private readonly Func<Window, string, Task> _addAccount;
    private readonly ContentControl _page = new ContentControl();
    private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();
    private readonly StackPanel _accountList = new StackPanel { Spacing = 10 };

    // Integrations
    private readonly ComboBox _editor;
    private readonly TextBox _editorPath = new TextBox { Watermark = "Path to the program" };
    private readonly TextBox _editorArguments = new TextBox();
    private readonly ComboBox _shell;
    private readonly TextBox _shellPath = new TextBox { Watermark = "Path to the program" };
    private readonly TextBox _shellArguments = new TextBox();

    // Git
    private readonly TextBox _userName = new TextBox();
    private readonly TextBox _userEmail = new TextBox();
    private readonly TextBox _defaultBranch = new TextBox { Watermark = "main" };
    private string? _loadedName;
    private string? _loadedEmail;
    private string? _loadedBranch;

    // Appearance
    private readonly RadioButton[] _themes;
    private readonly ComboBox _dateFormat;
    private readonly ComboBox _timeFormat;
    private readonly ComboBox _tabSize;

    // Prompts
    private readonly CheckBox _confirmRemoveRepository;
    private readonly CheckBox _confirmDiscard;
    private readonly CheckBox _confirmDropStash;
    private readonly CheckBox _confirmCheckout;
    private readonly CheckBox _confirmRemoveWorktree;
    private readonly CheckBox _lengthWarning;
    private readonly RadioButton[] _switching;

    // Advanced
    private readonly ComboBox _autoFetch;
    private readonly CheckBox _checkWhitespace;
    private readonly CheckBox _hideUntracked;

    public OptionsWindow(AppSettings settings, AccountManager accounts, Func<Window, string, Task> addAccount,
                         string section = ACCOUNTS)
    {
        _settings = settings;
        _accounts = accounts;
        _addAccount = addAccount;
        Title = "Options";
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _editor = Combo(SystemShell.EditorNames, settings.Editor.Name);
        _shell = Combo(SystemShell.ShellNames, settings.Shell.Name);
        _editorPath.Text = settings.Editor.Path;
        _editorArguments.Text = settings.Editor.Arguments;
        _shellPath.Text = settings.Shell.Path;
        _shellArguments.Text = settings.Shell.Arguments;

        _themes = new[] { Radio("System", "theme"), Radio("Dark", "theme"), Radio("Light", "theme") };
        _themes[(int)settings.Theme].IsChecked = true;
        _dateFormat = Combo(DisplayFormats.Dates.Select(DisplayFormats.Example).ToList(),
                            DisplayFormats.Example(settings.DateFormat));
        _timeFormat = Combo(DisplayFormats.Times.Select(DisplayFormats.Example).ToList(),
                            DisplayFormats.Example(settings.TimeFormat));
        _tabSize = Combo(TabSizes.Select(size => size == AppSettings.DEFAULT_TAB_SIZE ? size + " (default)"
                                                                                        : size.ToString()).ToList(),
                         null);
        _tabSize.SelectedIndex = Math.Max(0, Array.IndexOf(TabSizes, settings.TabSize));

        PromptSettings prompts = settings.Prompts;
        _confirmRemoveRepository = Check("Removing repositories from the list", prompts.RemovingRepositories);
        _confirmDiscard = Check("Discarding changes", prompts.DiscardingChanges);
        _confirmDropStash = Check("Dropping a stash", prompts.DroppingStashes);
        _confirmCheckout = Check("Checking out a commit", prompts.CheckingOutCommits);
        _confirmRemoveWorktree = Check("Removing worktrees", prompts.RemovingWorktrees);
        _lengthWarning = Check("Show the summary length counter", prompts.ShowCommitLengthWarning);
        _switching = new[]
        {
            Radio("Ask me where the changes should go", "switch"),
            Radio("Always bring my changes to the new branch", "switch"),
            Radio("Always stash them and leave them on the current branch", "switch")
        };
        _switching[(int)prompts.SwitchingWithChanges].IsChecked = true;

        _autoFetch = Combo(FetchIntervals.Select(minutes => minutes == 0 ? "Off" : "Every " + minutes + " minutes")
                                         .ToList(), null);
        _autoFetch.SelectedIndex = Math.Max(0, Array.IndexOf(FetchIntervals, settings.AutoFetchMinutes));
        _checkWhitespace = Check("Ask before committing whitespace problems (such as trailing spaces)",
                                 settings.CheckWhitespaceBeforeCommit);
        _hideUntracked = Check("Hide untracked files in Changes", settings.HideUntracked);

        _pages[ACCOUNTS] = AccountsPage();
        _pages[INTEGRATIONS] = IntegrationsPage();
        _pages[GIT] = GitPage();
        _pages[APPEARANCE] = AppearancePage();
        _pages[PROMPTS] = PromptsPage();
        _pages[ADVANCED] = AdvancedPage();

        ListBox sidebar = new ListBox { ItemsSource = Sections, Width = SIDEBAR_WIDTH };
        sidebar.SelectionChanged += (_, _) =>
        {
            if (sidebar.SelectedItem is string name)
                _page.Content = new ScrollViewer { Content = _pages[name], Padding = new Thickness(16, 0, 8, 0) };
        };

        Button save = new Button { Content = "Save", IsDefault = true, Classes = { "accent" } };
        Button cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += async (_, _) => await SaveAsync();
        cancel.Click += (_, _) => Close(false);
        StackPanel buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        DockPanel root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(buttons);
        root.Children.Add(sidebar);
        root.Children.Add(_page);
        Content = root;

        sidebar.SelectedItem = Sections.Contains(section) ? section : ACCOUNTS;
        Opened += async (_, _) => await LoadGitSettingsAsync();
    }

    // Pages

    private Control AccountsPage()
    {
        ShowAccounts();
        Button github = new Button { Content = "Sign in to GitHub…" };
        Button enterprise = new Button { Content = "Sign in to GitHub Enterprise…" };
        github.Click += async (_, _) => await AddAccountAsync(GitHubClient.GITHUB_API);
        enterprise.Click += async (_, _) => await AddAccountAsync(string.Empty);

        StackPanel signIn = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        signIn.Children.Add(github);
        signIn.Children.Add(enterprise);
        return Page(Heading("Accounts"), _accountList, signIn,
                    Note("Accounts let you add co-authors by @username, show profile pictures and clone your " +
                         "repositories. Tokens are kept in your system's credential store."));
    }

    private void ShowAccounts()
    {
        _accountList.Children.Clear();
        if (_accounts.Accounts.Count == 0)
            _accountList.Children.Add(Note("Not signed in."));

        foreach (Account account in _accounts.Accounts.ToList())
        {
            Button signOut = new Button { Content = "Sign out", VerticalAlignment = VerticalAlignment.Center };
            signOut.Click += async (_, _) =>
            {
                await _accounts.RemoveAsync(account);
                ShowAccounts();
            };

            StackPanel names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock
            {
                Text = account.Name ?? account.Username,
                FontWeight = FontWeight.SemiBold
            });
            names.Children.Add(new TextBlock { Text = "@" + account.Username + "  ·  " + account.Host,
                                               Foreground = MutedBrush });
            DockPanel row = new DockPanel();
            AvatarView avatar = new AvatarView
            {
                Url = account.AvatarUrl,
                PersonName = account.Name ?? account.Username,
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(avatar, Dock.Left);
            DockPanel.SetDock(signOut, Dock.Right);
            row.Children.Add(avatar);
            row.Children.Add(signOut);
            row.Children.Add(names);
            _accountList.Children.Add(row);
        }
    }

    private async Task AddAccountAsync(string api)
    {
        await _addAccount(this, api);
        ShowAccounts();
    }

    private Control IntegrationsPage()
    {
        StackPanel editorCustom = CustomProgram(_editorPath, _editorArguments);
        StackPanel shellCustom = CustomProgram(_shellPath, _shellArguments);
        editorCustom.IsVisible = _settings.Editor.IsCustom;
        shellCustom.IsVisible = _settings.Shell.IsCustom;
        _editor.SelectionChanged += (_, _) => editorCustom.IsVisible = _editor.SelectedItem is ProgramChoice.CUSTOM;
        _shell.SelectionChanged += (_, _) => shellCustom.IsVisible = _shell.SelectedItem is ProgramChoice.CUSTOM;

        return Page(Heading("External editor"), _editor, editorCustom, Heading("Shell"), _shell, shellCustom,
                    Note("Used by Open in editor and Open in terminal. In arguments, " + ProgramChoice.TARGET +
                         " stands for the folder or file; without it, the path goes at the end."));
    }

    private StackPanel CustomProgram(TextBox path, TextBox arguments)
    {
        Button choose = new Button { Content = "Choose…" };
        choose.Click += async (_, _) =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions { Title = "Choose the program", AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } chosen)
                path.Text = chosen;
        };

        DockPanel pathRow = new DockPanel();
        DockPanel.SetDock(choose, Dock.Right);
        choose.Margin = new Thickness(6, 0, 0, 0);
        pathRow.Children.Add(choose);
        pathRow.Children.Add(path);

        StackPanel panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Label("Path"));
        panel.Children.Add(pathRow);
        panel.Children.Add(Label("Arguments"));
        panel.Children.Add(arguments);
        return panel;
    }

    private Control GitPage()
    {
        Button useNoreply = new Button { Content = "Use my GitHub private address", IsVisible = false };
        Account? github = _accounts.Accounts.FirstOrDefault(account => account.Kind == AccountKind.GitHub);
        if (github != null)
        {
            string noreply = github.Id + "+" + github.Username + "@users.noreply.github.com";
            useNoreply.IsVisible = true;
            ToolTip.SetTip(useNoreply, noreply);
            useNoreply.Click += (_, _) => _userEmail.Text = noreply;
        }

        return Page(Heading("Author"), Label("Name"), _userName, Label("Email"), _userEmail, useNoreply,
                    Heading("Default branch for new repositories"), _defaultBranch,
                    Note("These go in your global Git configuration, so other Git tools use them too."));
    }

    private Control AppearancePage()
    {
        StackPanel themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (RadioButton theme in _themes)
            themes.Children.Add(theme);

        return Page(Heading("Theme"), themes,
                    Note("Urras's colours are tuned for the dark theme; the light one is still rough."),
                    Heading("Formatting"), Label("Date format"), _dateFormat, Label("Time format"), _timeFormat,
                    Heading("Diffs"), Label("Tab size"), _tabSize);
    }

    private Control PromptsPage()
    {
        StackPanel switching = new StackPanel { Spacing = 4 };
        foreach (RadioButton option in _switching)
            switching.Children.Add(option);

        return Page(Heading("Ask before…"), _confirmRemoveRepository, _confirmDiscard, _confirmDropStash,
                    _confirmCheckout, _confirmRemoveWorktree,
                    Heading("If I have changes and switch branches…"), switching,
                    Heading("Commit message"), _lengthWarning);
    }

    private Control AdvancedPage()
    {
        return Page(Heading("Background fetch"), _autoFetch,
                    Note("Fetches every repository in the list now and then, so ahead and behind counts stay " +
                         "current."),
                    Heading("Changes"), _checkWhitespace, _hideUntracked);
    }

    // Saving

    private async Task LoadGitSettingsAsync()
    {
        _loadedName = await GlobalConfig.GetAsync(GlobalConfig.USER_NAME);
        _loadedEmail = await GlobalConfig.GetAsync(GlobalConfig.USER_EMAIL);
        _loadedBranch = await GlobalConfig.GetAsync(GlobalConfig.DEFAULT_BRANCH);
        _userName.Text = _loadedName;
        _userEmail.Text = _loadedEmail;
        _defaultBranch.Text = _loadedBranch;
    }

    private async Task SaveAsync()
    {
        _settings.Editor = Program(_editor, _editorPath, _editorArguments);
        _settings.Shell = Program(_shell, _shellPath, _shellArguments);

        _settings.Theme = (ThemeChoice)Array.FindIndex(_themes, radio => radio.IsChecked == true);
        _settings.DateFormat = DisplayFormats.Dates[Math.Max(0, _dateFormat.SelectedIndex)];
        _settings.TimeFormat = DisplayFormats.Times[Math.Max(0, _timeFormat.SelectedIndex)];
        _settings.TabSize = TabSizes[Math.Max(0, _tabSize.SelectedIndex)];

        PromptSettings prompts = _settings.Prompts;
        prompts.RemovingRepositories = _confirmRemoveRepository.IsChecked == true;
        prompts.DiscardingChanges = _confirmDiscard.IsChecked == true;
        prompts.DroppingStashes = _confirmDropStash.IsChecked == true;
        prompts.CheckingOutCommits = _confirmCheckout.IsChecked == true;
        prompts.RemovingWorktrees = _confirmRemoveWorktree.IsChecked == true;
        prompts.ShowCommitLengthWarning = _lengthWarning.IsChecked == true;
        prompts.SwitchingWithChanges = (BranchSwitchChanges)Math.Max(0, Array.FindIndex(_switching,
                                                                         radio => radio.IsChecked == true));

        _settings.AutoFetchMinutes = FetchIntervals[Math.Max(0, _autoFetch.SelectedIndex)];
        _settings.CheckWhitespaceBeforeCommit = _checkWhitespace.IsChecked == true;
        _settings.HideUntracked = _hideUntracked.IsChecked == true;
        _settings.Save();

        // Only what changed is written to the global Git configuration
        await SaveGlobalAsync(GlobalConfig.USER_NAME, _loadedName, _userName.Text);
        await SaveGlobalAsync(GlobalConfig.USER_EMAIL, _loadedEmail, _userEmail.Text);
        await SaveGlobalAsync(GlobalConfig.DEFAULT_BRANCH, _loadedBranch, _defaultBranch.Text);
        Close(true);
    }

    private static async Task SaveGlobalAsync(string key, string? before, string? after)
    {
        if ((before ?? string.Empty) != (after?.Trim() ?? string.Empty))
            await GlobalConfig.SetAsync(key, after);
    }

    private static ProgramChoice Program(ComboBox box, TextBox path, TextBox arguments)
    {
        return new ProgramChoice
        {
            Name = box.SelectedItem as string ?? SystemShell.DEFAULT,
            Path = path.Text?.Trim() ?? string.Empty,
            Arguments = string.IsNullOrWhiteSpace(arguments.Text) ? ProgramChoice.TARGET : arguments.Text.Trim()
        };
    }

    // Building blocks

    private static StackPanel Page(params Control[] controls)
    {
        StackPanel page = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        foreach (Control control in controls)
            page.Children.Add(control);
        return page;
    }

    private static TextBlock Heading(string text)
    {
        return new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold,
                               Margin = new Thickness(0, 10, 0, 0) };
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock { Text = text, Foreground = MutedBrush };
    }

    private static TextBlock Note(string text)
    {
        return new TextBlock { Text = text, Foreground = MutedBrush, TextWrapping = TextWrapping.Wrap };
    }

    private static CheckBox Check(string text, bool value)
    {
        return new CheckBox { Content = text, IsChecked = value };
    }

    private static RadioButton Radio(string text, string group)
    {
        return new RadioButton { Content = text, GroupName = group };
    }

    private static ComboBox Combo(IReadOnlyList<string> items, string? selected)
    {
        int index = selected == null ? 0 : items.ToList().IndexOf(selected);
        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = Math.Max(0, index),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
