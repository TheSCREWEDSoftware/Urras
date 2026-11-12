namespace Urras.Core.Git;

// Lines added and removed in git's diff output, and GitHub's five squares showing the mix
public static class DiffStats
{
    public const int SQUARES = 5;

    public static (int Added, int Removed) Count(string? diff)
    {
        if (string.IsNullOrEmpty(diff))
            return (0, 0);

        int added = 0;
        int removed = 0;
        bool inHunk = false;
        foreach (string line in diff.Split('\n'))
        {
            // File headers (+++ and ---) come before the first hunk of each file
            if (line.StartsWith("diff ", StringComparison.Ordinal))
                inHunk = false;
            else if (line.StartsWith("@@", StringComparison.Ordinal))
                inHunk = true;
            else if (inHunk && line.StartsWith('+'))
                ++added;
            else if (inHunk && line.StartsWith('-'))
                ++removed;
        }

        return (added, removed);
    }

    // How many of the five squares are green and red; with fewer changed lines than squares, the rest stay empty
    public static (int Green, int Red) Squares(int added, int removed)
    {
        int total = added + removed;
        if (total == 0)
            return (0, 0);
        int filled = Math.Min(SQUARES, total);
        int green = (int)Math.Round(filled * (double)added / total, MidpointRounding.AwayFromZero);
        // A side with any change keeps at least one square
        if (added > 0 && green == 0)
            green = 1;
        if (removed > 0 && green == filled)
            green = filled - 1;
        return (green, filled - green);
    }
}
