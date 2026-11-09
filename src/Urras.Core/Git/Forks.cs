using Urras.Core.Settings;

namespace Urras.Core.Git;

// How a fork is used, as GitHub Desktop asks: pull requests and pages go to the parent, or to the fork itself
public enum ForkTarget
{
    // Not chosen yet; the parent is used, and Urras asks once it knows the repository is a fork
    Unset,
    Parent,
    Own
}

// What Urras knows about a repository being a fork, kept in the repository's own Git configuration
public sealed record ForkSettings(RemoteIdentity? Parent, ForkTarget Target)
{
    public const string PARENT_KEY = "urras.forkParent";
    public const string TARGET_KEY = "urras.forkTarget";
    public const string UPSTREAM = "upstream";

    // The repository pull requests, issues and "View on…" go to
    public RemoteIdentity? ContributionTarget(RemoteIdentity? origin)
    {
        return Parent != null && Target != ForkTarget.Own ? Parent : origin;
    }

    public static ForkTarget ParseTarget(string? text)
    {
        return text?.Trim().ToLowerInvariant() switch
        {
            "parent" => ForkTarget.Parent,
            "own" => ForkTarget.Own,
            _ => ForkTarget.Unset
        };
    }

    public static string? TargetText(ForkTarget target)
    {
        return target switch
        {
            ForkTarget.Parent => "parent",
            ForkTarget.Own => "own",
            _ => null
        };
    }

    // The parent as owner/name on the origin's host, or an upstream remote when nothing has been saved
    public static RemoteIdentity? ParentFrom(string? saved, RemoteIdentity? origin,
                                             IReadOnlyDictionary<string, RemoteIdentity> remotes)
    {
        string[] parts = saved?.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (parts.Length == 2 && origin != null)
            return new RemoteIdentity(parts[0], parts[1], origin.Host);
        if (remotes.TryGetValue(UPSTREAM, out RemoteIdentity? upstream) && upstream.Key != origin?.Key)
            return upstream;
        return null;
    }
}

public partial class GitRepository
{
    // The first commits of the current history; forks and clones of one project share them
    public async Task<List<string>> GetRootCommitsAsync()
    {
        GitResult roots = await Run("rev-list", "--max-parents=0", "HEAD");
        return roots.Success ? roots.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries |
                                                        StringSplitOptions.TrimEntries).ToList()
                             : new List<string>();
    }

    public async Task<ForkSettings> GetForkSettingsAsync()
    {
        GitResult parent = await Run("config", "--get", ForkSettings.PARENT_KEY);
        GitResult target = await Run("config", "--get", ForkSettings.TARGET_KEY);
        Dictionary<string, RemoteIdentity> remotes = RemoteIdentity.ReadAll(Path);
        remotes.TryGetValue(RemoteIdentity.ORIGIN, out RemoteIdentity? origin);
        return new ForkSettings(ForkSettings.ParentFrom(parent.Success ? parent.Output : null, origin, remotes),
                                ForkSettings.ParseTarget(target.Success ? target.Output : null));
    }

    // Saves the parent (owner/name) and the choice; a null parent or Unset choice removes the setting
    public async Task SaveForkSettingsAsync(RemoteIdentity? parent, ForkTarget target)
    {
        await SetOrUnset(ForkSettings.PARENT_KEY, parent == null ? null : parent.Owner + "/" + parent.Name);
        await SetOrUnset(ForkSettings.TARGET_KEY, ForkSettings.TargetText(target));
    }

    private async Task SetOrUnset(string key, string? value)
    {
        if (value == null)
            await Run("config", "--unset", key);
        else
            await Run("config", key, value);
    }
}
