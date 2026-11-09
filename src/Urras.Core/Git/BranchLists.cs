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
}
