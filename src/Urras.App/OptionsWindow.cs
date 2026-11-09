using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Urras.Core.Accounts;
using Urras.Core.Assistant;
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
    public const string ASSISTANT = "AI assistant";
    public const string ADVANCED = "Advanced";

    private const double WINDOW_WIDTH = 820;
    private const double WINDOW_HEIGHT = 620;
    private const double SIDEBAR_WIDTH = 170;

    private static readonly string[] Sections =
    {
        ACCOUNTS, INTEGRATIONS, GIT, APPEARANCE, PROMPTS, ASSISTANT, ADVANCED
    };
    private static readonly int[] TabSizes = { 1, 2, 3, 4, 5, 6, 8, 10, 12 };
    private static readonly int[] FetchIntervals = { 0, 5, 10, 15, 30, 60 };
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8B949E"));

    private readonly AppSettings _settings;
    private readonly AccountManager _accounts;
    private readonly Func<Window, AccountKind, Task> _addAccount;
    private readonly ContentControl _page = new ContentControl();
    // Space around each page, inside its scroll area (the scroll area's own padding cut off the end of long lines)
    private const double BADGE_RADIUS = 4;
    private const double BADGE_PADDING_X = 6;
    private const double BADGE_PADDING_Y = 1;
    private const double BADGE_FONT_SIZE = 11;

    private static readonly Thickness PagePadding = new Thickness(16, 0, 8, 0);

    private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();
    private readonly StackPanel _accountList = new StackPanel { Spacing = 10 };
    private readonly TextBox _githubAppId = new TextBox { Watermark = "GitHub OAuth app client id" };
    private readonly TextBox _gitlabAppId = new TextBox { Watermark = "GitLab application id" };

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

    // AI assistant
    private static readonly string[] AssistantKinds =
    {
        "Off", "OpenAI-compatible (OpenAI, OpenRouter, LM Studio, Ollama…)", "Anthropic", "Command-line agent"
    };
    private readonly ComboBox _assistantKind;
    private readonly TextBox _assistantEndpoint = new TextBox();
    private readonly TextBox _assistantModel = new TextBox { Watermark = "Model name, as the service lists it" };
    private readonly TextBox _assistantKey = new TextBox { PasswordChar = '•' };
    private readonly TextBox _assistantCommand = new TextBox { Watermark = "Such as: claude -p" };
    private bool _forgetKey;

    // Advanced
    private readonly ComboBox _autoFetch;
    private readonly CheckBox _checkWhitespace;
    private readonly CheckBox _hideUntracked;
    private readonly CheckBox _fullScrollBars;
    private readonly CheckBox _windowsInTaskbar;
    private readonly ComboBox _forkDefault;

    public OptionsWindow(AppSettings settings, AccountManager accounts, Func<Window, AccountKind, Task> addAccount,
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
            Radio("Always stash them and leave them on the current branch", "switch"),
            Radio("Keep them with each branch: put away when I leave, back when I return", "switch")
        };
        _switching[(int)prompts.SwitchingWithChanges].IsChecked = true;

        _autoFetch = Combo(FetchIntervals.Select(minutes => minutes == 0 ? "Off" : "Every " + minutes + " minutes")
                                         .ToList(), null);
        _autoFetch.SelectedIndex = Math.Max(0, Array.IndexOf(FetchIntervals, settings.AutoFetchMinutes));
        _checkWhitespace = Check("Ask before committing whitespace problems (such as trailing spaces)",
                                 settings.CheckWhitespaceBeforeCommit);
        _hideUntracked = Check("Hide untracked files in Changes", settings.HideUntracked);
        _forkDefault = Combo(new List<string>
        {
            "Ask for each fork", "Contribute to the parent project", "Use forks for my own purposes"
        }, null);
        _forkDefault.SelectedIndex = (int)settings.DefaultForkTarget;
        _fullScrollBars = Check("Always show full scroll bars, not only when the mouse is over them",
                                settings.AlwaysShowScrollBars);
        _windowsInTaskbar = Check("Show each Urras window in the taskbar, not only the main one",
                                  settings.ShowWindowsInTaskbar);

        _pages[ACCOUNTS] = AccountsPage();
        _pages[INTEGRATIONS] = IntegrationsPage();
        _pages[GIT] = GitPage();
        _pages[APPEARANCE] = AppearancePage();
        _pages[PROMPTS] = PromptsPage();
        _assistantKind = Combo(AssistantKinds, null);
        _assistantKind.SelectedIndex = (int)settings.Assistant.Kind;
        _assistantEndpoint.Text = settings.Assistant.Endpoint;
        _assistantModel.Text = settings.Assistant.Model;
        _assistantCommand.Text = settings.Assistant.Command;
        _pages[ASSISTANT] = AssistantPage();
        _pages[ADVANCED] = AdvancedPage();
        foreach (string page in _pages.Keys.ToList())
            _pages[page] = new ScrollViewer { Content = new Border { Padding = PagePadding, Child = _pages[page] } };

        ListBox sidebar = new ListBox { ItemsSource = Sections, Width = SIDEBAR_WIDTH };
        sidebar.SelectionChanged += (_, _) =>
        {
            // Each page keeps its own scroll area: a page can only sit in one, so visiting it again reuses it
            if (sidebar.SelectedItem is string name)
                _page.Content = _pages[name];
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
        WrapPanel signIn = new WrapPanel();
        foreach (AccountKind kind in Enum.GetValues<AccountKind>())
        {
            Button button = new Button { Content = "Sign in to " + Account.PlatformName(kind) + "…",
                                         Margin = new Thickness(0, 0, 8, 8) };
            button.Click += async (_, _) => await AddAccountAsync(kind);
            signIn.Children.Add(button);
        }

        _githubAppId.Text = _settings.GitHubClientId;
        _gitlabAppId.Text = _settings.GitLabClientId;
        return Page(Heading("Accounts"), _accountList, signIn,
                    Note("Accounts let you add co-authors by @username, show profile pictures, see pull requests " +
                         "and notifications, and clone your repositories. Tokens are kept in your system's " +
                         "credential store."),
                    Heading("Signing in through the browser"),
                    Note("GitHub signs in through the browser. GitLab, and GitHub Enterprise servers, do too once " +
                         "Urras is registered with them as an app, which takes two minutes; the ids go below. " +
                         "Other platforms use a pasted token."),
                    Links.ToPage("How to register Urras (docs/oauth.md)", Links.Document("docs/oauth.md")),
                    Label("GitHub client id"), _githubAppId, Label("GitLab application id"), _gitlabAppId,
                    Heading("Forks"), _forkDefault,
                    Note("Where pull requests and \"View on…\" go for forks that have no choice of their own. A " +
                         "fork's own choice, in its Repository settings, always comes first."));
    }

    // A coloured label with the platform's name, so accounts on different sites are told apart at a glance
    private static Border PlatformBadge(Account account)
    {
        AccountKind kind = account.Kind;
        Color colour = kind switch
        {
            AccountKind.GitHub or AccountKind.GitHubEnterprise => Color.Parse("#3D444D"),
            AccountKind.GitLab => Color.Parse("#C2410C"),
            AccountKind.Gitea => Color.Parse("#4D7C0F"),
            _ => Color.Parse("#1D4ED8")
        };
        return new Border
        {
            Background = new SolidColorBrush(colour),
            CornerRadius = new CornerRadius(BADGE_RADIUS),
            Padding = new Thickness(BADGE_PADDING_X, BADGE_PADDING_Y),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                // Codeberg runs Forgejo, a kind of Gitea, but is better known by its own name
                Text = kind == AccountKind.Gitea && account.WebHost == "codeberg.org" ? "Codeberg"
                                                                                       : Account.PlatformName(kind),
                FontSize = BADGE_FONT_SIZE,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White
            }
        };
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
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(PlatformBadge(account));
            title.Children.Add(new TextBlock
            {
                Text = account.Name ?? account.Username,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            names.Children.Add(title);
            names.Children.Add(new TextBlock { Text = "@" + account.Username + "  ·  " + account.WebHost,
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

    private async Task AddAccountAsync(AccountKind kind)
    {
        await _addAccount(this, kind);
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
                    Heading("Diffs"), Label("Tab size"), _tabSize, Heading("Scroll bars"), _fullScrollBars,
                    Heading("Taskbar"), _windowsInTaskbar);
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

    private Control AssistantPage()
    {
        StackPanel service = Page(Label("Address"), _assistantEndpoint, Label("Model"), _assistantModel,
                                  Label("Key"), _assistantKey);
        StackPanel agent = Page(Label("Command"), _assistantCommand,
                                Note("The request goes to the command's standard input and its answer is read " +
                                     "from standard output."));
        Button forget = new Button { Content = "Forget the saved key" };
        forget.Click += (_, _) =>
        {
            _forgetKey = true;
            _assistantKey.Text = string.Empty;
            forget.IsEnabled = false;
        };
        _assistantKey.Watermark = "Leave empty to keep the saved key (local services need none)";

        void Update()
        {
            AssistantKind kind = (AssistantKind)Math.Max(0, _assistantKind.SelectedIndex);
            service.IsVisible = kind is AssistantKind.OpenAiCompatible or AssistantKind.Anthropic;
            forget.IsVisible = service.IsVisible;
            agent.IsVisible = kind == AssistantKind.CommandLine;
            if (kind == AssistantKind.Anthropic && (_assistantEndpoint.Text ?? string.Empty).Contains("openai"))
                _assistantEndpoint.Text = AssistantSettings.ANTHROPIC_ENDPOINT;
            string endpoint = _assistantEndpoint.Text ?? string.Empty;
            if (kind == AssistantKind.OpenAiCompatible && endpoint.Contains("anthropic"))
                _assistantEndpoint.Text = AssistantSettings.OPENAI_ENDPOINT;
        }

        _assistantKind.SelectionChanged += (_, _) => Update();
        Update();
        return Page(Heading("AI assistant"), _assistantKind, service, forget, agent,
                    Note("Writes commit messages from the staged changes, explains commits and suggests conflict " +
                         "resolutions. Diffs are sent to the service you choose, so pick one you trust with this " +
                         "code. For Ollama, use the address http://localhost:11434/v1. The key is kept in your " +
                         "system's credential store."));
    }

    private Control AdvancedPage()
    {
        return Page(Heading("Background fetch"), _autoFetch,
                    Note("Fetches every repository in the list now and then, so ahead and behind counts stay " +
                         "current."),
                    Heading("Changes"), _checkWhitespace, _hideUntracked,
                    Heading("Files"),
                    Note("Urras keeps its settings, a backup of them for each of the last seven days, and crash.log " +
                         "(unexpected errors) in one folder."),
                    Links.ToFolder(Path.GetDirectoryName(AppSettings.DefaultPath) ?? AppSettings.DefaultPath,
                                   Path.GetDirectoryName(AppSettings.DefaultPath) ?? AppSettings.DefaultPath));
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
        _settings.GitHubClientId = _githubAppId.Text?.Trim() ?? string.Empty;
        _settings.GitLabClientId = _gitlabAppId.Text?.Trim() ?? string.Empty;
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

        _settings.Assistant.Kind = (AssistantKind)Math.Max(0, _assistantKind.SelectedIndex);
        _settings.Assistant.Endpoint = _assistantEndpoint.Text?.Trim() ?? string.Empty;
        _settings.Assistant.Model = _assistantModel.Text?.Trim() ?? string.Empty;
        _settings.Assistant.Command = _assistantCommand.Text?.Trim() ?? string.Empty;
        if (_forgetKey)
            await _accounts.DeleteSecretAsync(AssistantSettings.KEY_NAME);
        if (!string.IsNullOrWhiteSpace(_assistantKey.Text))
            await _accounts.SaveSecretAsync(AssistantSettings.KEY_NAME, _assistantKey.Text.Trim());

        _settings.AutoFetchMinutes = FetchIntervals[Math.Max(0, _autoFetch.SelectedIndex)];
        _settings.CheckWhitespaceBeforeCommit = _checkWhitespace.IsChecked == true;
        _settings.HideUntracked = _hideUntracked.IsChecked == true;
        _settings.AlwaysShowScrollBars = _fullScrollBars.IsChecked == true;
        _settings.ShowWindowsInTaskbar = _windowsInTaskbar.IsChecked == true;
        _settings.DefaultForkTarget = (ForkTarget)Math.Max(0, _forkDefault.SelectedIndex);
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
