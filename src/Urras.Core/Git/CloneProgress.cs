namespace Urras.Core.Git;

// How far a whole clone has got, from the progress line of whichever step Git is on
public static class CloneProgress
{
    private const double FULL = 100;
    // Where each step starts and ends on the whole bar; receiving the objects takes most of a clone
    private static readonly (string Phase, double Start, double End)[] Steps =
    {
        ("Counting objects", 0, 2),
        ("Compressing objects", 2, 5),
        ("Receiving objects", 5, 80),
        ("Resolving deltas", 80, 95),
        ("Updating files", 95, 100)
    };

    // 0 to 100, or null for a step that isn't listed (the bar then stays where it was)
    public static double? Overall(GitProgress progress)
    {
        foreach ((string phase, double start, double end) in Steps)
        {
            if (string.Equals(phase, progress.Phase, StringComparison.OrdinalIgnoreCase))
                return start + (end - start) * progress.Percent / FULL;
        }

        return null;
    }
}
