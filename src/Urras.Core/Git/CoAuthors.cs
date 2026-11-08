using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// Someone credited on a commit with a "Co-authored-by: Name <email>" line, as GitHub and GitLab show it.
// Username is set when the person came from a signed-in account (shown as @username).
public sealed partial record CoAuthor(string Name, string Email, string? Username = null)
{
    public const string TRAILER = "Co-authored-by:";

    [GeneratedRegex(@"^\s*(?<name>[^<>]*?)\s*<(?<email>[^<>\s]+@[^<>\s]+)>\s*$")]
    private static partial Regex PersonRegex();

    public string Trailer => TRAILER + " " + Name + " <" + Email + ">";
    public string Label => Username != null ? "@" + Username : Name;
    public override string ToString() => Name + " <" + Email + ">";

    // "Name <email>"; null when it doesn't look like one
    public static CoAuthor? Parse(string text)
    {
        Match match = PersonRegex().Match(text);
        if (!match.Success)
            return null;

        string name = match.Groups["name"].Value.Trim();
        string email = match.Groups["email"].Value;
        return new CoAuthor(name.Length == 0 ? email.Split('@')[0] : name, email);
    }

    // GitHub's private address for a user, so the commit links to their profile without revealing their email
    public static CoAuthor FromGitHub(long id, string login, string? name)
    {
        return new CoAuthor(string.IsNullOrWhiteSpace(name) ? login : name, id + "+" + login +
                            "@users.noreply.github.com", login);
    }

    // Splits a commit message into the message itself and its co-authors
    public static (string Message, List<CoAuthor> CoAuthors) Extract(string message)
    {
        List<CoAuthor> found = new List<CoAuthor>();
        List<string> kept = new List<string>();
        foreach (string line in message.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith(TRAILER, StringComparison.OrdinalIgnoreCase)
                && Parse(line.TrimStart()[TRAILER.Length..]) is { } person)
                found.Add(person);
            else
                kept.Add(line);
        }

        List<CoAuthor> unique = found.DistinctBy(person => person.Email.ToLowerInvariant()).ToList();
        return (string.Join('\n', kept).TrimEnd(), unique);
    }

    // The message with a trailer for each co-author, after a blank line as git expects
    public static string Append(string message, IEnumerable<CoAuthor> coAuthors)
    {
        List<string> trailers = coAuthors.DistinctBy(person => person.Email.ToLowerInvariant())
                                         .Select(person => person.Trailer).ToList();
        if (trailers.Count == 0)
            return message;

        return message.TrimEnd() + "\n\n" + string.Join('\n', trailers);
    }
}
