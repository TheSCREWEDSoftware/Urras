using System.Text.Json;
using System.Text.Json.Serialization;

namespace Urras.Core.Settings;

// Per file: a list of changed files with one diff at a time. Combined: every file in one diff
public enum FileListMode
{
    PerFile,
    Combined
}

// How the automatic repository list is grouped
public enum RepositoryGrouping
{
    // Each repository name, with every copy (fork or clone) inside it named by its owner
    Name,

    // The folders on disk
    Folder,

    // Each owner (user or organisation), like GitHub Desktop
    Owner
}

public enum DiffViewMode
{
    Unified,
    Split,
    Raw
}

public sealed class AppSettings
{
    public const string APP_FOLDER_NAME = "Urras";
    public const string SETTINGS_FILE_NAME = "settings.json";
    public const int DEFAULT_AUTO_FETCH_MINUTES = 10;
    public const int DEFAULT_TAB_SIZE = 4;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public List<string> Repositories { get; set; } = new List<string>();
    public string? LastRepository { get; set; }

    // Saved by name, so the settings file stays readable
    [JsonConverter(typeof(JsonStringEnumConverter<DiffViewMode>))]
    public DiffViewMode DiffView { get; set; } = DiffViewMode.Unified;

    // On: the repository list mirrors the folders on disk. Off: ManualLayout is used
    public bool GroupByFolder { get; set; } = true;

    // How the automatic list groups repositories (GroupByFolder on means automatic)
    [JsonConverter(typeof(JsonStringEnumConverter<RepositoryGrouping>))]
    public RepositoryGrouping Grouping { get; set; } = RepositoryGrouping.Name;

    // Folders pinned to the top of the list, laid out as on disk whatever the grouping, and to the toolbar
    public List<string> PinnedFolders { get; set; } = new List<string>();

    // Repositories pinned to the top of the list and to the toolbar
    public List<string> PinnedRepositories { get; set; } = new List<string>();

    // Kept while grouping by folder too, so switching back restores it
    public LayoutGroup? ManualLayout { get; set; }

    // Diffs show spaces, tabs and line endings
    public bool ShowWhitespace { get; set; }

    // Long diff lines wrap instead of scrolling sideways
    public bool WrapDiffLines { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<FileListMode>))]
    public FileListMode FileListMode { get; set; } = FileListMode.PerFile;

    // Committing asks first when the staged changes have whitespace problems such as trailing spaces
    public bool CheckWhitespaceBeforeCommit { get; set; }

    // Signed-in accounts; their tokens are in the system's credential store, not here
    public List<Urras.Core.Accounts.Account> Accounts { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter<ThemeChoice>))]
    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    public string DateFormat { get; set; } = DisplayFormats.DEFAULT_DATE;
    public string TimeFormat { get; set; } = DisplayFormats.DEFAULT_TIME;

    // Spaces shown for a tab in diffs and the file viewer
    public int TabSize { get; set; } = DEFAULT_TAB_SIZE;

    public ProgramChoice Editor { get; set; } = new ProgramChoice { Name = "Visual Studio Code" };
    public ProgramChoice Shell { get; set; } = new ProgramChoice { Name = "Default" };
    public PromptSettings Prompts { get; set; } = new PromptSettings();

    // Commands added to the menus by the user
    public List<Urras.Core.Commands.CustomCommand> CustomCommands { get; set; } = new();

    // History lists every branch, remote branch and tag, with the commit graph beside it
    public bool HistoryAllBranches { get; set; } = true;

    // Untracked files are left out of the change list and of Stage all
    public bool HideUntracked { get; set; }

    // Minutes between background fetches of every repository; 0 turns them off
    public int AutoFetchMinutes { get; set; } = DEFAULT_AUTO_FETCH_MINUTES;

    // Folder groups the user collapsed in the automatic layout
    public List<string> CollapsedFolders { get; set; } = new List<string>();

    // %APPDATA%\Urras on Windows, ~/.config/Urras on Linux
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), APP_FOLDER_NAME,
                     SETTINGS_FILE_NAME);

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ??
                       new AppSettings();
        }
        catch (JsonException)
        {
            // A broken settings file shouldn't stop the app from starting
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        string? folder = Path.GetDirectoryName(path);
        if (folder != null)
            Directory.CreateDirectory(folder);

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public bool AddRepository(string path)
    {
        if (Repositories.Any(existing => PathsEqual(existing, path)))
            return false;

        Repositories.Add(path);
        Repositories.Sort(StringComparer.OrdinalIgnoreCase);
        return true;
    }

    public void RemoveRepository(string path)
    {
        Repositories.RemoveAll(existing => PathsEqual(existing, path));
        if (ManualLayout != null)
            RepositoryLayout.RemoveRepository(ManualLayout, path);
        if (LastRepository != null && PathsEqual(LastRepository, path))
            LastRepository = null;
    }

    private static bool PathsEqual(string first, string second)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase
                                                                  : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first).TrimEnd('/', '\\'), Path.GetFullPath(second).TrimEnd('/', '\\'),
                             comparison);
    }
}
