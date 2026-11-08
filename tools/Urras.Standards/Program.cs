using System.Diagnostics;
using System.Text;
using Urras.Standards;

// Checks files and commit messages against the coding standards; run with --help for the options
string root = string.Empty;
root = Git("rev-parse", "--show-toplevel").Trim();
if (root.Length == 0)
{
    Console.Error.WriteLine("Run this inside the Urras repository.");
    return 2;
}

string mode = args.Length > 0 ? args[0] : "--uncommitted";
string? value = args.Length > 1 ? args[1] : null;
List<Violation> violations = new List<Violation>();
switch (mode)
{
    case "--help" or "-h":
        Console.WriteLine(HELP);
        return 0;
    case "--install-hooks":
        return InstallHooks(root);
    case "--uncommitted":
    {
        IEnumerable<string> changed = Lines(Git("diff", "--name-only", "--diff-filter=ACMR", "HEAD"))
            .Concat(Lines(Git("ls-files", "--others", "--exclude-standard")));
        CheckFiles(changed.Distinct(), staged: false);
        break;
    }
    case "--staged":
        CheckFiles(Lines(Git("diff", "--cached", "--name-only", "--diff-filter=ACMR")), staged: true);
        break;
    case "--last":
        CheckRange("HEAD~1..HEAD", Git("rev-parse", "--verify", "-q", "HEAD~1").Length == 0 ? "HEAD" : null);
        break;
    case "--unpushed":
    {
        // Against the branch's upstream; without one, every commit counts as not pushed
        bool hasUpstream = Git("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}").Trim().Length > 0;
        CheckRange("@{u}..HEAD", hasUpstream ? null : "HEAD");
        break;
    }
    case "--history":
        CheckRange("HEAD", "HEAD");
        break;
    case "--since-commit" when value != null:
        CheckRange(value + "..HEAD", null);
        break;
    case "--since-date" when value != null:
    {
        CheckCommits(Lines(Git("rev-list", "--reverse", "--no-merges", "--since=" + value, "HEAD")));
        CheckFiles(Lines(Git("log", "--since=" + value, "--name-only", "--diff-filter=ACMR", "--format=", "HEAD"))
                       .Distinct(), staged: false);
        break;
    }
    case "--commits" when value != null:
        CheckRange(value, null);
        break;
    case "--all":
        CheckFiles(Lines(Git("ls-files")), staged: false);
        break;
    case "--message" when value != null:
        violations.AddRange(CommitRules.Check("commit message", File.ReadAllText(value)));
        break;
    default:
    {
        Console.Error.WriteLine("Unknown option " + mode + ".");
        Console.Error.WriteLine(HELP);
        return 2;
    }
}

foreach (Violation violation in violations)
    Console.WriteLine(violation);
Console.WriteLine(violations.Count == 0 ? "Everything follows the coding standards."
                                        : violations.Count + (violations.Count == 1 ? " problem" : " problems") +
                                          " found. See CODING_STANDARDS.md.");
return violations.Count == 0 ? 0 : 1;

// Commits in a range and the files they changed; wholeHistory replaces a range that starts before the first commit
void CheckRange(string range, string? wholeHistory)
{
    string[] revisions = wholeHistory == null ? new[] { range } : new[] { wholeHistory };
    CheckCommits(Lines(Git(new[] { "rev-list", "--reverse", "--no-merges" }.Concat(revisions).ToArray())));
    string[] files = new[] { "log", "--name-only", "--diff-filter=ACMR", "--format=" }.Concat(revisions).ToArray();
    CheckFiles(Lines(Git(files)).Distinct(), staged: false);
}

void CheckCommits(IEnumerable<string> hashes)
{
    foreach (string hash in hashes)
    {
        string subject = Git("log", "-1", "--format=%s", hash).Trim();
        violations.AddRange(CommitRules.Check(hash[..7] + " \"" + subject + "\"",
                                              Git("log", "-1", "--format=%B", hash)));
    }
}

void CheckFiles(IEnumerable<string> paths, bool staged)
{
    foreach (string path in paths.Where(FileRules.IsChecked))
    {
        // Staged mode checks what will be committed, not what is in the working folder
        byte[]? bytes = staged ? GitBytes("show", ":" + path)
                               : File.Exists(Path.Combine(root, path)) ? File.ReadAllBytes(Path.Combine(root, path))
                                                                         : null;
        if (bytes != null)
            violations.AddRange(FileRules.Check(path, bytes));
    }
}

static IEnumerable<string> Lines(string text)
{
    return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

string Git(params string[] arguments)
{
    byte[]? bytes = GitBytes(arguments);
    return bytes == null ? string.Empty : Encoding.UTF8.GetString(bytes);
}

byte[]? GitBytes(params string[] arguments)
{
    ProcessStartInfo start = new ProcessStartInfo("git")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = root.Length > 0 ? root : Environment.CurrentDirectory
    };
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add("core.quotepath=off");
    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);

    using Process process = Process.Start(start)!;
    using MemoryStream output = new MemoryStream();
    process.StandardOutput.BaseStream.CopyTo(output);
    process.WaitForExit();
    return process.ExitCode == 0 ? output.ToArray() : null;
}

static int InstallHooks(string root)
{
    string hooks = Path.Combine(root, ".git", "hooks");
    Directory.CreateDirectory(hooks);
    const string HEADER = "#!/bin/sh\n# Installed by check-standards --install-hooks\n";
    File.WriteAllText(Path.Combine(hooks, "pre-commit"),
                      HEADER + "exec dotnet run --project tools/Urras.Standards -v q -- --staged\n");
    File.WriteAllText(Path.Combine(hooks, "commit-msg"),
                      HEADER + "exec dotnet run --project tools/Urras.Standards -v q -- --message \"$1\"\n");
    if (!OperatingSystem.IsWindows())
    {
        foreach (string hook in new[] { "pre-commit", "commit-msg" })
            File.SetUnixFileMode(Path.Combine(hooks, hook),
                                 UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                 UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                                 UnixFileMode.OtherExecute);
    }

    Console.WriteLine("Installed: every commit is checked first. Skip once with git commit --no-verify.");
    return 0;
}

partial class Program
{
    private const string HELP = """
        check-standards [option]

          --uncommitted         Changed and new files that aren't committed yet (the default)
          --staged              What is about to be committed
          --last                The last commit: its message and the files it changed
          --unpushed            Commits not pushed yet (all commits when the branch has no upstream)
          --history             Every commit's message, and every file any commit touched
          --since-commit HASH   Commits after HASH
          --since-date DATE     Commits since DATE, such as 2026-11-01 or "2 weeks ago"
          --commits A..B        Commits in a Git range
          --all                 Every tracked file as it is now
          --message FILE        One commit message
          --install-hooks       Check every commit before it is made (skip once with --no-verify)

        Files are checked as they are now in the working folder, except with --staged.
        """;
}
