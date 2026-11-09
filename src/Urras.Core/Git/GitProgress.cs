using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// One of Git's progress lines, such as "Writing objects:  45% (9/20), 1.2 MiB | 2.0 MiB/s"
public sealed partial record GitProgress(string Phase, int Percent)
{
    private const int FULL = 100;

    [GeneratedRegex(@"^(?:remote:\s*)?(?<phase>[A-Za-z][A-Za-z ]*?):\s+(?<percent>\d{1,3})%")]
    private static partial Regex ProgressPattern();

    // Null for lines that aren't progress, such as hints and errors
    public static GitProgress? Parse(string line)
    {
        Match match = ProgressPattern().Match(line.Trim());
        if (!match.Success)
            return null;
        return new GitProgress(match.Groups["phase"].Value, Math.Min(FULL, int.Parse(match.Groups["percent"].Value)));
    }

    // "Writing objects 45%"
    public override string ToString()
    {
        return Phase + " " + Percent + "%";
    }
}
