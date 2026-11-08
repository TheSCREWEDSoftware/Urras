using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace Urras.Core.Commands;

// Where a custom command appears: the Commands menu (repository), a file's menu, or a commit's menu
public enum CommandScope
{
    Repository,
    File,
    Commit
}

// What a command runs with: the values its placeholders are replaced with
public sealed record CommandContext(string Repository, string? Branch = null, string? File = null,
                                    string? Commit = null);

// A user command in Urras's menus; arguments may use ${repo}, ${branch}, ${file}, ${filePath}, ${sha}
public sealed class CustomCommand
{
    public string Name { get; set; } = string.Empty;

    // The program to run: "git" runs Git, anything else is started directly (for example "code" or "cmd")
    public string Program { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter<CommandScope>))]
    public CommandScope Scope { get; set; } = CommandScope.Repository;

    // Asks before running, for commands that change things
    public bool Confirm { get; set; }

    public override string ToString()
    {
        string where = Scope switch
        {
            CommandScope.File => "file menu",
            CommandScope.Commit => "commit menu",
            _ => "Commands menu"
        };
        return Name + "  ·  " + Program + " " + Arguments + "  ·  " + where;
    }

    // Splits like a shell, then fills placeholders per part, so a path with spaces stays one argument
    public List<string> ExpandArguments(CommandContext context)
    {
        return Split(Arguments).Select(argument => Expand(argument, context)).ToList();
    }

    // Placeholders this command needs but the context lacks, such as ${file} from the repository menu
    public IEnumerable<string> MissingValues(CommandContext context)
    {
        if (Arguments.Contains("${branch}") && context.Branch == null)
            yield return "branch";
        if ((Arguments.Contains("${file}") || Arguments.Contains("${filePath}")) && context.File == null)
            yield return "file";
        if (Arguments.Contains("${sha}") && context.Commit == null)
            yield return "commit";
    }

    private static string Expand(string argument, CommandContext context)
    {
        string filePath = context.File == null ? string.Empty
                                               : Path.GetFullPath(Path.Combine(context.Repository, context.File));
        return argument.Replace("${repo}", context.Repository)
                       .Replace("${branch}", context.Branch ?? string.Empty)
                       .Replace("${filePath}", filePath)
                       .Replace("${file}", context.File ?? string.Empty)
                       .Replace("${sha}", context.Commit ?? string.Empty);
    }

    public static List<string> Split(string text)
    {
        List<string> parts = new List<string>();
        StringBuilder current = new StringBuilder();
        bool quoted = false;
        bool any = false;
        foreach (char character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (any)
                    parts.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(character);
                any = true;
            }
        }

        if (any)
            parts.Add(current.ToString());
        return parts;
    }
}

public sealed record CommandResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

public static class CommandRunner
{
    // Runs the command in the repository folder and collects its output; errors starting it become the output
    public static async Task<CommandResult> RunAsync(CustomCommand command, CommandContext context,
                                                     CancellationToken cancellation = default)
    {
        ProcessStartInfo start = new ProcessStartInfo(command.Program)
        {
            WorkingDirectory = context.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in command.ExpandArguments(context))
            start.ArgumentList.Add(argument);

        try
        {
            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);
            string text = (await output + await error).TrimEnd();
            return new CommandResult(process.ExitCode, text);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new CommandResult(-1, "Couldn't start " + command.Program + ": " + exception.Message);
        }
    }
}
