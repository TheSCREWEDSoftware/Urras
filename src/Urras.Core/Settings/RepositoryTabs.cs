namespace Urras.Core.Settings;

// The repositories open in tabs, as Fork keeps them, and which one is showing
public sealed class RepositoryTabs
{
    private readonly List<string> _paths = new List<string>();

    // The saved tabs, with current (the repository last open) in them even if the list lost it
    public RepositoryTabs(IEnumerable<string> saved, int active, string? current)
    {
        foreach (string path in saved.Where(path => !Contains(path)))
            _paths.Add(path);
        Active = _paths.Count == 0 ? -1 : Math.Clamp(active, 0, _paths.Count - 1);
        if (current != null && IndexOf(current) is var index and >= 0)
            Active = index;
        else if (current != null && Active < 0)
            Show(current);
        else if (current != null)
            ShowInNewTab(current);
    }

    public IReadOnlyList<string> Paths => _paths;
    public int Active { get; private set; }
    public string? ActivePath => Active >= 0 ? _paths[Active] : null;

    // Shows a repository in the current tab, or switches to the tab already showing it
    public void Show(string path)
    {
        int existing = IndexOf(path);
        if (existing >= 0)
            Active = existing;
        else if (Active < 0)
        {
            _paths.Add(path);
            Active = 0;
        }
        else
            _paths[Active] = path;
    }

    // Opens a repository in a new tab beside the current one, or switches to the tab already showing it
    public void ShowInNewTab(string path)
    {
        int existing = IndexOf(path);
        if (existing >= 0)
        {
            Active = existing;
            return;
        }

        _paths.Insert(Active + 1, path);
        Active++;
    }

    // Closes a tab, leaving at least one; the tab to the right takes over, or the one to the left at the end
    public bool Close(int index)
    {
        if (_paths.Count <= 1 || index < 0 || index >= _paths.Count)
            return false;

        _paths.RemoveAt(index);
        if (index < Active || Active >= _paths.Count)
            Active--;
        return true;
    }

    // Ctrl+Tab and Ctrl+Shift+Tab: the next or previous tab, round from the end to the start
    public void Step(int direction)
    {
        if (_paths.Count > 1)
            Active = ((Active + direction) % _paths.Count + _paths.Count) % _paths.Count;
    }

    // A repository removed from Urras leaves its tab too
    public void Forget(string path)
    {
        int index = IndexOf(path);
        if (index < 0)
            return;
        if (_paths.Count == 1)
        {
            _paths.Clear();
            Active = -1;
            return;
        }

        Close(index);
    }

    // A repository found in a new folder keeps its tab, or leaves it if the new folder already has one
    public void Move(string from, string to)
    {
        int index = IndexOf(from);
        if (index < 0)
            return;
        if (Contains(to))
            Forget(from);
        else
            _paths[index] = to;
    }

    public bool Contains(string path)
    {
        return IndexOf(path) >= 0;
    }

    private int IndexOf(string path)
    {
        return _paths.FindIndex(open => RepositoryLayout.PathComparer.Equals(open, path));
    }
}
