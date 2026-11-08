namespace Urras.Core.Git;

public enum HistorySearch
{
    Message,
    Author,
    // A file or folder; a single file is followed through renames
    Path,
    // Commits that added or removed the text
    Content
}

// Narrows the history to matching commits
public sealed class HistoryFilter
{
    public HistoryFilter(HistorySearch search, string text)
    {
        Search = search;
        Text = text;
    }

    public HistorySearch Search { get; }
    public string Text { get; }

    public string Description => Search switch
    {
        HistorySearch.Message => "Commits whose message contains \"" + Text + "\"",
        HistorySearch.Author => "Commits by \"" + Text + "\"",
        HistorySearch.Path => "History of " + Text,
        _ => "Commits that add or remove \"" + Text + "\""
    };

    public IEnumerable<string> ToArguments(string repositoryPath)
    {
        switch (Search)
        {
            case HistorySearch.Message:
                return new[] { "-i", "--grep=" + Text };
            case HistorySearch.Author:
                return new[] { "-i", "--author=" + Text };
            case HistorySearch.Path:
            {
                // --follow only works for one file, so folders are listed without it
                bool isFile = File.Exists(Path.Combine(repositoryPath, Text));
                return isFile ? new[] { "--follow", "--", Text } : new[] { "--", Text };
            }
            default:
                return new[] { "-S" + Text };
        }
    }
}

// One line of `git blame`
public sealed class BlameLine
{
    public BlameLine(int number, string hash, string author, DateTimeOffset date, string summary, string text)
    {
        Number = number;
        Hash = hash;
        Author = author;
        Date = date;
        Summary = summary;
        Text = text;
    }

    public int Number { get; }
    public string Hash { get; }
    public string ShortHash => Hashes.Short(Hash);
    public string Author { get; }
    public DateTimeOffset Date { get; }
    public string DateText => Urras.Core.Settings.DisplayFormats.Date(Date);
    public string Summary { get; }
    public string Text { get; }

    // Lines not committed yet are blamed on the all-zero hash
    public bool IsUncommitted => Hash.All(character => character == '0');

    // Alternates per block of lines from the same commit, so the blame view can shade blocks
    public bool IsShaded { get; set; }
}

public sealed class ReflogEntry
{
    public ReflogEntry(string hash, string shortHash, string selector, string action, DateTimeOffset date)
    {
        Hash = hash;
        ShortHash = shortHash;
        Selector = selector;
        Action = action;
        Date = date;
    }

    public string Hash { get; }
    public string ShortHash { get; }

    // "HEAD@{3}"
    public string Selector { get; }

    // "commit: Add readme", "checkout: moving from main to feature"
    public string Action { get; }
    public DateTimeOffset Date { get; }

    public override string ToString()
    {
        return ShortHash + "  " + Selector + "  " + Action + "  (" + Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm") +
               ")";
    }
}
