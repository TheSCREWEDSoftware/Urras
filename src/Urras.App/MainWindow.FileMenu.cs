using Avalonia.Controls;
using Urras.Core.Git;

namespace Urras.App;

// The File menu, like GitHub Desktop's: new, add and clone repositories, Options, and Exit
public partial class MainWindow
{
    private void InitializeFileMenu()
    {
        NewRepositoryItem.Click += async (_, _) => await NewRepositoryAsync();
        AddLocalRepositoryItem.Click += async (_, _) => await AddRepositoryAsync();
        CloneRepositoryItem.Click += async (_, _) => await CloneRepositoryAsync();
        OptionsItem.Click += async (_, _) => await OpenOptionsAsync(OptionsWindow.ACCOUNTS);
        ExitItem.Click += async (_, _) => await ExitAsync();
    }

    // New repositories go next to the open one by default, or in Documents when none is open
    private string DefaultParentFolder()
    {
        string? parent = _repository == null ? null : Path.GetDirectoryName(_repository.Path.TrimEnd('/', '\\'));
        return parent ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private async Task NewRepositoryAsync()
    {
        CreateRepositoryWindow window = new CreateRepositoryWindow(AccountsHttp, DefaultParentFolder());
        if (await window.ShowDialog<NewRepository?>(this) is not { } repository)
            return;

        SetBusy(true, "Creating " + repository.Name + "…");
        GitResult result = await RepositoryCreator.CreateAsync(repository);
        SetBusy(false, null);
        if (ReportResult(result, "Created " + repository.Folder + "."))
            await AddAndOpenRepositoryAsync(repository.Folder);
    }
}
