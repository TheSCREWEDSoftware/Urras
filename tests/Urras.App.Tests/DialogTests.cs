using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;
using Xunit;

namespace Urras.App.Tests;

// Every dialog opens, lays out and closes without an error
public sealed class DialogTests
{
    private static readonly Dialogs.ForkChoice Fork = new Dialogs.ForkChoice(
        "azerothcore/wiki", "TheSCREWEDSoftware/wiki", ForkTarget.Unset,
        "Signed in to GitHub as @someone.\nYou can push to TheSCREWEDSoftware/wiki.");

    private static readonly Dialogs.ForkChoice NotAFork = new Dialogs.ForkChoice(
        null, "TheSCREWEDSoftware/Urras", ForkTarget.Unset, "This repository isn't a fork.");

    private static Window Owner()
    {
        Window owner = new Window();
        owner.Show();
        Ui.Pump();
        return owner;
    }

    [AvaloniaFact]
    public void SimpleDialogsOpenAndClose()
    {
        Window owner = Owner();
        Ui.OpenAndClose(owner, () => Dialogs.ConfirmAsync(owner, "Remove", "Remove it?", "Remove"));
        Ui.OpenAndClose(owner, () => Dialogs.PromptAsync(owner, "New branch", "Name:", "feature"));
        Ui.OpenAndClose(owner, () => Dialogs.ChooseAsync(owner, "Pick", "Which?", new[] { "One", "Two" }));
        Ui.OpenAndClose(owner, () => Dialogs.ChooseManyAsync(owner, "Add", "Found 2.", new[] { "a", "b" }, "Add"));
        Ui.OpenAndClose(owner, () => Dialogs.EditTextAsync(owner, "Edit", "text"));
        Ui.OpenAndClose(owner, () => Dialogs.MessageAsync(owner, "Message", "Summary"));
        Ui.OpenAndClose(owner, () => Dialogs.StashAsync(owner));
        Ui.OpenAndClose(owner, () => Dialogs.CustomCommandAsync(owner, null));
        Ui.OpenAndClose(owner, async () =>
        {
            await Dialogs.ShowTextAsync(owner, "Details", "Line one\nLine two");
            return true;
        });
        Ui.OpenAndClose(owner, async () =>
        {
            await Dialogs.AboutAsync(owner, "0.1.0");
            return true;
        });
        owner.Close();
    }

    [AvaloniaFact]
    public void ForkDialogsOpenAndClose()
    {
        Window owner = Owner();
        Assert.Null(Ui.OpenAndClose(owner, () => Dialogs.ForkTargetAsync(owner, Fork)));
        Ui.OpenAndClose(owner, () => Dialogs.RepositorySettingsAsync(owner, "wiki", LineEndingPolicy.Custom, Fork));
        Ui.OpenAndClose(owner, () => Dialogs.RepositorySettingsAsync(owner, "Urras", LineEndingPolicy.Lf, NotAFork));
        owner.Close();
    }

    // Saving line endings mustn't quietly answer the fork question as well
    [AvaloniaFact]
    public void SavingRepositorySettingsLeavesTheForkChoiceAlone()
    {
        Window owner = Owner();
        Task<Dialogs.RepositorySettingsResult?> task =
            Dialogs.RepositorySettingsAsync(owner, "wiki", LineEndingPolicy.Custom, Fork);
        Ui.Pump(() => owner.OwnedWindows.Count > 0);
        Button save = Ui.Find<Button>(owner.OwnedWindows[^1], button => button.Content as string == "Save");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Ui.Pump(() => task.IsCompleted);
        Assert.True(task.IsCompleted, "saved");
        Assert.Null(task.GetAwaiter().GetResult()!.Fork);
        owner.Close();
    }

    [AvaloniaFact]
    public void OptionsPagesOpenAgainAndAgain()
    {
        AppSettings settings = new AppSettings();
        AccountManager accounts = new AccountManager(settings, new MemoryTokenStore(), new HttpClient());
        OptionsWindow window = new OptionsWindow(settings, accounts, (_, _) => Task.CompletedTask);
        window.Show();
        Ui.VisitEveryPage(window);
        window.Close();
    }
}
