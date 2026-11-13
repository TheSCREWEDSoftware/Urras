namespace Urras.Core.Git;

// How far a reset goes: keep the changes staged, keep them unstaged, or throw them away
public enum ResetMode
{
    Soft,
    Mixed,
    Hard
}

// A remote and where it points, for Repository settings
public sealed record RemoteInfo(string Name, string Url);

// Undo and reset, renaming branches, .gitignore entries and remotes
public sealed partial class GitRepository
{
    private const string GITIGNORE = ".gitignore";

    // The commit HEAD is on, or null in a repository with no commits yet
    public async Task<string?> GetHeadAsync()
    {
        GitResult result = await Run("rev-parse", "--verify", "-q", "HEAD");
        return result.Success ? result.Output.Trim() : null;
    }

    // Takes back the last commit; its changes stay staged, ready to commit again
    public async Task<GitResult> UndoLastCommitAsync()
    {
        GitResult parent = await Run("rev-parse", "--verify", "-q", "HEAD~1");
        // The first commit has no parent to go back to, so the branch is emptied instead; the files stay staged
        return parent.Success ? await Run("reset", "--soft", "HEAD~1") : await Run("update-ref", "-d", "HEAD");
    }

    // Moves the current branch to a commit; Hard also throws away uncommitted changes
    public Task<GitResult> ResetAsync(string hash, ResetMode mode)
    {
        string option = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            _ => "--mixed"
        };
        return Run("reset", "-q", option, hash);
    }

    // Renames a local branch; with renameRemote, its branch on the remote is renamed too and tracked again
    public async Task<GitResult> RenameBranchAsync(string oldName, string newName, bool renameRemote)
    {
        if (RefNames.Refuse(newName) is { } refused)
            return refused;
        GitResult valid = await Run("check-ref-format", "--branch", newName);
        if (!valid.Success)
            return new GitResult(-1, string.Empty, "\"" + newName + "\" can't be a branch name.");

        string? upstream = (await Run("rev-parse", "--abbrev-ref", oldName + "@{upstream}")).Output.Trim();
        GitResult renamed = await Run("branch", "-m", oldName, newName);
        if (!renamed.Success || !renameRemote || string.IsNullOrEmpty(upstream) || !upstream.Contains('/'))
            return renamed;

        string remote = upstream[..upstream.IndexOf('/')];
        string remoteBranch = upstream[(upstream.IndexOf('/') + 1)..];
        GitResult pushed = await Run("push", "-u", remote, newName);
        if (!pushed.Success)
            return pushed;
        return await Run("push", remote, "--delete", remoteBranch);
    }

    // Adds a line to the .gitignore at the top of the repository, unless it's there already
    public async Task<GitResult> AddToGitIgnoreAsync(string pattern)
    {
        string file = System.IO.Path.Combine(Path, GITIGNORE);
        try
        {
            string existing = File.Exists(file) ? await File.ReadAllTextAsync(file) : string.Empty;
            if (existing.Replace("\r\n", "\n").Split('\n').Any(line => line.Trim() == pattern))
                return new GitResult(0, string.Empty, string.Empty);

            // Keep the file's own line endings, and start on a new line
            string newline = existing.Contains("\r\n") ? "\r\n" : "\n";
            string separator = existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : newline;
            await File.AppendAllTextAsync(file, separator + pattern + newline);
            return new GitResult(0, string.Empty, string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new GitResult(-1, string.Empty, "Couldn't write " + GITIGNORE + ": " + exception.Message);
        }
    }

    // The patterns that ignore a file, its kind of file, or its folder, for the right-click menu
    public static (string File, string? Extension, string? Folder) IgnorePatterns(string path)
    {
        string normal = path.Replace('\\', '/');
        string extension = System.IO.Path.GetExtension(normal);
        int slash = normal.LastIndexOf('/');
        return ("/" + normal, extension.Length > 1 ? "*" + extension : null,
                slash > 0 ? "/" + normal[..slash] + "/" : null);
    }

    // Every remote with the address it fetches from
    public async Task<List<RemoteInfo>> GetRemoteListAsync()
    {
        GitResult result = await Run("remote", "-v");
        List<RemoteInfo> remotes = new List<RemoteInfo>();
        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "origin", a tab, the address, then "(fetch)"; the push line repeats it
            string[] parts = line.Split('\t', ' ');
            if (parts.Length >= 2 && line.TrimEnd().EndsWith("(fetch)", StringComparison.Ordinal))
                remotes.Add(new RemoteInfo(parts[0], parts[1]));
        }

        return remotes;
    }

    public Task<GitResult> AddRemoteAsync(string name, string url)
    {
        return RefNames.Refuse(name) != null || url.TrimStart().StartsWith('-')
            ? Task.FromResult(new GitResult(-1, string.Empty, "That isn't a usable remote name or address."))
            : Run("remote", "add", "--", name, url.Trim());
    }

    public Task<GitResult> RenameRemoteAsync(string name, string newName)
    {
        return RefNames.Refuse(newName) is { } refused ? Task.FromResult(refused)
                                                       : Run("remote", "rename", name, newName);
    }

    public Task<GitResult> SetRemoteUrlAsync(string name, string url)
    {
        return url.TrimStart().StartsWith('-')
            ? Task.FromResult(new GitResult(-1, string.Empty, "That isn't a usable address: " + url))
            : Run("remote", "set-url", "--", name, url.Trim());
    }

    public Task<GitResult> RemoveRemoteAsync(string name)
    {
        return Run("remote", "remove", name);
    }
}
