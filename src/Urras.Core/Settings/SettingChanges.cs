namespace Urras.Core.Settings;

// A setting changed in a window but not saved yet
public sealed record SettingChange(string Page, string Name, string Before, string After)
{
    // "Changed Theme from Dark to Light (Appearance)"
    public override string ToString()
    {
        return "Changed " + Name + " from " + Shown(Before) + " to " + Shown(After) + " (" + Page + ")";
    }

    private static string Shown(string value)
    {
        return value.Length == 0 ? "(empty)" : value;
    }
}

// Remembers each setting as a window first showed it, then lists the ones that differ now
public sealed class SettingTracker
{
    private readonly List<(string Page, string Name, string Before, Func<string> Read)> _settings =
        new List<(string Page, string Name, string Before, Func<string> Read)>();

    // Tracking a setting again (once its value has loaded) takes its new value as the starting point
    public void Track(string page, string name, Func<string> read)
    {
        _settings.RemoveAll(setting => setting.Page == page && setting.Name == name);
        _settings.Add((page, name, read(), read));
    }

    public List<SettingChange> Changes()
    {
        return _settings.Select(setting => new SettingChange(setting.Page, setting.Name, setting.Before,
                                                             setting.Read()))
                        .Where(change => change.Before != change.After)
                        .ToList();
    }
}
