using System.Globalization;
using System.Text.Json.Serialization;

namespace Urras.Core.Settings;

public enum ThemeChoice
{
    System,
    Dark,
    Light
}

// What happens to uncommitted changes when switching branch
public enum BranchSwitchChanges
{
    Ask,
    Bring,
    Stash,

    // Each branch keeps its own uncommitted changes: put away when leaving, back when returning
    KeepWithBranch
}

// Which questions Urras asks before doing something that can't easily be undone
public sealed class PromptSettings
{
    public bool RemovingRepositories { get; set; } = true;
    public bool DiscardingChanges { get; set; } = true;
    public bool DroppingStashes { get; set; } = true;
    public bool CheckingOutCommits { get; set; } = true;
    public bool RemovingWorktrees { get; set; } = true;
    public bool ShowCommitLengthWarning { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter<BranchSwitchChanges>))]
    public BranchSwitchChanges SwitchingWithChanges { get; set; } = BranchSwitchChanges.Ask;
}

// A known program by name, or a custom path and arguments where %TARGET_PATH% is the folder or file
public sealed class ProgramChoice
{
    public const string TARGET = "%TARGET_PATH%";
    public const string CUSTOM = "Custom";

    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Arguments { get; set; } = TARGET;

    public bool IsCustom => Name == CUSTOM;

    public List<string> ArgumentsFor(string target)
    {
        List<string> arguments = Urras.Core.Commands.CustomCommand.Split(Arguments)
                                                                  .Select(part => part.Replace(TARGET, target))
                                                                  .ToList();
        if (!Arguments.Contains(TARGET))
            arguments.Add(target);
        return arguments;
    }
}

// How dates and times are written in History, blame and elsewhere
public static class DisplayFormats
{
    public const string DEFAULT_DATE = "yyyy-MM-dd";
    public const string DEFAULT_TIME = "HH:mm";

    public static readonly string[] Dates =
    {
        "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "yyyy/MM/dd", "yyyy.MM.dd",
        "d MMM yyyy", "MMM d, yyyy", "dd/MM/yy", "MM/dd/yy"
    };

    public static readonly string[] Times = { "HH:mm", "HH:mm:ss", "h:mm tt", "h:mm:ss tt" };

    public static string DateFormat { get; set; } = DEFAULT_DATE;
    public static string TimeFormat { get; set; } = DEFAULT_TIME;

    public static string Date(DateTimeOffset date)
    {
        return date.LocalDateTime.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    public static string DateAndTime(DateTimeOffset date)
    {
        return date.LocalDateTime.ToString(DateFormat + " " + TimeFormat, CultureInfo.InvariantCulture);
    }

    // "2026-11-08 (yyyy-MM-dd)", for the options list
    public static string Example(string format)
    {
        return new DateTime(2026, 11, 8, 14, 30, 45).ToString(format, CultureInfo.InvariantCulture) + "  (" + format +
               ")";
    }
}
