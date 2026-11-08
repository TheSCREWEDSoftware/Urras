using Avalonia.Controls;
using Urras.Core.Git;

namespace Urras.App;

// Staging, unstaging and discarding selected lines or whole hunks from the Changes diff
public partial class MainWindow
{
    // Whether the Changes diff shows staged changes; set with the diff
    private bool _changesDiffStaged;

    private void InitializeLineStaging()
    {
        DiffList.SelectionMode = SelectionMode.Multiple;
        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, _) => FillLineMenu(menu);
        DiffList.ContextMenu = menu;
    }

    private HashSet<int> SelectedDiffLines()
    {
        return DiffList.SelectedItems?.OfType<DiffRow>().SelectMany(row => row.RawLines).ToHashSet()
               ?? new HashSet<int>();
    }

    private void FillLineMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        string? diff = _changesDiffText;
        HashSet<int> selected = SelectedDiffLines();
        if (_repository == null || diff == null || selected.Count == 0)
            return;

        HashSet<int> changes = PatchBuilder.AllChanges(diff);
        HashSet<int> lines = selected.Where(changes.Contains).ToHashSet();
        HashSet<int> hunks = selected.SelectMany(line => PatchBuilder.HunkOf(diff, line)).ToHashSet();
        string count = lines.Count == 1 ? "line" : lines.Count + " lines";
        int hunkTotal = selected.Select(line => PatchBuilder.HunkOf(diff, line))
                                .Where(hunk => hunk.Count > 0).Select(hunk => hunk.Min()).Distinct().Count();
        string hunkCount = hunkTotal > 1 ? hunkTotal + " hunks" : "hunk";

        if (_changesDiffStaged)
        {
            AddLineItem(menu, "Unstage " + count, lines, () => UnstageLinesAsync(diff, lines));
            AddLineItem(menu, "Unstage " + hunkCount, hunks, () => UnstageLinesAsync(diff, hunks));
            return;
        }

        AddLineItem(menu, "Stage " + count, lines, () => StageLinesAsync(diff, lines));
        AddLineItem(menu, "Stage " + hunkCount, hunks, () => StageLinesAsync(diff, hunks));
        menu.Items.Add(new Separator());
        AddLineItem(menu, "Discard " + count + "…", lines, () => DiscardLinesAsync(diff, lines, count));
        AddLineItem(menu, "Discard " + hunkCount + "…", hunks, () => DiscardLinesAsync(diff, hunks, hunkCount));
    }

    private void AddLineItem(ContextMenu menu, string header, HashSet<int> lines, Func<Task> action)
    {
        MenuItem item = new MenuItem { Header = Literal(header), IsEnabled = lines.Count > 0 };
        item.Click += async (_, _) => await action();
        menu.Items.Add(item);
    }

    private async Task StageLinesAsync(string diff, HashSet<int> lines)
    {
        await RunGitAsync("Staging lines…", repository => repository.StageLinesAsync(diff, lines), "Lines staged.");
        await ReloadChangesDiffAsync();
    }

    private async Task UnstageLinesAsync(string diff, HashSet<int> lines)
    {
        await RunGitAsync("Unstaging lines…", repository => repository.UnstageLinesAsync(diff, lines),
                          "Lines unstaged.");
        await ReloadChangesDiffAsync();
    }

    private async Task DiscardLinesAsync(string diff, HashSet<int> lines, string what)
    {
        if (!await Dialogs.ConfirmAsync(this, "Discard changes",
                                        "Discard the selected " + what + "? This cannot be undone.", "Discard"))
            return;

        await RunGitAsync("Discarding lines…", repository => repository.DiscardLinesAsync(diff, lines),
                          "Lines discarded.");
        await ReloadChangesDiffAsync();
    }

    // The file lists keep their selection on refresh without reloading the diff, so reload it here
    private async Task ReloadChangesDiffAsync()
    {
        ListBox list = _changesDiffStaged ? StagedList : UnstagedList;
        if (list.SelectedItem is FileChange change)
            await ShowSelectedChangeAsync(change, _changesDiffStaged);
        else
            ShowChangesDiff(null, string.Empty);
    }
}
