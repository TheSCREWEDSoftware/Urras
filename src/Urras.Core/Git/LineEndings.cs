using System.Text.RegularExpressions;

namespace Urras.Core.Git;

public enum LineEnding
{
    None,
    Lf,
    Crlf,
    Mixed
}

// The line endings a file has and should have, from `git ls-files --eol`, .gitattributes and .editorconfig
public sealed class LineEndingInfo
{
    public LineEndingInfo(string path, LineEnding index, LineEnding workingTree, LineEnding expected)
    {
        Path = path;
        Index = index;
        WorkingTree = workingTree;
        Expected = expected;
    }

    public string Path { get; }
    public LineEnding Index { get; }
    public LineEnding WorkingTree { get; }
    public LineEnding Expected { get; }

    // What would be committed: mixed endings, or endings that don't match the repository's rule
    public string? StagedWarning => Warning(Index);

    // The copy on disk; Git converts it on staging when .gitattributes asks for it, so only mixed endings matter
    public string? WorkingTreeWarning => WorkingTree == LineEnding.Mixed ? "Mixed line endings" : null;

    private string? Warning(LineEnding actual)
    {
        if (actual == LineEnding.Mixed)
            return "Mixed line endings";
        if (Expected != LineEnding.None && actual != LineEnding.None && actual != Expected)
            return Name(actual) + " line endings, but this repository expects " + Name(Expected);
        return null;
    }

    public static string Name(LineEnding ending)
    {
        return ending switch
        {
            LineEnding.Lf => "LF",
            LineEnding.Crlf => "CRLF",
            LineEnding.Mixed => "mixed",
            _ => "no"
        };
    }
}

public static partial class LineEndings
{
    [GeneratedRegex(@"\beol=(lf|crlf)\b")]
    private static partial Regex AttributeEolRegex();

    private const int PATHS_PER_COMMAND = 100;

    // Only the given files are checked, usually the changed ones, so large repositories stay fast
    public static async Task<Dictionary<string, LineEndingInfo>> GetAsync(string repositoryPath,
                                                                         IReadOnlyCollection<string> paths)
    {
        Dictionary<string, LineEndingInfo> files = new Dictionary<string, LineEndingInfo>(StringComparer.Ordinal);
        if (paths.Count == 0)
            return files;

        EditorConfig editorConfig = EditorConfig.Load(System.IO.Path.Combine(repositoryPath, ".editorconfig"));

        // Paths are passed in batches to stay under the command line length limit
        foreach (string[] batch in paths.Chunk(PATHS_PER_COMMAND))
        {
            List<string> arguments = new List<string> { "ls-files", "--eol", "-z", "--cached", "--others",
                                                        "--exclude-standard", "--" };
            arguments.AddRange(batch);
            GitResult result = await GitRunner.RunAsync(repositoryPath, arguments);
            if (!result.Success)
                continue;

            foreach (LineEndingInfo info in Parse(result.Output, editorConfig))
                files[info.Path] = info;
        }

        return files;
    }

    // Lines look like "i/lf    w/crlf  attr/text=auto eol=lf \tpath", separated by NUL
    public static IEnumerable<LineEndingInfo> Parse(string output, EditorConfig editorConfig)
    {
        foreach (string entry in output.Split('\0'))
        {
            int tab = entry.IndexOf('\t');
            if (tab < 0)
                continue;

            string path = entry.Substring(tab + 1);
            string[] fields = entry.Substring(0, tab).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            LineEnding index = ParseEnding(fields, "i/");
            LineEnding workingTree = ParseEnding(fields, "w/");
            IEnumerable<string> attributeFields = fields.Where(field =>
                !field.StartsWith("i/", StringComparison.Ordinal) && !field.StartsWith("w/", StringComparison.Ordinal));
            string attributes = string.Join(' ', attributeFields);

            // Binary files have no line endings to check
            bool noText = index == LineEnding.None && workingTree == LineEnding.None;
            if (noText || attributes.Contains("-text", StringComparison.Ordinal))
                continue;

            yield return new LineEndingInfo(path, index, workingTree, Expected(attributes, path, editorConfig));
        }
    }

    private static LineEnding ParseEnding(string[] fields, string prefix)
    {
        string? field = fields.FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
        return field?.Substring(prefix.Length) switch
        {
            "lf" => LineEnding.Lf,
            "crlf" => LineEnding.Crlf,
            "mixed" => LineEnding.Mixed,
            _ => LineEnding.None
        };
    }

    private static LineEnding Expected(string attributes, string path, EditorConfig editorConfig)
    {
        Match match = AttributeEolRegex().Match(attributes);
        if (match.Success)
            return match.Groups[1].Value == "crlf" ? LineEnding.Crlf : LineEnding.Lf;

        return editorConfig.EndOfLine(path);
    }
}

// The end_of_line rules of a repository's top-level .editorconfig, with simple glob sections like [*] and [*.{bat,cmd}]
public sealed class EditorConfig
{
    private readonly List<(Regex Pattern, LineEnding Ending)> _rules = new List<(Regex, LineEnding)>();

    public static EditorConfig Load(string path)
    {
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new EditorConfig();
    }

    public static EditorConfig Parse(string text)
    {
        EditorConfig config = new EditorConfig();
        Regex? section = null;

        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = GlobToRegex(line.Substring(1, line.Length - 2));
                continue;
            }

            int equals = line.IndexOf('=');
            if (section == null || equals < 0)
                continue;

            string key = line.Substring(0, equals).Trim().ToLowerInvariant();
            string value = line.Substring(equals + 1).Trim().ToLowerInvariant();
            if (key == "end_of_line" && (value == "lf" || value == "crlf"))
                config._rules.Add((section, value == "crlf" ? LineEnding.Crlf : LineEnding.Lf));
        }

        return config;
    }

    // Later sections win, as in EditorConfig
    public LineEnding EndOfLine(string path)
    {
        string normalized = path.Replace('\\', '/');
        LineEnding ending = LineEnding.None;
        foreach ((Regex pattern, LineEnding rule) in _rules)
        {
            if (pattern.IsMatch(normalized))
                ending = rule;
        }

        return ending;
    }

    // Supports *, **, ? and {a,b}; a pattern without a slash matches the file name in any folder
    private static Regex GlobToRegex(string glob)
    {
        System.Text.StringBuilder pattern = new System.Text.StringBuilder();
        bool anyFolder = !glob.Contains('/');
        pattern.Append(anyFolder ? "(^|/)" : "^");

        for (int i = 0; i < glob.Length; ++i)
        {
            char character = glob[i];
            switch (character)
            {
                case '*':
                {
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        pattern.Append(".*");
                        ++i;
                    }
                    else
                        pattern.Append("[^/]*");
                    break;
                }
                case '?':
                    pattern.Append("[^/]");
                    break;
                case '{':
                    pattern.Append("(?:");
                    break;
                case '}':
                    pattern.Append(')');
                    break;
                case ',':
                    pattern.Append('|');
                    break;
                default:
                    pattern.Append(Regex.Escape(character.ToString()));
                    break;
            }
        }

        pattern.Append('$');
        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant);
    }
}
