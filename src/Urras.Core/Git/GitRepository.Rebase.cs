namespace Urras.Core.Git;

// Interactive rebase: rewrite the commits after a base commit
public partial class GitRepository
{
    // Commits after onto on the current branch, oldest first, as `git rebase -i` lists them
    public async Task<List<CommitInfo>> GetRebaseCommitsAsync(string onto)
    {
        GitResult result = await Run("log", "--reverse", "--topo-order", "--no-merges",
                                     "--format=" + GitParsers.LOG_FORMAT, onto + "..HEAD");
        if (!result.Success)
            throw new GitException(result.Message);
        return GitParsers.ParseLog(result.Output);
    }

    public async Task<bool> HasMergesSinceAsync(string onto)
    {
        GitResult result = await Run("rev-list", "--merges", "--count", onto + "..HEAD");
        return result.Success && result.Output.Trim() != "0";
    }

    // backupTag is created at the current commit first, so the old history can always be restored
    public async Task<GitResult> InteractiveRebaseAsync(string onto, IReadOnlyList<RebaseStep> steps,
                                                        string? backupTag)
    {
        string? problem = RebaseTodo.Validate(steps);
        if (problem != null)
            return new GitResult(1, string.Empty, problem);

        if (backupTag != null)
        {
            GitResult tag = await Run("tag", backupTag, "HEAD");
            if (!tag.Success)
                return tag;
        }

        // Messages and the todo list live in a folder of their own. A rebase stopped by a conflict still needs its
        // message files when it continues, so folders are only cleaned up a day later.
        string temp = System.IO.Path.GetTempPath();
        RemoveOldRebaseFolders(temp);
        string folder = System.IO.Path.Combine(temp, "urras-rebase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Dictionary<RebaseStep, string> files = new Dictionary<RebaseStep, string>();
        string MessageFile(RebaseStep step)
        {
            if (!files.TryGetValue(step, out string? file))
            {
                file = System.IO.Path.Combine(folder, "message-" + files.Count + ".txt");
                File.WriteAllText(file, step.Message!.Trim() + "\n");
                files[step] = file;
            }

            return file;
        }

        string todoFile = System.IO.Path.Combine(folder, "todo.txt");
        File.WriteAllText(todoFile, RebaseTodo.Build(steps, MessageFile));

        // git calls the sequence editor with the path of its own todo list; copying ours over it is all it takes
        Dictionary<string, string> environment = new Dictionary<string, string>
        {
            ["GIT_SEQUENCE_EDITOR"] = "cp \"" + todoFile.Replace('\\', '/') + "\"",
            ["GIT_EDITOR"] = "true"
        };
        return await GitRunner.RunAsync(Path, new[] { "rebase", "-i", onto }, environment: environment);
    }

    private static void RemoveOldRebaseFolders(string temp)
    {
        foreach (string old in Directory.EnumerateDirectories(temp, "urras-rebase-*"))
        {
            try
            {
                if (Directory.GetCreationTimeUtc(old) < DateTime.UtcNow.AddDays(-1))
                    Directory.Delete(old, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use or locked; try again next time
            }
        }
    }

    // A tag name like urras-backup/main-20261108-103500
    public static string BackupTagName(string? branch, DateTime now)
    {
        string name = string.IsNullOrEmpty(branch) ? "detached" : branch.Replace('/', '-');
        return "urras-backup/" + name + "-" + now.ToString("yyyyMMdd-HHmmss");
    }
}
