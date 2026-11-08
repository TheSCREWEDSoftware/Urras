using Avalonia.Controls;
using Urras.Core.Commands;
using Urras.Core.Git;

namespace Urras.App;

// Custom commands: the Commands… list in the Branch menu, and entries in the file and commit menus
public partial class MainWindow
{
    private async Task ManageCommandsAsync()
    {
        List<CustomCommand> commands = _settings.CustomCommands;
        string label = commands.Count == 0
            ? "No commands yet. Add one to run a build, a script or another tool from Urras."
            : "Your commands. Repository commands run from here; file and commit commands appear in those menus.";
        (CustomCommand? command, string? action) = await Dialogs.ListActionsAsync(
            this, "Commands", label, commands, "Run", "Add…", "Edit…", "Remove", "Move up");

        switch (action)
        {
            case "Run" when command != null:
                await RunCustomCommandAsync(command, CurrentContext());
                break;
            case "Add…":
            {
                if (await Dialogs.CustomCommandAsync(this, null) is { } added)
                {
                    commands.Add(added);
                    _settings.Save();
                }

                break;
            }
            case "Edit…" when command != null:
            {
                if (await Dialogs.CustomCommandAsync(this, command) is { } edited)
                {
                    commands[commands.IndexOf(command)] = edited;
                    _settings.Save();
                }

                break;
            }
            case "Remove" when command != null:
            {
                commands.Remove(command);
                _settings.Save();
                break;
            }
            case "Move up" when command != null && commands.IndexOf(command) > 0:
            {
                int index = commands.IndexOf(command);
                (commands[index - 1], commands[index]) = (commands[index], commands[index - 1]);
                _settings.Save();
                break;
            }
            default:
                return;
        }

        if (action is not "Run")
            await ManageCommandsAsync();
    }

    private CommandContext CurrentContext(string? file = null, string? commit = null)
    {
        return new CommandContext(_repository?.Path ?? string.Empty, _status?.Branch, file, commit);
    }

    // Adds the user's commands for this kind of item to a context menu
    private void AddCustomCommandItems(ContextMenu menu, CommandScope scope, CommandContext context)
    {
        List<CustomCommand> commands = _settings.CustomCommands.Where(command => command.Scope == scope).ToList();
        if (commands.Count == 0 || _repository == null)
            return;

        menu.Items.Add(new Separator());
        foreach (CustomCommand command in commands)
            AddMenuItem(menu, command.Name, () => RunCustomCommandAsync(command, context));
    }

    private async Task RunCustomCommandAsync(CustomCommand command, CommandContext context)
    {
        if (_repository == null)
            return;

        List<string> missing = command.MissingValues(context).ToList();
        if (missing.Count > 0)
        {
            SetStatus(command.Name + " needs a " + string.Join(" and a ", missing) + "; run it from that menu.", true);
            return;
        }

        if (command.Confirm && !await Dialogs.ConfirmAsync(this, command.Name,
                                                           "Run " + command.Program + " " +
                                                               string.Join(' ', command.ExpandArguments(context)) +
                                                               "?", "Run"))
            return;

        SetStatus("Running " + command.Name + "…", false);
        CommandResult result = await CommandRunner.RunAsync(command, context);
        AppendLog(command.Program + " " + string.Join(' ', command.ExpandArguments(context)),
                  new GitResult(result.ExitCode, result.Output, string.Empty));
        SetStatus(result.Success ? command.Name + " finished." : command.Name + " failed (exit code " +
                                                                 result.ExitCode + ").", !result.Success);
        if (result.Output.Length > 0)
            await Dialogs.ShowTextAsync(this, command.Name, result.Output);
        await RefreshAsync();
    }
}
