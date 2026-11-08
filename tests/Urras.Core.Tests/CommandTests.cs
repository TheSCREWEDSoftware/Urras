using Urras.Core.Commands;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class CommandTests
{
    public static void SplitsAndExpandsArguments()
    {
        Assert.Equal("a|b c|d|", string.Join("|", CustomCommand.Split("a \"b c\"  d \"\"")), "quotes and empty");

        CustomCommand command = new CustomCommand
        {
            Name = "Open", Program = "code", Arguments = "--goto \"${filePath}\" ${branch}:${sha}"
        };
        CommandContext context = new CommandContext("/repos/My Game", "main", "src/a b.cs", "abc123");
        List<string> arguments = command.ExpandArguments(context);
        Assert.Equal("--goto", arguments[0], "plain argument");
        Assert.Equal(Path.GetFullPath("/repos/My Game/src/a b.cs"), arguments[1], "path with spaces stays one");
        Assert.Equal("main:abc123", arguments[2], "branch and commit");
        Assert.Equal(0, command.MissingValues(context).Count(), "everything given");
        Assert.Equal("file|commit", string.Join("|", command.MissingValues(new CommandContext("/r", "main"))),
                     "missing file and commit");
        Assert.True(command.ToString().Contains("Commands menu"), "readable");
    }

    public static async Task RunsCommandsAndReportsFailures()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        CustomCommand status = new CustomCommand
        {
            Name = "Branch", Program = "git", Arguments = "branch --show-current"
        };
        CommandResult result = await CommandRunner.RunAsync(status, new CommandContext(temp.Path));
        Assert.True(result.Success && result.Output == "main", "git runs in the repository: " + result.Output);

        CustomCommand missing = new CustomCommand { Name = "Nope", Program = "urras-no-such-program" };
        CommandResult failed = await CommandRunner.RunAsync(missing, new CommandContext(temp.Path));
        Assert.True(!failed.Success && failed.Output.Contains("Couldn't start"), "missing program reported");
    }

    public static void SavesCommandsInTheSettings()
    {
        AppSettings settings = new AppSettings();
        settings.CustomCommands.Add(new CustomCommand
        {
            Name = "Log", Program = "git", Arguments = "log -1", Scope = CommandScope.Commit, Confirm = true
        });
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        Assert.True(json.Contains("\"Commit\""), "scope saved by name");
        AppSettings loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.True(loaded.CustomCommands[0].Confirm && loaded.CustomCommands[0].Scope == CommandScope.Commit,
                    "read back");
    }
}
