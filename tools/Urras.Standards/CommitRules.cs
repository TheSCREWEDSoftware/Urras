using System.Text.RegularExpressions;

namespace Urras.Standards;

// The commit message format from .git_commit_template.txt
public static partial class CommitRules
{
    public const int MAX_LINE_LENGTH = 72;

    public static readonly string[] Types = { "feat", "fix", "refactor", "style", "docs", "test", "chore" };
    public static readonly string[] Scopes = { "Core", "Git", "UI", "Settings", "Build", "Docs", "Tests" };

    [GeneratedRegex(@"^(?<type>[a-z]+)\((?<scope>[A-Za-z]+)(?:/[A-Za-z0-9]+)*\): (?<subject>.+)$")]
    private static partial Regex TitleRegex();

    // Trailers such as Co-authored-by: are exempt from the body's line length
    [GeneratedRegex(@"^[A-Za-z-]+: ")]
    private static partial Regex TrailerRegex();

    public static List<Violation> Check(string commit, string message)
    {
        List<Violation> violations = new List<Violation>();
        string[] lines = message.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        string title = lines[0];

        Match match = TitleRegex().Match(title);
        if (!match.Success)
            violations.Add(new Violation(commit, 1, "commit-title",
                                         "Write the title as type(Scope): Summary, such as " +
                                             "fix(Git/Status): Paths with spaces no longer break the list."));
        else
        {
            if (!Types.Contains(match.Groups["type"].Value))
                violations.Add(new Violation(commit, 1, "commit-title",
                                             "Type must be one of " + string.Join(", ", Types) + "."));
            if (!Scopes.Contains(match.Groups["scope"].Value))
                violations.Add(new Violation(commit, 1, "commit-title",
                                             "Scope must start with one of " + string.Join(", ", Scopes) + "."));

            string subject = match.Groups["subject"].Value;
            if (!char.IsUpper(subject[0]))
                violations.Add(new Violation(commit, 1, "commit-title", "Start the summary with a capital letter."));
            if (subject.EndsWith('.'))
                violations.Add(new Violation(commit, 1, "commit-title", "No full stop at the end of the title."));
        }

        if (title.Length > MAX_LINE_LENGTH)
            violations.Add(new Violation(commit, 1, "commit-title",
                                         $"Title is {title.Length} characters; keep it to {MAX_LINE_LENGTH}."));
        if (lines.Length > 1 && lines[1].Length > 0)
            violations.Add(new Violation(commit, 2, "commit-body", "Leave a blank line after the title."));

        for (int i = 2; i < lines.Length; ++i)
        {
            if (lines[i].Length > MAX_LINE_LENGTH && !TrailerRegex().IsMatch(lines[i]) && !lines[i].Contains("://"))
                violations.Add(new Violation(commit, i + 1, "commit-body",
                                             $"Line is {lines[i].Length} characters; wrap at {MAX_LINE_LENGTH}."));
            if (lines[i].StartsWith("Claude-Session:", StringComparison.OrdinalIgnoreCase))
                violations.Add(new Violation(commit, i + 1, "commit-body", "Leave out session links."));
        }

        return violations;
    }
}
