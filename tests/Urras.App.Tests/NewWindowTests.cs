using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Urras.Core.Accounts;
using Urras.Core.Settings;
using Xunit;

namespace Urras.App.Tests;

// The windows added with the File menu, themes and the unsaved-changes question
public sealed class NewWindowTests
{
    // GitHub's template lists, answered without the network
    private sealed class FakeTemplates : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.AbsolutePath == "/licenses"
                ? "[{\"key\":\"mit\",\"name\":\"MIT License\"}]"
                : "[\"C++\",\"Node\"]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    // GitHub's organisations and a new repository, answered without the network
    private sealed class FakeGitHub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            string body = request.Method == HttpMethod.Post
                ? "{\"full_name\":\"ryan/B_Forlorn-Hope---Complete\",\"clone_url\":" +
                  "\"https://github.com/ryan/B_Forlorn-Hope---Complete.git\",\"private\":true}"
                : "[{\"id\":1,\"login\":\"Phantom-Sonar\"}]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static Window Owner()
    {
        Window owner = new Window();
        owner.Show();
        Ui.Pump();
        return owner;
    }

    [AvaloniaFact]
    public void CreateRepositoryOpensWithGitHubsTemplates()
    {
        Window owner = Owner();
        CreateRepositoryWindow window = new CreateRepositoryWindow(new HttpClient(new FakeTemplates()),
                                                                   Path.GetTempPath());
        Task<object?> task = window.ShowDialog<object?>(owner);
        Ui.Pump(() => Ui.Find<ComboBox>(window).ItemCount > 1);
        Assert.Equal(3, Ui.Find<ComboBox>(window).ItemCount);
        Assert.False(Ui.Find<Button>(window, button => button.Content as string == "Create repository").IsEnabled);
        window.Close();
        Ui.Pump(() => task.IsCompleted);
        owner.Close();
    }

    // Like GitHub Desktop's: the organisations listed, the name the site will use shown, and Publish makes it
    [AvaloniaFact]
    public void PublishRepositoryListsOrganisationsAndPublishes()
    {
        Window owner = Owner();
        Account account = new Account
        {
            Kind = AccountKind.GitHub, ApiUrl = GitHubClient.GITHUB_API, Username = "ryan"
        };
        GitHubClient client = new GitHubClient(new HttpClient(new FakeGitHub()), "t");
        PublishRepositoryWindow window = new PublishRepositoryWindow(
            new List<(Account Account, PlatformClient Client)> { (account, client) }, "B_Forlorn Hope - Complete",
            (_, _) => Task.FromResult<PlatformClient?>(null));
        Task<PlatformRepository?> task = window.ShowDialog<PlatformRepository?>(owner);
        Ui.Pump(() => Ui.Find<ComboBox>(window, combo => combo.IsVisible).ItemCount > 1);
        Assert.Equal(2, Ui.Find<ComboBox>(window, combo => combo.IsVisible).ItemCount);
        const string RENAMED = "Will be published as B_Forlorn-Hope---Complete";
        Assert.NotNull(Ui.Find<TextBlock>(window, text => text.Text == RENAMED));

        Ui.Find<Button>(window, button => button.Content as string == "Publish repository")
          .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => task.IsCompleted);
        Assert.Equal("https://github.com/ryan/B_Forlorn-Hope---Complete.git", task.Result?.CloneUrl);
        owner.Close();
    }

    [AvaloniaFact]
    public void ThemeEditorAndUnsavedChangesOpenAndClose()
    {
        Window owner = Owner();
        string folder = Path.Combine(TestApp.SettingsFolder, "themes-test");
        Urras.Core.Settings.Theme theme = Urras.Core.Settings.Theme.BuiltIn(ThemeBase.Dark).Copy("Test");
        Ui.OpenAndClose(owner, () => new ThemeEditorWindow(theme, new ThemeStore(folder)).ShowDialog<bool>(owner));
        Dialogs.UnsavedChoice choice = Ui.OpenAndClose(owner, () => Dialogs.UnsavedChangesAsync(
            owner, new[] { "Changed Theme from Dark to Light (Appearance)" }));
        Assert.Equal(Dialogs.UnsavedChoice.KeepEditing, choice);
        owner.Close();
    }

    // With one taskbar entry chosen, the main window keeps it and the other windows stay out of the taskbar
    [AvaloniaFact]
    public void MainWindowStaysInTheTaskbar()
    {
        AppSettings settings = new AppSettings { ShowWindowsInTaskbar = false };
        settings.Save();
        MainWindow window = new MainWindow();
        window.Show();
        Ui.Pump();
        Window dialog = new Window();
        dialog.Show(window);
        Ui.Pump();
        Assert.True(window.ShowInTaskbar);
        Assert.False(dialog.ShowInTaskbar);
        dialog.Close();
        window.Close();
    }

    // Closing Options after changing something asks first; Discard closes it without saving
    [AvaloniaFact]
    public void OptionsAsksBeforeLosingChanges()
    {
        AppSettings settings = new AppSettings();
        settings.Save();
        MainWindow window = new MainWindow();
        window.Show();
        Ui.Pump();
        window.FindControl<Button>("SettingsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => window.OwnedWindows.Count > 0);
        OptionsWindow options = Assert.IsType<OptionsWindow>(window.OwnedWindows[^1]);
        Ui.VisitEveryPage(options);
        Ui.Find<ListBox>(options, list => list.ItemsSource is string[]).SelectedItem = OptionsWindow.APPEARANCE;
        Ui.Pump();

        CheckBox taskbar = Ui.Find<CheckBox>(options, box => (box.Content as string)?.Contains("taskbar") == true);
        taskbar.IsChecked = !taskbar.IsChecked;
        options.Close(false);
        Ui.Pump(() => options.OwnedWindows.Count > 0);
        Window question = options.OwnedWindows[^1];
        Assert.Equal("Unsaved changes", question.Title);

        Ui.Find<Button>(question, button => button.Content as string == "Discard")
          .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => !options.IsVisible);
        Assert.False(options.IsVisible);
        Assert.False(AppSettings.Load().ShowWindowsInTaskbar);
        window.Close();
    }
}
