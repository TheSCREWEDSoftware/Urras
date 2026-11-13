using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private static IBrush MutedBrush => ThemeBrushes.MutedText;

    private readonly AppSettings _settings;
    private readonly AccountManager _accounts;
    private readonly Func<Window, AccountKind, Task> _addAccount;
    private readonly ContentControl _page = new ContentControl();
    private const double BADGE_RADIUS = 4;
    private const double BADGE_PADDING_X = 6;
    private const double BADGE_PADDING_Y = 1;
    private const double BADGE_FONT_SIZE = 11;
    // A site's own icon beside the account's name
    private const double SITE_ICON_SIZE = 18;
    private const double BUTTON_ICON_SIZE = 16;

    // Space around each page, inside its scroll area (the scroll area's own padding cut off the end of long lines)
    private static readonly Thickness PagePadding = new Thickness(16, 0, 8, 0);

    private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();
    private readonly SettingTracker _tracker = new SettingTracker();

    // Set once the window may close without asking about unsaved changes
    private bool _closeAllowed;
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
    // Commit signing: off, with an SSH key, or with a GPG key
    private static readonly string[] SigningChoices = { "Off", "With an SSH key", "With a GPG key" };
    private readonly ComboBox _signing = new ComboBox
    {
        ItemsSource = SigningChoices,
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBox _signingKey = new TextBox { Watermark = "~/.ssh/id_ed25519.pub, or a GPG key id" };
    private string? _loadedSigning;
    private string? _loadedFormat;
    private string? _loadedKey;
    private readonly CheckBox _checkUpdates;
    private readonly ComboBox _closeButton;
    private readonly CheckBox _confirmExit;
    private string? _loadedName;
    private string? _loadedEmail;
    private string? _loadedBranch;

    // Appearance
    private readonly RadioButton[] _themes;
    private readonly ComboBox _dateFormat;
    private readonly ComboBox _timeFormat;
    private readonly ComboBox _tabSize;
    private readonly CheckBox _jumpToChange;
    private readonly CheckBox _fileGap;
    private readonly ComboBox _previewImages;
    private readonly ComboBox _font;
    private readonly ComboBox _fontSize;
    private readonly ComboBox _themeFont;
    private readonly ComboBox _themeSize;
    private readonly TextBlock _themeTextTitle = new TextBlock { FontWeight = FontWeight.SemiBold,
                                                                 Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<string> _fonts = TextLook.SystemFonts();
    private Dictionary<string, ThemeText> _themeText;
    private string? _textTheme;
    private readonly ComboBox _colours = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ThemeStore _themeStore = ThemeStore.Default;
    private const string BUILT_IN_COLOURS = AppSettings.BUILT_IN_COLOURS;
    private const string SYSTEM_FONT = "System default";
    private const string SAME_FOR_ALL = "Same as every theme";
    private readonly ThemePreview _preview = new ThemePreview { Margin = new Thickness(0, 8, 0, 0) };
    // The listed themes by name, read once per listing rather than once per row drawn
    private Dictionary<string, Urras.Core.Settings.Theme> _listedThemes =
        new Dictionary<string, Urras.Core.Settings.Theme>(StringComparer.OrdinalIgnoreCase);

    // Prompts
    private readonly CheckBox _confirmRemoveRepository;
    private readonly CheckBox _confirmDiscard;
    private readonly CheckBox _confirmDropStash;
    private readonly CheckBox _confirmCheckout;
    private readonly CheckBox _confirmRemoveWorktree;
    private readonly CheckBox _lengthWarning;
    private readonly CheckBox _gitErrors;
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
        _jumpToChange = Check("Scroll to the first change when opening a file's diff", settings.JumpToFirstChange);
        _fileGap = Check("Leave a gap before each file in the Combined view", settings.CombinedFileGap);
        _previewImages = Combo(new List<string>
        {
            "Don't show pictures", "Pictures from the repository", "Pictures from the repository and the internet"
        }, null);
        _previewImages.SelectedIndex = (int)settings.PreviewImages;
        _font = TextLook.FontPicker(SYSTEM_FONT, _fonts);
        TextLook.ChooseFont(_font, _fonts, settings.FontFamily);
        _fontSize = TextLook.SizePicker(null);
        TextLook.ChooseSize(_fontSize, false, settings.FontSize);
        _themeFont = TextLook.FontPicker(SAME_FOR_ALL, _fonts);
        _themeSize = TextLook.SizePicker(SAME_FOR_ALL);
        _themeText = settings.ThemeText.ToDictionary(entry => entry.Key, entry => new ThemeText
        {
            FontSize = entry.Value.FontSize,
            FontFamily = entry.Value.FontFamily
        }, StringComparer.OrdinalIgnoreCase);

        PromptSettings prompts = settings.Prompts;
        _confirmRemoveRepository = Check("Removing repositories from the list", prompts.RemovingRepositories);
        _confirmDiscard = Check("Discarding changes", prompts.DiscardingChanges);
        _confirmDropStash = Check("Dropping a stash", prompts.DroppingStashes);
        _confirmCheckout = Check("Checking out a commit", prompts.CheckingOutCommits);
        _confirmRemoveWorktree = Check("Removing worktrees", prompts.RemovingWorktrees);
        _lengthWarning = Check("Show the summary length counter", prompts.ShowCommitLengthWarning);
        _gitErrors = Check("Show Git's errors in a window, not just the status bar", prompts.ShowingGitErrors);
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
        _checkUpdates = Check("Check GitHub for a new version of Urras once a day", settings.CheckForUpdates);
        _closeButton = Combo(new List<string> { "Exit Urras completely", "Minimise Urras to the system tray" }, null);
        _closeButton.SelectedIndex = (int)settings.CloseButton;
        // Only where there's always a notification area to come back from
        _closeButton.IsEnabled = MainWindow.TrayWorks;
        _confirmExit = Check("Ask before exiting Urras", settings.ConfirmBeforeExit);
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
        TrackSettings();
        Opened += async (_, _) => await LoadGitSettingsAsync();
    }

    // Leaving with unsaved changes asks first, listing each one
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeAllowed || e.Cancel)
            return;

        List<SettingChange> changes = _tracker.Changes();
        if (changes.Count == 0)
            return;

        e.Cancel = true;
        _ = AskAboutChangesAsync(changes);
    }

    private async Task AskAboutChangesAsync(List<SettingChange> changes)
    {
        switch (await Dialogs.UnsavedChangesAsync(this, changes.Select(change => change.ToString()).ToList()))
        {
            case Dialogs.UnsavedChoice.Save:
            {
                await SaveAsync();
                break;
            }
            case Dialogs.UnsavedChoice.Discard:
            {
                _closeAllowed = true;
                Close(false);
                break;
            }
            default:
            {
                break;
            }
        }
    }

    // What each setting showed when the window opened, to compare with when it closes
    private void TrackSettings()
    {
        _tracker.Track(ACCOUNTS, "GitHub client id", () => Text(_githubAppId));
        _tracker.Track(ACCOUNTS, "GitLab application id", () => Text(_gitlabAppId));
        _tracker.Track(ACCOUNTS, "Forks", () => Selected(_forkDefault));
        _tracker.Track(INTEGRATIONS, "External editor", () => Selected(_editor));
        _tracker.Track(INTEGRATIONS, "Editor path", () => Text(_editorPath));
        _tracker.Track(INTEGRATIONS, "Editor arguments", () => Text(_editorArguments));
        _tracker.Track(INTEGRATIONS, "Shell", () => Selected(_shell));
        _tracker.Track(INTEGRATIONS, "Shell path", () => Text(_shellPath));
        _tracker.Track(INTEGRATIONS, "Shell arguments", () => Text(_shellArguments));
        _tracker.Track(APPEARANCE, "Theme", () => Chosen(_themes));
        _tracker.Track(APPEARANCE, "Colours", () => Selected(_colours));
        _tracker.Track(APPEARANCE, "Date format", () => Selected(_dateFormat));
        _tracker.Track(APPEARANCE, "Time format", () => Selected(_timeFormat));
        _tracker.Track(APPEARANCE, "Tab size", () => Selected(_tabSize));
        _tracker.Track(APPEARANCE, "Scroll to the first change", () => OnOff(_jumpToChange));
        _tracker.Track(APPEARANCE, "Gap before each file", () => OnOff(_fileGap));
        _tracker.Track(APPEARANCE, "Pictures in previews", () => Selected(_previewImages));
        _tracker.Track(APPEARANCE, "Font", () => Selected(_font));
        _tracker.Track(APPEARANCE, "Text size", () => Selected(_fontSize));
        _tracker.Track(APPEARANCE, "Text per theme", ThemeTextSummary);
        _tracker.Track(APPEARANCE, "Full scroll bars", () => OnOff(_fullScrollBars));
        _tracker.Track(APPEARANCE, "Every window in the taskbar", () => OnOff(_windowsInTaskbar));
        foreach (CheckBox box in new[]
                 {
                     _confirmRemoveRepository, _confirmDiscard, _confirmDropStash, _confirmCheckout,
                     _confirmRemoveWorktree, _lengthWarning, _gitErrors
                 })
            _tracker.Track(PROMPTS, box.Content as string ?? "Prompt", () => OnOff(box));
        _tracker.Track(PROMPTS, "Switching branches with changes", () => Chosen(_switching));
        _tracker.Track(ASSISTANT, "AI assistant", () => Selected(_assistantKind));
        _tracker.Track(ASSISTANT, "Address", () => Text(_assistantEndpoint));
        _tracker.Track(ASSISTANT, "Model", () => Text(_assistantModel));
        _tracker.Track(ASSISTANT, "Command", () => Text(_assistantCommand));
        // The key itself is never shown, only that it will change
        _tracker.Track(ASSISTANT, "Key", () => _forgetKey ? "none" :
                                               string.IsNullOrWhiteSpace(_assistantKey.Text) ? "the saved key"
                                                                                             : "a new key");
        _tracker.Track(ADVANCED, "Background fetch", () => Selected(_autoFetch));
        _tracker.Track(ADVANCED, "Whitespace check", () => OnOff(_checkWhitespace));
        _tracker.Track(ADVANCED, "Update check", () => OnOff(_checkUpdates));
        _tracker.Track(ADVANCED, "Close button", () => Selected(_closeButton));
        _tracker.Track(ADVANCED, "Ask before exiting", () => OnOff(_confirmExit));
        _tracker.Track(ADVANCED, "Hide untracked files", () => OnOff(_hideUntracked));
    }

    private static string Text(TextBox box)
    {
        return box.Text?.Trim() ?? string.Empty;
    }

    private static string Selected(ComboBox box)
    {
        return box.SelectedItem?.ToString() ?? string.Empty;
    }

    private static string OnOff(CheckBox box)
    {
        return box.IsChecked == true ? "on" : "off";
    }

    private static string Chosen(RadioButton[] group)
    {
        return group.FirstOrDefault(radio => radio.IsChecked == true)?.Content as string ?? string.Empty;
    }

    // Pages

    private Control AccountsPage()
    {
        ShowAccounts();
        WrapPanel signIn = new WrapPanel();
        foreach (AccountKind kind in Enum.GetValues<AccountKind>())
        {
            Image icon = new Image
            {
                Width = BUTTON_ICON_SIZE,
                Height = BUTTON_ICON_SIZE,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false
            };
            StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = "Sign in to " + Account.PlatformName(kind) + "…",
                                                 VerticalAlignment = VerticalAlignment.Center });
            Button button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8) };
            _ = ShowSiteIconAsync(KnownHost(kind), icon, null);
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
                    Note("GitHub and gitlab.com sign in through the browser with nothing to set up. Your own " +
                         "GitLab server and GitHub Enterprise servers do too once Urras is registered with them as " +
                         "an app, which takes two minutes; their ids go below. Other platforms use a pasted token."),
                    Links.ToPage("How to register Urras (docs/oauth.md)", Links.Document("docs/oauth.md")),
                    Label("GitHub client id"), _githubAppId, Label("GitLab application id"), _gitlabAppId,
                    Heading("Forks"), _forkDefault,
                    Note("Where pull requests and \"View on…\" go for forks that have no choice of their own. A " +
                         "fork's own choice, in its Repository settings, always comes first."));
    }

    // The site's own icon, or a badge with the platform's name in the theme's colours until or unless it loads
    private static Control PlatformBadge(Account account)
    {
        AccountKind kind = account.Kind;
        // Codeberg runs Forgejo, a kind of Gitea, but is better known by its own name
        string name = kind == AccountKind.Gitea && account.WebHost == "codeberg.org" ? "Codeberg"
                                                                                      : Account.PlatformName(kind);
        Border badge = new Border
        {
            Background = ThemeBrushes.Get(ThemeColours.BADGE),
            CornerRadius = new CornerRadius(BADGE_RADIUS),
            Padding = new Thickness(BADGE_PADDING_X, BADGE_PADDING_Y),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = name, FontSize = BADGE_FONT_SIZE, FontWeight = FontWeight.SemiBold }
        };
        Image icon = new Image
        {
            Width = SITE_ICON_SIZE,
            Height = SITE_ICON_SIZE,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        Panel mark = new Panel { VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(mark, name + " (" + account.WebHost + ")");
        mark.Children.Add(badge);
        mark.Children.Add(icon);
        _ = ShowSiteIconAsync(account.WebHost, icon, badge);
        return mark;
    }

    // The site's icon in place of, or beside, its name when it loads; the name stays when it doesn't
    private static async Task ShowSiteIconAsync(string host, Image icon, Control? badge)
    {
        if (await SiteIcons.GetAsync(host) is not { } picture)
            return;
        icon.Source = picture;
        icon.IsVisible = true;
        if (badge != null)
            badge.IsVisible = false;
    }

    // Where a platform's icon comes from; a self-hosted kind borrows its public site's
    private static string KnownHost(AccountKind kind)
    {
        return kind switch
        {
            AccountKind.GitHub or AccountKind.GitHubEnterprise => "github.com",
            AccountKind.GitLab => "gitlab.com",
            AccountKind.Gitea => "gitea.com",
            _ => "bitbucket.org"
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
            OpensProfile(avatar, account);
            OpensProfile(names, account);
            row.ContextMenu = ProfileMenu(account);
            _accountList.Children.Add(row);
        }
    }

    // A click on the picture or names opens the account's page on its website
    private static void OpensProfile(Control control, Account account)
    {
        control.Cursor = new Cursor(StandardCursorType.Hand);
        // The gaps between the names count too
        if (control is Panel { Background: null } panel)
            panel.Background = Brushes.Transparent;
        ToolTip.SetTip(control, "Open " + account.ProfileUrl);
        control.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                return;
            e.Handled = true;
            SystemShell.OpenUrl(account.ProfileUrl);
        };
    }

    // Right-click: open the profile, or copy the username
    private ContextMenu ProfileMenu(Account account)
    {
        MenuItem open = new MenuItem { Header = "Open profile" };
        open.Click += (_, _) => SystemShell.OpenUrl(account.ProfileUrl);
        MenuItem copy = new MenuItem { Header = "Copy username" };
        copy.Click += async (_, _) =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(account.Username);
        };
        return new ContextMenu { ItemsSource = new[] { open, copy } };
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
                    Heading("Sign commits"), _signing, Label("Key"), _signingKey,
                    Note("Signed commits show as Verified on GitHub once the key is added to your account as a " +
                         "signing key. For SSH (Git 2.34 or newer), give the public key file; for GPG, the key's " +
                         "id."),
                    Note("These go in your global Git configuration, so other Git tools use them too."));
    }

    private Control AppearancePage()
    {
        StackPanel themes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (RadioButton theme in _themes)
            themes.Children.Add(theme);

        // Each theme in the list shows its colours, and the picture below shows the chosen one
        _colours.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((name, _) =>
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            if (name != null)
            {
                row.Children.Add(ThemePreview.Swatches(ThemeNamed(name)));
                row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            }

            return row;
        });
        ListColours(_settings.CustomTheme);
        _colours.SelectionChanged += (_, _) =>
        {
            ShowPreview();
            ShowThemeText();
        };
        ShowThemeText();
        foreach (RadioButton look in _themes)
            look.IsCheckedChanged += (_, _) => ShowPreview();
        ShowPreview();
        Button create = new Button { Content = "New theme…" };
        Button edit = new Button { Content = "Edit…" };
        Button folder = new Button { Content = "Open themes folder" };
        create.Click += async (_, _) => await NewThemeAsync();
        edit.Click += async (_, _) => await EditThemeAsync();
        folder.Click += (_, _) =>
        {
            Directory.CreateDirectory(_themeStore.Folder);
            SystemShell.OpenFolder(_themeStore.Folder);
        };
        ToolTip.SetTip(edit, "Change the colours of the theme chosen under Colours");
        Button everywhere = new Button { Content = "Use this theme's text for every theme" };
        everywhere.Click += (_, _) => UseThemeTextEverywhere();
        StackPanel themeButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        themeButtons.Children.Add(create);
        themeButtons.Children.Add(edit);
        themeButtons.Children.Add(folder);

        return Page(Heading("Theme"), themes, Label("Colours"), _colours, _preview, themeButtons,
                    Note("Colours offers ready-made themes (the same as Anarres's: Frost, Fel, Horde, Alliance, " +
                         "Parchment, Anarres's own, and GitHub, GitLab, SourceGit and Fork's colours) and your " +
                         "own. A theme sets every colour of Urras on top of a dark or light base, and its base " +
                         "wins over the choice above. Edit changes the theme chosen under Colours, built-in and " +
                         "ready-made ones included; Restore defaults in the editor brings back their original " +
                         "colours. Themes are files in the themes folder, in the same format as Anarres's, to " +
                         "share or edit; colours added by newer versions of Urras are written into them."),
                    Heading("Text, every theme"), Label("Text size"), _fontSize, Label("Font"), _font,
                    _themeTextTitle, Label("Text size"), _themeSize, Label("Font"), _themeFont, everywhere,
                    Note("A theme can have its own text size and font; \"" + SAME_FOR_ALL + "\" follows the " +
                         "settings above. The button makes this theme's choice the one for every theme and clears " +
                         "the other themes' own choices. Fonts are the ones installed on this computer."),
                    Heading("Formatting"), Label("Date format"), _dateFormat, Label("Time format"), _timeFormat,
                    Heading("Diffs"), Label("Tab size"), _tabSize,
                    _jumpToChange, _fileGap,
                    Label("Pictures in Markdown and HTML previews"), _previewImages,
                    Note("Pictures from the internet, such as README badges, are downloaded when shown, so their " +
                         "sites can see that. SVG pictures show as their description."),
                    Heading("Scroll bars"), _fullScrollBars,
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
                    Heading("Commit message"), _lengthWarning,
                    Heading("When Git fails"), _gitErrors);
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
        return Page(Heading("When clicking X (close window)"), _closeButton, _confirmExit,
                    Note("In the tray, click the Urras icon to bring it back, or right-click it to exit. " +
                         "File › Exit always exits."),
                    Heading("Updates"), _checkUpdates,
                    Heading("Background fetch"), _autoFetch,
                    Note("Fetches every repository in the list now and then, so ahead and behind counts stay " +
                         "current."),
                    Heading("Changes"), _checkWhitespace, _hideUntracked,
                    Heading("Files"),
                    Note("Urras keeps its settings, a backup of them for each of the last seven days, and crash.log " +
                         "(unexpected errors) in one folder."),
                    Links.ToFolder(Path.GetDirectoryName(AppSettings.DefaultPath) ?? AppSettings.DefaultPath,
                                   Path.GetDirectoryName(AppSettings.DefaultPath) ?? AppSettings.DefaultPath));
    }

    // Themes

    // Built-in colours first, then each theme file, with the given one chosen
    private void ListColours(string? chosen)
    {
        // Ready-made themes, then theme files; one named after a ready-made theme, Dark or Light is your version
        _listedThemes = new Dictionary<string, Urras.Core.Settings.Theme>(StringComparer.OrdinalIgnoreCase);
        foreach (string preset in ThemePresets.Names)
            _listedThemes[preset] = ThemePresets.Find(preset)!;
        foreach (Urras.Core.Settings.Theme theme in _themeStore.LoadAll())
        {
            if (theme.Name is not (Urras.Core.Settings.Theme.DARK or Urras.Core.Settings.Theme.LIGHT))
                _listedThemes[theme.Name] = theme;
        }

        List<string> names = _listedThemes.Keys.ToList();
        _colours.ItemsSource = names.Prepend(BUILT_IN_COLOURS).ToList();
        int index = chosen == null ? -1 : names.FindIndex(name => string.Equals(name, chosen,
                                                                                StringComparison.OrdinalIgnoreCase));
        _colours.SelectedIndex = index + 1;
    }

    // A new theme starts as a copy of the built-in dark or light colours, whichever look is chosen above
    private async Task NewThemeAsync()
    {
        string? name = (await Dialogs.PromptAsync(this, "New theme", "Name of the theme:"))?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        if (name is Urras.Core.Settings.Theme.DARK or Urras.Core.Settings.Theme.LIGHT or BUILT_IN_COLOURS ||
            ThemePresets.IsPreset(name) || _themeStore.Find(name) != null)
        {
            await Dialogs.ShowTextAsync(this, "New theme", "There's already a theme called " + name + ".");
            return;
        }

        // A copy of the colours chosen below, or else of the built-in dark or light ones
        bool light = Array.FindIndex(_themes, radio => radio.IsChecked == true) == (int)ThemeChoice.Light;
        Urras.Core.Settings.Theme theme = (SelectedTheme() ?? Urras.Core.Settings.Theme.BuiltIn(
                                               light ? ThemeBase.Light : ThemeBase.Dark)).Copy(name);
        try
        {
            _themeStore.Save(theme);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await Dialogs.ShowTextAsync(this, "New theme", "Couldn't save the theme: " + exception.Message);
            return;
        }

        ListColours(name);
        await EditThemeAsync();
    }

    // Edits the chosen theme in place; built-in and ready-made ones are saved as your version of them
    private async Task EditThemeAsync()
    {
        string chosen = _colours.SelectedItem as string ?? BUILT_IN_COLOURS;
        Urras.Core.Settings.Theme theme;
        string about;
        if (chosen == BUILT_IN_COLOURS)
        {
            ThemeBase look = ChosenBase();
            theme = _themeStore.BuiltIn(look).Copy(look == ThemeBase.Light ? Urras.Core.Settings.Theme.LIGHT
                                                                           : Urras.Core.Settings.Theme.DARK);
            about = "the built-in " + theme.Name + " colours (the choice above picks Dark or Light)";
        }
        else if (ThemePresets.IsPreset(chosen))
        {
            theme = (_themeStore.Find(chosen) ?? ThemePresets.Find(chosen)!).Copy(chosen);
            about = chosen + ", a ready-made theme";
        }
        else if (_themeStore.Find(chosen) is { } custom)
        {
            theme = custom;
            about = chosen + ", your theme";
        }
        else
            return;

        await new ThemeEditorWindow(theme, _themeStore, about).ShowDialog<bool>(this);
        // The edited colours show in the list and the preview straight away
        ListColours(chosen == BUILT_IN_COLOURS ? null : chosen);
        ShowPreview();
    }

    private ThemeBase ChosenBase()
    {
        bool light = Array.FindIndex(_themes, radio => radio.IsChecked == true) == (int)ThemeChoice.Light;
        return light ? ThemeBase.Light : ThemeBase.Dark;
    }

    private string ThemeKey => _colours.SelectedItem as string ?? BUILT_IN_COLOURS;

    // Keeps the shown theme's own text choices, then shows the newly chosen theme's
    private void ShowThemeText()
    {
        KeepThemeText();
        _textTheme = ThemeKey;
        _themeTextTitle.Text = "Text, this theme only: " + _textTheme;
        ThemeText? text = _themeText.GetValueOrDefault(_textTheme);
        TextLook.ChooseSize(_themeSize, true, text?.FontSize);
        TextLook.ChooseFont(_themeFont, _fonts, text?.FontFamily);
    }

    private void KeepThemeText()
    {
        if (_textTheme == null)
            return;
        _themeText[_textTheme] = new ThemeText
        {
            FontSize = TextLook.ChosenSize(_themeSize, true),
            FontFamily = TextLook.ChosenFont(_themeFont)
        };
    }

    // The themes with their own text, such as "Fel: 16 pt Inter", for the unsaved-changes list
    private string ThemeTextSummary()
    {
        KeepThemeText();
        List<string> own = _themeText.Where(entry => entry.Value.FontSize != null ||
                                                     !string.IsNullOrEmpty(entry.Value.FontFamily))
                                     .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                                     .Select(entry => entry.Key + ": " + Describe(entry.Value)).ToList();
        return own.Count == 0 ? "none" : string.Join(", ", own);
    }

    private static string Describe(ThemeText text)
    {
        string size = text.FontSize is { } points ? points + " pt" : string.Empty;
        return (size + " " + text.FontFamily).Trim();
    }

    // This theme's text becomes the one for every theme, and every theme's own choice is cleared
    private void UseThemeTextEverywhere()
    {
        if (_themeSize.SelectedIndex > 0)
            _fontSize.SelectedIndex = _themeSize.SelectedIndex - 1;
        if (_themeFont.SelectedIndex > 0)
            _font.SelectedIndex = _themeFont.SelectedIndex;
        _themeText.Clear();
        _textTheme = null;
        ShowThemeText();
    }

    // A theme by its name in the list; the built-in entry follows the dark or light choice above
    private Urras.Core.Settings.Theme ThemeNamed(string name)
    {
        if (name != BUILT_IN_COLOURS && _listedThemes.TryGetValue(name, out Urras.Core.Settings.Theme? theme))
            return theme;
        return _themeStore.BuiltIn(ChosenBase());
    }

    private void ShowPreview()
    {
        _preview.Show(ThemeNamed(_colours.SelectedItem as string ?? BUILT_IN_COLOURS));
    }

    // The theme picked under Colours, ready-made or a file; null for the built-in colours
    private Urras.Core.Settings.Theme? SelectedTheme()
    {
        return _colours.SelectedIndex > 0 && _colours.SelectedItem is string name &&
               _listedThemes.TryGetValue(name, out Urras.Core.Settings.Theme? theme)
            ? theme
            : null;
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
        _loadedSigning = await GlobalConfig.GetAsync(GlobalConfig.SIGN_COMMITS);
        _loadedFormat = await GlobalConfig.GetAsync(GlobalConfig.SIGNING_FORMAT);
        _loadedKey = await GlobalConfig.GetAsync(GlobalConfig.SIGNING_KEY);
        bool signing = string.Equals(_loadedSigning, "true", StringComparison.OrdinalIgnoreCase);
        _signing.SelectedIndex = !signing ? 0 : _loadedFormat == "ssh" ? 1 : 2;
        _signingKey.Text = _loadedKey;
        _tracker.Track(GIT, "Sign commits", () => Selected(_signing));
        _tracker.Track(GIT, "Signing key", () => Text(_signingKey));
        _tracker.Track(GIT, "Name", () => Text(_userName));
        _tracker.Track(GIT, "Email", () => Text(_userEmail));
        _tracker.Track(GIT, "Default branch", () => Text(_defaultBranch));
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
        _settings.JumpToFirstChange = _jumpToChange.IsChecked == true;
        _settings.CombinedFileGap = _fileGap.IsChecked == true;
        _settings.PreviewImages = (PreviewImages)Math.Max(0, _previewImages.SelectedIndex);
        _settings.FontFamily = TextLook.ChosenFont(_font) ?? string.Empty;
        _settings.FontSize = TextLook.ChosenSize(_fontSize, false) ?? AppSettings.DEFAULT_FONT_SIZE;
        KeepThemeText();
        _settings.ThemeText = _themeText.Where(entry => entry.Value.FontSize != null ||
                                                        !string.IsNullOrEmpty(entry.Value.FontFamily))
                                        .ToDictionary(entry => entry.Key, entry => entry.Value,
                                                      StringComparer.OrdinalIgnoreCase);
        _settings.CustomTheme = _colours.SelectedIndex > 0 ? _colours.SelectedItem as string : null;

        PromptSettings prompts = _settings.Prompts;
        prompts.RemovingRepositories = _confirmRemoveRepository.IsChecked == true;
        prompts.DiscardingChanges = _confirmDiscard.IsChecked == true;
        prompts.DroppingStashes = _confirmDropStash.IsChecked == true;
        prompts.CheckingOutCommits = _confirmCheckout.IsChecked == true;
        prompts.RemovingWorktrees = _confirmRemoveWorktree.IsChecked == true;
        prompts.ShowCommitLengthWarning = _lengthWarning.IsChecked == true;
        prompts.ShowingGitErrors = _gitErrors.IsChecked == true;
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
        _settings.CheckForUpdates = _checkUpdates.IsChecked == true;
        _settings.CloseButton = (CloseAction)Math.Max(0, _closeButton.SelectedIndex);
        _settings.ConfirmBeforeExit = _confirmExit.IsChecked == true;
        _settings.AlwaysShowScrollBars = _fullScrollBars.IsChecked == true;
        _settings.ShowWindowsInTaskbar = _windowsInTaskbar.IsChecked == true;
        _settings.DefaultForkTarget = (ForkTarget)Math.Max(0, _forkDefault.SelectedIndex);
        _settings.Save();

        // Only what changed is written to the global Git configuration
        await SaveGlobalAsync(GlobalConfig.USER_NAME, _loadedName, _userName.Text);
        await SaveGlobalAsync(GlobalConfig.USER_EMAIL, _loadedEmail, _userEmail.Text);
        await SaveGlobalAsync(GlobalConfig.DEFAULT_BRANCH, _loadedBranch, _defaultBranch.Text);
        // Off leaves signing unset unless it was on before; the key stays for turning it back on
        bool sign = _signing.SelectedIndex > 0;
        string? signSetting = sign ? "true" : _loadedSigning == null ? null : "false";
        await SaveGlobalAsync(GlobalConfig.SIGN_COMMITS, _loadedSigning, signSetting);
        if (sign)
            await SaveGlobalAsync(GlobalConfig.SIGNING_FORMAT, _loadedFormat,
                                  _signing.SelectedIndex == 1 ? "ssh" : "openpgp");
        await SaveGlobalAsync(GlobalConfig.SIGNING_KEY, _loadedKey, _signingKey.Text);
        _closeAllowed = true;
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
