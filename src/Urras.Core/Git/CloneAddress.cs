namespace Urras.Core.Git;

// What the clone box accepts: a full address, or owner/name for GitHub, as GitHub Desktop does
public static class CloneAddress
{
    public const string GITHUB_WEB = "https://github.com/";
    private const string GIT_SUFFIX = ".git";

    // "azerothcore/wiki" and "github.com/azerothcore/wiki" become GitHub addresses; anything else is kept
    public static string Resolve(string input)
    {
        string text = input.Trim();
        if (text.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
            return "https://" + text;

        string[] parts = text.Split('/');
        bool ownerAndName = parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(IsNameCharacter));
        return ownerAndName && !Directory.Exists(text) ? GITHUB_WEB + text : text;
    }

    // The folder a clone goes into: the last part of the address, without .git
    public static string FolderName(string address)
    {
        string name = address.Trim().TrimEnd('/', '\\');
        name = name[(name.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1)..];
        return name.EndsWith(GIT_SUFFIX, StringComparison.OrdinalIgnoreCase) ? name[..^GIT_SUFFIX.Length] : name;
    }

    // GitHub owners and repositories use letters, digits, - _ and .
    private static bool IsNameCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';
    }
}
