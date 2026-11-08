using System.Text.Json.Serialization;

namespace Urras.Core.Settings;

// One entry of a manual layout: either a repository path or a group
public sealed class LayoutItem
{
    public string? Repository { get; set; }
    public LayoutGroup? Group { get; set; }

    public static LayoutItem ForRepository(string path)
    {
        return new LayoutItem { Repository = path };
    }

    public static LayoutItem ForGroup(LayoutGroup group)
    {
        return new LayoutItem { Group = group };
    }
}

public sealed class LayoutGroup
{
    public string Name { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;

    // The folder this group came from when an automatic layout was turned into a manual one
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceFolder { get; set; }

    public List<LayoutItem> Items { get; set; } = new List<LayoutItem>();
}

// A node of the repository list, built either from folders or from a manual layout
public sealed class RepositoryTreeNode
{
    public RepositoryTreeNode(string name, string? repositoryPath, string? folder, LayoutItem? item)
    {
        Name = name;
        RepositoryPath = repositoryPath;
        Folder = folder;
        Item = item;
    }

    public string Name { get; }
    public string? RepositoryPath { get; }

    // The folder an automatic group stands for; null in manual layouts
    public string? Folder { get; }

    // The manual layout entry this node shows; null in automatic layouts
    public LayoutItem? Item { get; }

    public List<RepositoryTreeNode> Children { get; } = new List<RepositoryTreeNode>();
    public bool IsGroup => RepositoryPath == null;
}

public static class RepositoryLayout
{
    // Mirrors the folders on disk. From each drive, a chain of folders with a single subfolder and no repositories is
    // skipped, so each project folder (such as F:\Echo Flint Engine) becomes a top-level group with its subfolders
    // as nested groups
    public static List<RepositoryTreeNode> BuildByFolder(IEnumerable<string> repositories)
    {
        FolderTrie drives = new FolderTrie(string.Empty, string.Empty);
        foreach (string repository in repositories.Select(Normalize).Distinct(PathComparer))
        {
            string root = Path.GetPathRoot(repository) ?? string.Empty;
            FolderTrie node = drives.Child(root, root);
            string folder = root;
            string parent = Path.GetDirectoryName(repository) ?? root;

            foreach (string segment in RelativeSegments(root, parent))
            {
                folder = Path.Combine(folder, segment);
                node = node.Child(segment, folder);
            }

            node.Repositories.Add(repository);
        }

        List<RepositoryTreeNode> roots = new List<RepositoryTreeNode>();
        foreach (FolderTrie drive in drives.Children)
        {
            // Repositories directly on a drive keep the drive as their group
            if (drive.Repositories.Count > 0)
                roots.Add(ToNode(drive));
            else
                roots.AddRange(drive.Children.Select(child => ToNode(SkipSingleFolders(child))));
        }

        Sort(roots);
        return roots;
    }

    private sealed class FolderTrie
    {
        public FolderTrie(string name, string folder)
        {
            Name = name;
            Folder = folder;
        }

        public string Name { get; }
        public string Folder { get; }
        public List<FolderTrie> Children { get; } = new List<FolderTrie>();
        public List<string> Repositories { get; } = new List<string>();

        public FolderTrie Child(string name, string folder)
        {
            FolderTrie? child = Children.FirstOrDefault(existing => PathComparer.Equals(existing.Folder, folder));
            if (child == null)
            {
                child = new FolderTrie(name, folder);
                Children.Add(child);
            }

            return child;
        }
    }

    private static FolderTrie SkipSingleFolders(FolderTrie node)
    {
        while (node.Repositories.Count == 0 && node.Children.Count == 1)
            node = node.Children[0];
        return node;
    }

    private static RepositoryTreeNode ToNode(FolderTrie folder)
    {
        RepositoryTreeNode node = new RepositoryTreeNode(FolderName(folder.Folder), null, folder.Folder, null);
        foreach (FolderTrie child in folder.Children)
            node.Children.Add(ToNode(child));
        foreach (string repository in folder.Repositories)
            node.Children.Add(new RepositoryTreeNode(Path.GetFileName(repository), repository, null, null));
        return node;
    }

    // Turns an automatic layout into a manual one that looks the same
    public static LayoutGroup FromTree(IEnumerable<RepositoryTreeNode> nodes)
    {
        LayoutGroup root = new LayoutGroup();
        AddNodes(root, nodes);
        return root;
    }

    public static List<RepositoryTreeNode> ToTree(LayoutGroup root)
    {
        List<RepositoryTreeNode> nodes = new List<RepositoryTreeNode>();
        foreach (LayoutItem item in root.Items)
        {
            if (item.Repository != null)
                nodes.Add(new RepositoryTreeNode(Path.GetFileName(item.Repository), item.Repository, null, item));
            else if (item.Group != null)
            {
                RepositoryTreeNode node = new RepositoryTreeNode(item.Group.Name, null, null, item);
                node.Children.AddRange(ToTree(item.Group));
                nodes.Add(node);
            }
        }

        return nodes;
    }

    // Drops repositories that are no longer in the list and adds new ones at the end of the top level
    public static void Reconcile(LayoutGroup root, IReadOnlyCollection<string> repositories)
    {
        HashSet<string> known = new HashSet<string>(repositories.Select(Normalize), PathComparer);
        RemoveWhere(root, path => !known.Contains(Normalize(path)));

        HashSet<string> placed = new HashSet<string>(AllRepositories(root).Select(Normalize), PathComparer);
        foreach (string repository in repositories)
        {
            if (placed.Add(Normalize(repository)))
                root.Items.Add(LayoutItem.ForRepository(repository));
        }
    }

    public static IEnumerable<string> AllRepositories(LayoutGroup group)
    {
        foreach (LayoutItem item in group.Items)
        {
            if (item.Repository != null)
                yield return item.Repository;
            else if (item.Group != null)
            {
                foreach (string repository in AllRepositories(item.Group))
                    yield return repository;
            }
        }
    }

    public static IEnumerable<string> AllRepositories(RepositoryTreeNode node)
    {
        if (node.RepositoryPath != null)
        {
            yield return node.RepositoryPath;
            yield break;
        }

        foreach (RepositoryTreeNode child in node.Children)
        {
            foreach (string repository in AllRepositories(child))
                yield return repository;
        }
    }

    public static IEnumerable<LayoutGroup> AllGroups(LayoutGroup group)
    {
        foreach (LayoutItem item in group.Items)
        {
            if (item.Group == null)
                continue;

            yield return item.Group;
            foreach (LayoutGroup nested in AllGroups(item.Group))
                yield return nested;
        }
    }

    public static LayoutGroup? FindParent(LayoutGroup root, LayoutItem target)
    {
        if (root.Items.Contains(target))
            return root;

        foreach (LayoutItem item in root.Items)
        {
            if (item.Group == null)
                continue;

            LayoutGroup? parent = FindParent(item.Group, target);
            if (parent != null)
                return parent;
        }

        return null;
    }

    public static LayoutItem? FindRepository(LayoutGroup root, string path)
    {
        foreach (LayoutItem item in root.Items)
        {
            if (item.Repository != null && PathComparer.Equals(Normalize(item.Repository), Normalize(path)))
                return item;

            if (item.Group != null && FindRepository(item.Group, path) is LayoutItem found)
                return found;
        }

        return null;
    }

    public static LayoutItem? FindGroupFromFolder(LayoutGroup root, string folder)
    {
        foreach (LayoutItem item in root.Items)
        {
            if (item.Group == null)
                continue;

            if (item.Group.SourceFolder != null && PathComparer.Equals(item.Group.SourceFolder, folder))
                return item;

            if (FindGroupFromFolder(item.Group, folder) is LayoutItem found)
                return found;
        }

        return null;
    }

    // Moves an entry one place up or down among its siblings; returns false at either end
    public static bool Move(LayoutGroup root, LayoutItem item, int direction)
    {
        LayoutGroup? parent = FindParent(root, item);
        if (parent == null)
            return false;

        int index = parent.Items.IndexOf(item);
        int target = index + Math.Sign(direction);
        if (target < 0 || target >= parent.Items.Count)
            return false;

        parent.Items.RemoveAt(index);
        parent.Items.Insert(target, item);
        return true;
    }

    // Moves an entry to the end of another group, or of the top level when target is null
    public static bool MoveInto(LayoutGroup root, LayoutItem item, LayoutGroup? target)
    {
        LayoutGroup destination = target ?? root;
        LayoutGroup? parent = FindParent(root, item);
        if (parent == null || parent == destination)
            return false;

        // A group can't go inside itself or one of its own groups
        if (item.Group != null && (item.Group == destination || AllGroups(item.Group).Contains(destination)))
            return false;

        parent.Items.Remove(item);
        destination.Items.Add(item);
        return true;
    }

    public static LayoutItem AddGroup(LayoutGroup parent, string name)
    {
        LayoutItem item = LayoutItem.ForGroup(new LayoutGroup { Name = name });
        parent.Items.Add(item);
        return item;
    }

    // Removes a group but keeps what was in it, in its place
    public static bool RemoveGroup(LayoutGroup root, LayoutItem item)
    {
        LayoutGroup? parent = FindParent(root, item);
        if (parent == null || item.Group == null)
            return false;

        int index = parent.Items.IndexOf(item);
        parent.Items.RemoveAt(index);
        parent.Items.InsertRange(index, item.Group.Items);
        return true;
    }

    public static void RemoveRepository(LayoutGroup root, string path)
    {
        RemoveWhere(root, repository => PathComparer.Equals(Normalize(repository), Normalize(path)));
    }

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        return full.Length > (root?.Length ?? 0) ? full.TrimEnd('/', '\\') : full;
    }

    private static void AddNodes(LayoutGroup group, IEnumerable<RepositoryTreeNode> nodes)
    {
        foreach (RepositoryTreeNode node in nodes)
        {
            if (node.RepositoryPath != null)
            {
                group.Items.Add(LayoutItem.ForRepository(node.RepositoryPath));
                continue;
            }

            LayoutGroup child = new LayoutGroup { Name = node.Name, SourceFolder = node.Folder };
            AddNodes(child, node.Children);
            group.Items.Add(LayoutItem.ForGroup(child));
        }
    }

    private static void RemoveWhere(LayoutGroup group, Func<string, bool> predicate)
    {
        group.Items.RemoveAll(item => item.Repository != null && predicate(item.Repository));
        foreach (LayoutItem item in group.Items)
        {
            if (item.Group != null)
                RemoveWhere(item.Group, predicate);
        }
    }

    private static void Sort(List<RepositoryTreeNode> nodes)
    {
        nodes.Sort((first, second) => StringComparer.OrdinalIgnoreCase.Compare(first.Name, second.Name));
        foreach (RepositoryTreeNode node in nodes)
            Sort(node.Children);
    }

    private static IEnumerable<string> RelativeSegments(string ancestor, string folder)
    {
        string relative = Path.GetRelativePath(ancestor, folder);
        if (relative == ".")
            return Array.Empty<string>();

        return relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FolderName(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd('/', '\\'));
        return name.Length > 0 ? name : folder;
    }
}
