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
    private const string FETCH_MARK = " (fetch)";

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
        // The "--" keeps a commit from being read as a file of the same name
        return Run("reset", "-q", option, hash, "--");
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
            // "origin", a tab, the address, then " (fetch)"; the push line repeats it, and addresses can hold spaces
            string[] parts = line.TrimEnd('\r').Split('\t', 2);
            if (parts.Length == 2 && parts[1].EndsWith(FETCH_MARK, StringComparison.Ordinal))
                remotes.Add(new RemoteInfo(parts[0], parts[1][..^FETCH_MARK.Length]));
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

    // Stashes only these files, new ones included, and leaves every other change where it is
    public Task<GitResult> StashFilesAsync(IEnumerable<string> paths, string? message)
    {
        List<string> arguments = new List<string> { "stash", "push", "--include-untracked" };
        if (!string.IsNullOrWhiteSpace(message))
            arguments.AddRange(new[] { "-m", message });
        arguments.Add("--");
        arguments.AddRange(paths);
        return Run(arguments);
    }

    // Local branches worth deleting: merged into the main branch, or whose branch on the remote is gone
    public async Task<List<BranchCleanup>> GetCleanupCandidatesAsync()
    {
        string main = await GetDefaultBranchAsync();
        string target = (await Run("rev-parse", "--verify", "-q", "refs/remotes/origin/" + main)).Success
            ? "origin/" + main
            : main;
        HashSet<string> merged = (await Run("branch", "--format=%(refname:short)", "--merged", target)).Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        string current = (await Run("branch", "--show-current")).Output.Trim();

        List<BranchCleanup> candidates = new List<BranchCleanup>();
        GitResult branches = await Run("for-each-ref", "--format=%(refname:short)%1f%(upstream:track)", "refs/heads");
        foreach (string line in branches.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split(GitParsers.FIELD_SEPARATOR);
            string name = fields[0];
            bool gone = fields.Length > 1 && fields[1].Contains("gone", StringComparison.Ordinal);
            bool isMerged = merged.Contains(name);
            if (name != current && name != main && (gone || isMerged))
                candidates.Add(new BranchCleanup(name, isMerged, gone));
        }

        return candidates;
    }

    // A commit as a patch file with its message and author, as git format-patch writes it
    public async Task<GitResult> SaveCommitPatchAsync(string hash, string file)
    {
        // format-patch names its own file, so it writes into a folder of its own and the file is moved after
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                               "urras-patch-" + Guid.NewGuid().ToString("N"));
        try
        {
            GitResult result = await Run("format-patch", "-1", "--binary", "-o", folder, hash);
            if (!result.Success)
                return result;
            string made = Directory.EnumerateFiles(folder).Single();
            File.Move(made, file, true);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidOperationException)
        {
            return new GitResult(-1, string.Empty, "Couldn't save the patch: " + exception.Message);
        }
        finally
        {
            DeleteQuietly(folder);
        }
    }

    // A temporary folder that can't be removed is left for the system to clear
    private static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing to report: the patch was saved, and the system clears its temporary folder in time
        }
    }

    // Changes not yet committed (staged or not) to these files, as a patch file
    public Task<GitResult> SaveChangesPatchAsync(IEnumerable<string> paths, bool staged, string file)
    {
        List<string> arguments = new List<string> { "diff", "--binary", "--output=" + file };
        if (staged)
            arguments.Add("--cached");
        arguments.Add("--");
        arguments.AddRange(paths);
        return Run(arguments);
    }

    // A patch made by format-patch can become commits (git am); any patch can be applied as changes
    public static bool IsMailPatch(string file)
    {
        try
        {
            using StreamReader reader = new StreamReader(file);
            return reader.ReadLine()?.StartsWith("From ", StringComparison.Ordinal) == true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // As commits, a failed patch is backed out whole; as changes, a three-way merge leaves conflicts to resolve
    public async Task<GitResult> ApplyPatchAsync(string file, bool asCommits)
    {
        if (asCommits)
        {
            GitResult applied = await RunWithoutEditor("am", "--3way", file);
            if (!applied.Success)
                await Run("am", "--abort");
            return applied;
        }

        GitResult plain = await Run("apply", "--whitespace=nowarn", file);
        return plain.Success ? plain : await Run("apply", "--3way", "--whitespace=nowarn", file);
    }

    // The files exactly as they were at a commit, as a zip
    public Task<GitResult> ExportZipAsync(string hash, string file)
    {
        return Run("archive", "--format=zip", "--output=" + file, hash);
    }
}

// A branch the clean-up offers to delete, and why
public sealed record BranchCleanup(string Name, bool Merged, bool Gone)
{
    public override string ToString()
    {
        string why = Merged && Gone ? "merged, gone from the remote" : Merged ? "merged" : "gone from the remote";
        return Name + "  (" + why + ")";
    }
}
