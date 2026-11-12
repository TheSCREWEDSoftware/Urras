namespace Urras.Core.Git;

// Staging, unstaging and discarding single lines and hunks
public partial class GitRepository
{
    // diff is the unstaged diff the lines were chosen from
    public Task<GitResult> StageLinesAsync(string diff, IReadOnlySet<int> lines)
    {
        return ApplyAsync(PatchBuilder.Build(diff, lines, reverse: false), "--cached");
    }

    // diff is the staged diff the lines were chosen from
    public Task<GitResult> UnstageLinesAsync(string diff, IReadOnlySet<int> lines)
    {
        return ApplyAsync(PatchBuilder.Build(diff, lines, reverse: true), "--cached", "-R");
    }

    // diff is the unstaged diff; the selected lines are reverted in the working tree
    public Task<GitResult> DiscardLinesAsync(string diff, IReadOnlySet<int> lines)
    {
        // The whole file is copied first, from the "+++ b/path" line of its diff
        string? path = diff.Split('\n').Select(line => line.TrimEnd('\r'))
                           .FirstOrDefault(line => line.StartsWith("+++ b/", StringComparison.Ordinal))?[6..];
        LastDiscardBackup = path == null ? null : Urras.Core.Settings.DiscardBackup.Save(Path, new[] { path });
        return ApplyAsync(PatchBuilder.Build(diff, lines, reverse: true), "-R");
    }

    private async Task<GitResult> ApplyAsync(string? patch, params string[] options)
    {
        if (patch == null)
            return new GitResult(1, string.Empty, "Select changed lines (+ or -) first.");

        // --recount lets git count the lines of each hunk itself
        List<string> arguments = Arguments("apply", "--recount", "--whitespace=nowarn");
        arguments.AddRange(options);
        arguments.Add("-");
        return await GitRunner.RunAsync(Path, arguments, patch);
    }
}
