using System.Globalization;

namespace Urras.Core.Git;

public sealed record ContributorStats(string Name, string Email, int Commits, int Added, int Removed);
public sealed record FileStats(string Path, int Commits, int Added, int Removed);

// Numbers about a repository's history: who committed, when, and which files change most
public sealed class RepositoryStatistics
{
    public int Commits { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }
    public DateTimeOffset? First { get; init; }
    public DateTimeOffset? Last { get; init; }
    public List<ContributorStats> Contributors { get; init; } = new();
    public List<FileStats> Files { get; init; } = new();

    // Commits per day of the week (Monday first) and per hour, in the commit's own time zone
    public int[] ByWeekday { get; init; } = new int[DAYS_IN_WEEK];
    public int[] ByHour { get; init; } = new int[HOURS_IN_DAY];

    // Commits per month as "yyyy-MM", oldest first, months without commits included
    public List<(string Month, int Commits)> ByMonth { get; init; } = new();

    public const string FORMAT = "%x1e%an%x1f%ae%x1f%aI";
    public const int TOP_FILES = 15;
    private const int DAYS_IN_WEEK = 7;
    private const int HOURS_IN_DAY = 24;
    private const string RENAME_ARROW = " => ";

    // Output of git log --no-merges --numstat with FORMAT
    public static RepositoryStatistics Parse(string output, int topFiles = TOP_FILES)
    {
        Dictionary<string, (string Name, int Commits, int Added, int Removed)> people = new();
        Dictionary<string, (int Commits, int Added, int Removed)> files = new();
        Dictionary<string, int> months = new();
        int[] weekdays = new int[DAYS_IN_WEEK];
        int[] hours = new int[HOURS_IN_DAY];
        int commits = 0;
        int added = 0;
        int removed = 0;
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;

        foreach (string record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] lines = record.Replace("\r", string.Empty).Split('\n');
            string[] header = lines[0].Split('\u001f');
            if (header.Length < 3 || !DateTimeOffset.TryParse(header[2], CultureInfo.InvariantCulture,
                                                               DateTimeStyles.None, out DateTimeOffset date))
                continue;

            ++commits;
            first = first == null || date < first ? date : first;
            last = last == null || date > last ? date : last;
            weekdays[((int)date.DayOfWeek + DAYS_IN_WEEK - 1) % DAYS_IN_WEEK]++;
            hours[date.Hour]++;
            string month = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            months[month] = months.GetValueOrDefault(month) + 1;

            int commitAdded = 0;
            int commitRemoved = 0;
            foreach (string line in lines.Skip(1))
            {
                // "added<TAB>removed<TAB>path"; binary files show "-" for both
                string[] parts = line.Split('\t', 3);
                if (parts.Length < 3)
                    continue;

                int plus = int.TryParse(parts[0], out int a) ? a : 0;
                int minus = int.TryParse(parts[1], out int r) ? r : 0;
                commitAdded += plus;
                commitRemoved += minus;
                string path = RenamedPath(parts[2]);
                (int c, int fa, int fr) = files.GetValueOrDefault(path);
                files[path] = (c + 1, fa + plus, fr + minus);
            }

            added += commitAdded;
            removed += commitRemoved;
            string key = header[1].ToLowerInvariant();
            // Keeps the name as first seen; the log runs newest first, so that is the most recent spelling
            (string name, int pc, int pa, int pr) = people.GetValueOrDefault(key, (header[0], 0, 0, 0));
            people[key] = (name, pc + 1, pa + commitAdded, pr + commitRemoved);
        }

        return new RepositoryStatistics
        {
            Commits = commits,
            Added = added,
            Removed = removed,
            First = first,
            Last = last,
            ByWeekday = weekdays,
            ByHour = hours,
            ByMonth = AllMonths(months, first, last),
            Contributors = people.Select(pair => new ContributorStats(pair.Value.Name, pair.Key, pair.Value.Commits,
                                                                      pair.Value.Added, pair.Value.Removed))
                                 .OrderByDescending(person => person.Commits).ToList(),
            Files = files.Select(pair => new FileStats(pair.Key, pair.Value.Commits, pair.Value.Added,
                                                       pair.Value.Removed))
                         .OrderByDescending(file => file.Commits).ThenBy(file => file.Path)
                         .Take(topFiles).ToList()
        };
    }

    // "src/{old => new}/a.cs" and "old.cs => new.cs" count under the new path
    private static string RenamedPath(string path)
    {
        int arrow = path.IndexOf(RENAME_ARROW, StringComparison.Ordinal);
        if (arrow < 0)
            return path;

        int open = path.LastIndexOf('{', arrow);
        int close = path.IndexOf('}', arrow);
        if (open >= 0 && close > arrow)
            return (path[..open] + path[(arrow + RENAME_ARROW.Length)..close] + path[(close + 1)..]).Replace("//", "/");
        return path[(arrow + RENAME_ARROW.Length)..];
    }

    private static List<(string, int)> AllMonths(Dictionary<string, int> months, DateTimeOffset? first,
                                                 DateTimeOffset? last)
    {
        List<(string, int)> result = new();
        if (first == null || last == null)
            return result;

        DateTime month = new DateTime(first.Value.Year, first.Value.Month, 1);
        DateTime end = new DateTime(last.Value.Year, last.Value.Month, 1);
        while (month <= end)
        {
            string key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            result.Add((key, months.GetValueOrDefault(key)));
            month = month.AddMonths(1);
        }

        return result;
    }
}

public partial class GitRepository
{
    // since is a git date such as "1 year ago"; null for the whole history of the current branch
    public async Task<RepositoryStatistics> GetStatisticsAsync(string? since = null)
    {
        if (await IsUnbornAsync())
            return new RepositoryStatistics();

        List<string> arguments = Arguments("log", "--no-merges", "--numstat",
                                           "--format=" + RepositoryStatistics.FORMAT);
        if (since != null)
            arguments.Add("--since=" + since);
        GitResult result = await Run(arguments);
        return result.Success ? RepositoryStatistics.Parse(result.Output) : new RepositoryStatistics();
    }
}
