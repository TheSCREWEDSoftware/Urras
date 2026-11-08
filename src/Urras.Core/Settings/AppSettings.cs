using System.Text.Json;
using System.Text.Json.Serialization;

namespace Urras.Core.Settings;

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

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    public List<string> Repositories { get; set; } = new List<string>();
    public string? LastRepository { get; set; }

    // Saved by name, so the settings file stays readable
    [JsonConverter(typeof(JsonStringEnumConverter<DiffViewMode>))]
    public DiffViewMode DiffView { get; set; } = DiffViewMode.Unified;

    // On: the repository list mirrors the folders on disk. Off: ManualLayout is used
    public bool GroupByFolder { get; set; } = true;

    // Kept while grouping by folder too, so switching back restores it
    public LayoutGroup? ManualLayout { get; set; }

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
