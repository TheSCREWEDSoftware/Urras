using System.Text;

namespace Urras.Core.Git;

public enum RebaseAction
{
    Pick,
    Reword,
    Edit,
    Squash,
    Fixup,
    Drop
}

// One commit in an interactive rebase and what to do with it. Message replaces the commit's message for Reword,
// and the combined message for the last commit of a squash.
public sealed class RebaseStep
{
    public RebaseStep(CommitInfo commit)
    {
        Commit = commit;
    }

    public CommitInfo Commit { get; }
    public RebaseAction Action { get; set; } = RebaseAction.Pick;
    public string? Message { get; set; }

    public bool JoinsPrevious => Action is RebaseAction.Squash or RebaseAction.Fixup;
}

// Writes the instructions `git rebase -i` follows, so no text editor is needed
public static class RebaseTodo
{
    // Problems to fix before starting, or null when the plan can run
    public static string? Validate(IReadOnlyList<RebaseStep> steps)
    {
        List<RebaseStep> kept = steps.Where(step => step.Action != RebaseAction.Drop).ToList();
        if (kept.Count == 0)
            return "Every commit would be dropped. Keep at least one, or reset the branch instead.";
        if (kept[0].JoinsPrevious)
            return "The first kept commit can't be squashed or fixed up: there is no commit before it to join.";
        if (steps.Any(step => step.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(step.Message)))
            return "Write a new message for every commit marked Reword.";
        return null;
    }

    // messageFile gives the path of a file holding a step's new message, which the caller writes
    public static string Build(IReadOnlyList<RebaseStep> steps, Func<RebaseStep, string> messageFile)
    {
        StringBuilder todo = new StringBuilder();
        for (int i = 0; i < steps.Count; ++i)
        {
            RebaseStep step = steps[i];
            string verb = step.Action switch
            {
                RebaseAction.Squash => "squash",
                RebaseAction.Fixup => "fixup",
                RebaseAction.Drop => "drop",
                RebaseAction.Edit => "edit",
                _ => "pick"
            };
            todo.Append(verb).Append(' ').Append(step.Commit.Hash).Append(' ').Append(step.Commit.Subject).Append('\n');

            // A new message is applied by amending once the commit (and anything squashed into it) is in place
            bool endsGroup = i + 1 == steps.Count || !steps[i + 1].JoinsPrevious;
            RebaseStep? owner = MessageOwner(steps, i);
            if (endsGroup && owner?.Message is { } message && !string.IsNullOrWhiteSpace(message)
                && step.Action != RebaseAction.Drop)
                todo.Append("exec git commit --amend --allow-empty --quiet --cleanup=strip -F \"")
                    .Append(messageFile(owner).Replace('\\', '/')).Append("\"\n");
        }

        return todo.ToString();
    }

    // The step whose message applies at this point: the commit itself, or the last squash with its own message
    private static RebaseStep? MessageOwner(IReadOnlyList<RebaseStep> steps, int index)
    {
        int start = index;
        while (start > 0 && steps[start].JoinsPrevious)
            --start;

        RebaseStep? owner = null;
        for (int i = start; i <= index; ++i)
        {
            if (steps[i].Action == RebaseAction.Drop)
                continue;
            if (steps[i].Action is RebaseAction.Reword or RebaseAction.Squash && steps[i].Message != null)
                owner = steps[i];
        }

        return owner;
    }
}
