using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// A piece of a commit's summary; code is shown in a monospaced box, like GitHub shows `code`
public sealed record SubjectPart(string Text, bool IsCode);

// Splits a commit summary into text and code: `backticked` text, and the branch names in merge messages
public static partial class CommitSubject
{
    // Merge branch 'a' into b, Merge branch 'a' of <address> into b, Merge remote-tracking branch 'origin/a'
    [GeneratedRegex(@"^Merge (?:remote-tracking )?branch(?:es)? '(?<from>[^']+)'(?: of \S+)?(?: into (?<into>\S+))?")]
    private static partial Regex MergeBranchRegex();

    // Merge pull request #12 from owner/branch
    [GeneratedRegex(@"^Merge pull request #\d+ from (?<from>\S+)")]
    private static partial Regex MergePullRequestRegex();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex BacktickRegex();

    public static List<SubjectPart> Parts(string subject)
    {
        List<(int Start, int Length, string Code)> codes = new List<(int, int, string)>();
        Match merge = MergeBranchRegex().Match(subject);
        if (!merge.Success)
            merge = MergePullRequestRegex().Match(subject);
        if (merge.Success)
        {
            foreach (string name in new[] { "from", "into" })
            {
                Group group = merge.Groups[name];
                if (!group.Success)
                    continue;

                // The quotes around a branch name go, as the code box marks it instead
                int end = group.Index + group.Length;
                bool quoted = group.Index > 0 && subject[group.Index - 1] == '\'' && end < subject.Length &&
                              subject[end] == '\'';
                codes.Add(quoted ? (group.Index - 1, group.Length + 2, group.Value)
                                 : (group.Index, group.Length, group.Value));
            }
        }

        foreach (Match code in BacktickRegex().Matches(subject))
        {
            if (!codes.Any(existing => code.Index < existing.Start + existing.Length &&
                                       existing.Start < code.Index + code.Length))
                codes.Add((code.Index, code.Length, code.Groups[1].Value));
        }

        List<SubjectPart> parts = new List<SubjectPart>();
        int position = 0;
        foreach ((int start, int length, string text) in codes.OrderBy(code => code.Start))
        {
            if (start > position)
                parts.Add(new SubjectPart(subject[position..start], false));
            parts.Add(new SubjectPart(text, true));
            position = start + length;
        }

        if (position < subject.Length || parts.Count == 0)
            parts.Add(new SubjectPart(subject[position..], false));
        return parts;
    }
}
