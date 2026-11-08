using System.Text.RegularExpressions;

namespace Urras.Core.Git;

public enum LineEndingPolicy
{
    // No rule, or a rule Urras doesn't recognise; Urras leaves it alone
    Custom,
    Lf,
    Crlf,

    // LF everywhere except Windows batch scripts, which need CRLF to run
    LfWithWindowsScripts,

    // Git picks: text files are stored as LF and checked out with the system's endings
    Auto
}

// Reads and writes the line-ending rule of a repository's .gitattributes and .editorconfig
public static partial class LineEndingRules
{
    public const string GIT_ATTRIBUTES_FILE = ".gitattributes";
    public const string EDITOR_CONFIG_FILE = ".editorconfig";
    private const string MANAGED_COMMENT = "# Line endings";

    [GeneratedRegex(@"^\*\s+-?text\b")]
    private static partial Regex AllFilesRuleRegex();

    [GeneratedRegex(@"^\*\.(bat|cmd)\s+text\b")]
    private static partial Regex ScriptRuleRegex();

    [GeneratedRegex(@"^\[\*\.(\{bat,cmd\}|\{cmd,bat\}|bat|cmd)\]$")]
    private static partial Regex ScriptSectionRegex();

    // Comments that only described the rules being replaced
    private static readonly HashSet<string> ReplacedComments = new HashSet<string>
    {
        MANAGED_COMMENT,
        "# Auto detect text files and perform LF normalization",
        "# Windows batch scripts need CRLF to run"
    };

    public static LineEndingPolicy Detect(string? gitAttributes)
    {
        if (gitAttributes == null)
            return LineEndingPolicy.Custom;

        string[] lines = Lines(gitAttributes).Select(line => line.Trim()).ToArray();
        string? all = lines.FirstOrDefault(line => AllFilesRuleRegex().IsMatch(line));
        bool scriptsCrlf = lines.Any(line => ScriptRuleRegex().IsMatch(line) && line.Contains("eol=crlf"));

        if (all == null)
            return LineEndingPolicy.Custom;
        if (all.Contains("eol=lf"))
            return scriptsCrlf ? LineEndingPolicy.LfWithWindowsScripts : LineEndingPolicy.Lf;
        if (all.Contains("eol=crlf"))
            return LineEndingPolicy.Crlf;
        return all.Contains("text=auto") ? LineEndingPolicy.Auto : LineEndingPolicy.Custom;
    }

    // Replaces the line-ending rules at the top of .gitattributes and keeps every other line
    public static string ApplyToGitAttributes(string? existing, LineEndingPolicy policy)
    {
        List<string> rules = policy switch
        {
            LineEndingPolicy.Lf => new List<string> { "* text=auto eol=lf" },
            LineEndingPolicy.Crlf => new List<string> { "* text=auto eol=crlf" },
            LineEndingPolicy.LfWithWindowsScripts => new List<string>
            {
                "* text=auto eol=lf", "*.bat text eol=crlf", "*.cmd text eol=crlf"
            },
            LineEndingPolicy.Auto => new List<string> { "* text=auto" },
            _ => new List<string>()
        };

        List<string> kept = Lines(existing ?? string.Empty)
            .Where(line => !AllFilesRuleRegex().IsMatch(line.Trim()) && !ScriptRuleRegex().IsMatch(line.Trim()) &&
                           !ReplacedComments.Contains(line.Trim()))
            .ToList();
        TrimBlankLines(kept);

        List<string> result = new List<string>();
        if (rules.Count > 0)
        {
            result.Add(MANAGED_COMMENT);
            result.AddRange(rules);
            if (kept.Count > 0)
                result.Add(string.Empty);
        }

        result.AddRange(kept);
        return string.Join("\n", result) + "\n";
    }

    // Sets end_of_line in the [*] section, and in a [*.{bat,cmd}] section for Windows scripts
    public static string ApplyToEditorConfig(string? existing, LineEndingPolicy policy)
    {
        string? all = policy switch
        {
            LineEndingPolicy.Lf or LineEndingPolicy.LfWithWindowsScripts => "lf",
            LineEndingPolicy.Crlf => "crlf",
            _ => null
        };

        List<string> lines = existing == null ? new List<string> { "root = true" } : Lines(existing).ToList();
        TrimBlankLines(lines);
        if (all == null)
            return string.Join("\n", lines) + "\n";

        SetKey(lines, "[*]", all);
        if (policy == LineEndingPolicy.LfWithWindowsScripts)
        {
            string section = lines.FirstOrDefault(line => ScriptSectionRegex().IsMatch(line.Trim())) ?? "[*.{bat,cmd}]";
            SetKey(lines, section, "crlf");
        }
        else
        {
            // Without the script exception, batch files follow the [*] rule
            foreach (string section in lines.Where(line => ScriptSectionRegex().IsMatch(line.Trim())).ToList())
                RemoveKey(lines, section);
        }

        return string.Join("\n", lines) + "\n";
    }

    public static async Task<LineEndingPolicy> ReadAsync(string repositoryPath)
    {
        string path = Path.Combine(repositoryPath, GIT_ATTRIBUTES_FILE);
        return Detect(File.Exists(path) ? await File.ReadAllTextAsync(path) : null);
    }

    public static async Task WriteAsync(string repositoryPath, LineEndingPolicy policy, bool editorConfig)
    {
        if (policy == LineEndingPolicy.Custom)
            return;

        string attributesPath = Path.Combine(repositoryPath, GIT_ATTRIBUTES_FILE);
        string? attributes = File.Exists(attributesPath) ? await File.ReadAllTextAsync(attributesPath) : null;
        await File.WriteAllTextAsync(attributesPath, ApplyToGitAttributes(attributes, policy));

        if (!editorConfig)
            return;

        string configPath = Path.Combine(repositoryPath, EDITOR_CONFIG_FILE);
        string? config = File.Exists(configPath) ? await File.ReadAllTextAsync(configPath) : null;
        await File.WriteAllTextAsync(configPath, ApplyToEditorConfig(config, policy));
    }

    private static void SetKey(List<string> lines, string section, string value)
    {
        int start = lines.FindIndex(line => line.Trim() == section);
        if (start < 0)
        {
            lines.Add(string.Empty);
            lines.Add(section);
            lines.Add("end_of_line = " + value);
            return;
        }

        int end = SectionEnd(lines, start);
        int key = lines.FindIndex(start + 1, end - start - 1, IsEndOfLine);
        if (key >= 0)
            lines[key] = "end_of_line = " + value;
        else
            lines.Insert(start + 1, "end_of_line = " + value);
    }

    private static void RemoveKey(List<string> lines, string section)
    {
        int start = lines.FindIndex(line => line.Trim() == section);
        if (start < 0)
            return;

        int end = SectionEnd(lines, start);
        int key = lines.FindIndex(start + 1, end - start - 1, IsEndOfLine);
        if (key >= 0)
            lines.RemoveAt(key);
    }

    private static int SectionEnd(List<string> lines, int start)
    {
        int next = lines.FindIndex(start + 1, line => line.TrimStart().StartsWith('['));
        return next < 0 ? lines.Count : next;
    }

    private static bool IsEndOfLine(string line)
    {
        return line.Trim().StartsWith("end_of_line", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Lines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static void TrimBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && lines[0].Trim().Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);
    }
}
