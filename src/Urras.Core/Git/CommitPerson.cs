using System.Text.RegularExpressions;

namespace Urras.Core.Git;

// Someone who wrote a commit: its author, or a co-author named in a "Co-authored-by:" line
public sealed partial record CommitPerson(string Name, string Email)
{
    // Most avatars drawn for one commit; GitHub Desktop stops at three too
    public const int MAX_PICTURES = 3;
    // Between co-authors in the log field, as asked of git with %(trailers:separator=)
    public const char SEPARATOR = '\x1d';

    [GeneratedRegex(@"^\s*(?<name>.*?)\s*<(?<email>[^>]*)>\s*$")]
    private static partial Regex PersonPattern();

    // "Name <email>" lines, without anyone listed twice or the author again
    public static List<CommitPerson> ParseCoAuthors(string field, string authorEmail)
    {
        List<CommitPerson> people = new List<CommitPerson>();
        foreach (string value in field.Split(SEPARATOR, StringSplitOptions.RemoveEmptyEntries))
        {
            Match match = PersonPattern().Match(value);
            CommitPerson person = match.Success
                ? new CommitPerson(match.Groups["name"].Value, match.Groups["email"].Value.Trim())
                : new CommitPerson(value.Trim(), string.Empty);
            if (person.Name.Length == 0 && person.Email.Length == 0)
                continue;
            bool known = person.Email.Length > 0 &&
                         (person.Email.Equals(authorEmail, StringComparison.OrdinalIgnoreCase) ||
                          people.Any(other => other.Email.Equals(person.Email, StringComparison.OrdinalIgnoreCase)));
            if (!known)
                people.Add(person);
        }

        return people;
    }

    // The name shown for several people: "Crow", "Crow, Claude" or "3 people", as GitHub Desktop writes it
    public static string Describe(IReadOnlyList<CommitPerson> people)
    {
        return people.Count switch
        {
            0 => string.Empty,
            1 => people[0].Name,
            2 => people[0].Name + ", " + people[1].Name,
            _ => people.Count + " people"
        };
    }

    public override string ToString()
    {
        return Email.Length == 0 ? Name : Name + " <" + Email + ">";
    }
}
