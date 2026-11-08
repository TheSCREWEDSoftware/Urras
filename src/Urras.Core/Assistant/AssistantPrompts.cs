using System.Text;
using Urras.Core.Git;

namespace Urras.Core.Assistant;

// What Urras asks the assistant for, and how its answers are read back
public static class AssistantPrompts
{
    public static string CommitMessageInstructions(string? template)
    {
        StringBuilder text = new StringBuilder(
            "You write Git commit messages. Reply with the message only: a summary line of at most 72 " +
            "characters in the imperative mood without a full stop, then a blank line, then a short body " +
            "explaining what changed and why, wrapped at 72 characters. No code fences, no quotes.");
        if (!string.IsNullOrWhiteSpace(template))
            text.Append("\n\nFollow this commit message template (lines starting with # are guidance):\n")
                .Append(template);
        return text.ToString();
    }

    public static string CommitMessageInput(string stagedDiff, IEnumerable<string> recentSubjects)
    {
        StringBuilder text = new StringBuilder("Recent commit summaries, for style:\n");
        foreach (string subject in recentSubjects.Take(10))
            text.Append("- ").Append(subject).Append('\n');
        return text.Append("\nStaged changes:\n").Append(stagedDiff).ToString();
    }

    public const string EXPLAIN_INSTRUCTIONS =
        "Explain this Git commit for a developer reading the history: what it changes and why it likely " +
        "matters, in a few short paragraphs of plain text. Mention anything that looks risky.";

    public const string CONFLICT_INSTRUCTIONS =
        "You resolve Git merge conflicts. Given the current side, the incoming side and the surrounding lines, " +
        "reply with only the lines that should replace the conflict, keeping both sides' intent where they " +
        "don't contradict. No explanations, no code fences, no conflict markers.";

    public static string ConflictInput(string path, ConflictBlock block, IEnumerable<string> before,
                                       IEnumerable<string> after)
    {
        StringBuilder text = new StringBuilder("File: ").Append(path).Append("\n\nLines before:\n");
        AppendLines(text, before);
        text.Append("\nCurrent side:\n");
        AppendLines(text, block.Ours!);
        if (block.Base != null)
        {
            text.Append("\nCommon ancestor:\n");
            AppendLines(text, block.Base);
        }

        text.Append("\nIncoming side:\n");
        AppendLines(text, block.Theirs!);
        text.Append("\nLines after:\n");
        AppendLines(text, after);
        return text.ToString();
    }

    private static void AppendLines(StringBuilder text, IEnumerable<string> lines)
    {
        foreach (string line in lines)
            text.Append(line.TrimEnd('\r')).Append('\n');
    }

    // Models sometimes wrap answers in a code fence despite being asked not to
    public static string Unfence(string answer)
    {
        string text = answer.Trim();
        if (!text.StartsWith("```"))
            return text;

        int firstLine = text.IndexOf('\n');
        int close = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine > 0 && close > firstLine ? text[(firstLine + 1)..close].TrimEnd() : text.Trim('`');
    }
}
