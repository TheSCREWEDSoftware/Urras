namespace Urras.Core.Git;

// Which branches the branch box offers; the rest stay a search away (Quick Launch, All branches…)
public static class BranchLists
{
    public static readonly string[] MainRemotes = { "origin", "upstream" };

    // Local branches, then origin's and upstream's without a local twin; others (pull request remotes) left out
    public static List<BranchInfo> ForPicker(IEnumerable<BranchInfo> branches)
    {
        List<BranchInfo> all = branches.ToList();
        HashSet<string> local = all.Where(branch => !branch.IsRemote).Select(branch => branch.Name)
                                   .ToHashSet(StringComparer.Ordinal);
        IEnumerable<BranchInfo> remote = all.Where(branch => branch.IsRemote && IsMainRemote(branch.Name) &&
                                                             !local.Contains(LocalName(branch.Name)));
        return all.Where(branch => !branch.IsRemote).Concat(remote).ToList();
    }

    // "feature" for "origin/feature"
    public static string LocalName(string remoteBranch)
    {
        int slash = remoteBranch.IndexOf('/');
        return slash >= 0 ? remoteBranch[(slash + 1)..] : remoteBranch;
    }

    private static bool IsMainRemote(string remoteBranch)
    {
        int slash = remoteBranch.IndexOf('/');
        return slash > 0 && MainRemotes.Contains(remoteBranch[..slash], StringComparer.Ordinal);
    }

    // Default branch, recently used ones, then the rest, like GitHub Desktop; a filter searches every remote instead
    public static List<BranchSection> Sections(IReadOnlyList<BranchInfo> branches, string? defaultBranch,
                                               IReadOnlyList<string> recent, string? filter)
    {
        string text = filter?.Trim() ?? string.Empty;
        if (text.Length > 0)
        {
            List<BranchInfo> matches = branches.Where(branch => branch.Name.Contains(text,
                                                                                    StringComparison.OrdinalIgnoreCase))
                                               .OrderBy(branch => branch.IsRemote)
                                               .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)
                                               .ToList();
            return matches.Count == 0 ? new List<BranchSection>()
                                      : new List<BranchSection> { new BranchSection("Matching branches", matches) };
        }

        List<BranchInfo> offered = ForPicker(branches);
        List<BranchSection> sections = new List<BranchSection>();
        BranchInfo? main = offered.FirstOrDefault(branch => !branch.IsRemote && branch.Name == defaultBranch) ??
                           offered.FirstOrDefault(branch => branch.IsRemote && LocalName(branch.Name) == defaultBranch);
        if (main != null)
            sections.Add(new BranchSection("Default branch", new List<BranchInfo> { main }));

        List<BranchInfo> recentBranches = recent.Select(name => offered.FirstOrDefault(branch => !branch.IsRemote &&
                                                                                           branch.Name == name))
                                                .OfType<BranchInfo>().Where(branch => branch != main)
                                                .Distinct().Take(RECENT_COUNT).ToList();
        if (recentBranches.Count > 0)
            sections.Add(new BranchSection("Recent branches", recentBranches));

        List<BranchInfo> others = offered.Where(branch => branch != main && !recentBranches.Contains(branch))
                                         .OrderBy(branch => branch.IsRemote)
                                         .ThenBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (others.Count > 0)
            sections.Add(new BranchSection("Other branches", others));
        return sections;
    }

    public const int RECENT_COUNT = 5;
}

// A titled group of branches in the picker
public sealed record BranchSection(string Title, List<BranchInfo> Branches);
