namespace Urras.Core.Git;

public static class RefNames
{
    // A typed branch or tag name that git would read as an option is refused before it reaches the command line
    public static GitResult? Refuse(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.TrimStart().StartsWith('-'))
            return new GitResult(-1, string.Empty, "That isn't a usable name: " + name);

        return null;
    }
}
