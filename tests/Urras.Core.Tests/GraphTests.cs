using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class GraphTests
{
    private static CommitInfo Commit(string hash, params string[] parents)
    {
        return new CommitInfo(hash, hash, "A", "a@example.com", DateTimeOffset.UnixEpoch, hash) { Parents = parents };
    }

    public static void StraightHistoryUsesOneLane()
    {
        List<CommitInfo> commits = new() { Commit("c", "b"), Commit("b", "a"), Commit("a") };
        CommitGraph.Assign(commits);

        Assert.True(commits.All(commit => commit.Graph!.Lane == 0 && commit.Graph.Width == 1), "one lane");
        Assert.Equal(1, commits[0].Graph!.Lines.Count, "the tip only has a line down");
        Assert.Equal(new GraphLine(0, 0.5, 0, 1, 0), commits[0].Graph!.Lines[0], "dot to bottom");
        Assert.Equal(2, commits[1].Graph!.Lines.Count, "middle commit: in from above and out below");
        Assert.Equal(1, commits[2].Graph!.Lines.Count, "the root only has a line in");
    }

    public static void MergeAndBranchUseASecondLane()
    {
        // m merges f into main; f and b both come from a
        List<CommitInfo> commits = new()
        {
            Commit("m", "b", "f"), Commit("f", "a"), Commit("b", "a"), Commit("a")
        };
        CommitGraph.Assign(commits);

        GraphRow merge = commits[0].Graph!;
        Assert.True(merge.IsMerge, "merge flagged");
        Assert.Equal(2, merge.Width, "two lanes below the merge");
        Assert.True(merge.Lines.Contains(new GraphLine(0, 0.5, 1, 1, 1)), "line to the merged branch's lane");

        Assert.Equal(1, commits[1].Graph!.Lane, "the feature commit sits in lane 1");
        Assert.True(commits[1].Graph!.Lines.Contains(new GraphLine(0, 0, 0, 1, 0)), "main passes beside it");
        Assert.Equal(0, commits[2].Graph!.Lane, "main's commit in lane 0");

        GraphRow root = commits[3].Graph!;
        Assert.True(root.Lines.Contains(new GraphLine(1, 0, 0, 0.5, 1)), "the feature lane joins at the fork point");
        Assert.True(root.Lines.Contains(new GraphLine(0, 0, 0, 0.5, 0)), "main arrives too");
    }

    public static void UnrelatedTipsTakeFreeLanes()
    {
        List<CommitInfo> commits = new() { Commit("x", "a"), Commit("y", "a"), Commit("a") };
        CommitGraph.Assign(commits);
        Assert.Equal(0, commits[0].Graph!.Lane, "first tip");
        Assert.Equal(1, commits[1].Graph!.Lane, "second tip waits beside it");
        Assert.Equal(0, commits[2].Graph!.Lane, "shared parent");
        Assert.Equal(1, commits[2].Graph!.Width - 1, "both lanes arrive at the parent");

        CommitGraph.Clear(commits);
        Assert.True(commits.All(commit => commit.Graph == null), "cleared");
    }

    public static async Task ReadsParentsAndAllBranches()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("Base");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "checkout", "-q", "-b", "feature"), "branch");
        temp.Write("b.txt", "b\n");
        await temp.CommitAllAsync("Feature work");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "checkout", "-q", "main"), "back to main");
        temp.Write("c.txt", "c\n");
        await temp.CommitAllAsync("Main work");

        List<CommitInfo> current = await temp.Repository.GetHistoryAsync();
        Assert.Equal(2, current.Count, "only the current branch by default");
        List<CommitInfo> all = await temp.Repository.GetHistoryAsync(allBranches: true);
        Assert.Equal(3, all.Count, "every branch");
        Assert.Equal(0, all[^1].Parents.Count, "the first commit has no parent");
        Assert.Equal(all[^1].Hash, all[0].Parents[0], "parents are full hashes");

        Assert.Success(await GitRunner.RunAsync(temp.Path, "merge", "-q", "--no-ff", "-m", "Merge feature",
                                                "feature"), "merge");
        List<CommitInfo> merged = await temp.Repository.GetHistoryAsync();
        Assert.True(merged[0].IsMerge, "merge commit has two parents");
        CommitGraph.Assign(merged);
        Assert.Equal(2, merged[0].Graph!.Width, "the graph opens a second lane");
    }

    public static void SplitsRefsIntoBadges()
    {
        List<RefBadge> badges = RefBadge.Parse("HEAD -> refs/heads/main, tag: refs/tags/v0.1.0, " +
                                               "refs/remotes/origin/main, refs/remotes/origin/HEAD, " +
                                               "refs/heads/feature/graph");
        Assert.Equal(3, badges.Count, "origin/HEAD is left out, and origin/main joins main");
        Assert.Equal(new RefBadge("main", RefKind.Head) { Remote = "origin" }, badges[0], "checked-out branch");
        Assert.Equal("main", badges[0].Text, "the badge style marks the current branch");
        Assert.Equal(new RefBadge("v0.1.0", RefKind.Tag), badges[1], "tag");
        Assert.Equal(new RefBadge("feature/graph", RefKind.Local), badges[2], "local branch with a slash");

        // Only "<remote>/<the same name>" joins; a remote branch alone, or one ending the same way, stays apart
        List<RefBadge> split = RefBadge.Parse("refs/heads/split/scripts, refs/remotes/origin/split/scripts, " +
                                              "refs/heads/scripts, refs/remotes/fork/other");
        Assert.Equal("split/scripts:origin,scripts:,fork/other:",
                     string.Join(",", split.Select(badge => badge.Name + ":" + badge.Remote)),
                     "split/scripts joined with origin; scripts doesn't take origin/split/scripts");
        Assert.Equal(new RefBadge("HEAD (detached)", RefKind.Head), RefBadge.Parse("HEAD")[0], "detached");
        Assert.Equal(0, RefBadge.Parse("").Count, "no refs");
    }

    public static void DescribesChangeKinds()
    {
        FileChange renamed = new FileChange("new.cs", ChangeKind.Renamed, "old.cs");
        Assert.Equal("Renamed", renamed.KindName, "name");
        Assert.Equal("Renamed from old.cs", renamed.KindDescription, "tooltip");
        Assert.Equal("Modified",
                     new FileChange("a.cs", ChangeKind.Modified).KindDescription, "modified");
    }

    public static async Task ListsStashesInTheHistory()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("Base");
        temp.Write("a.txt", "changed\n");
        Assert.Success(await temp.Repository.StashAsync("Work in progress", false), "stash");
        await Task.Delay(1100);
        temp.Write("b.txt", "b\n");
        await temp.CommitAllAsync("Later");

        List<CommitInfo> all = await temp.Repository.GetHistoryAsync(allBranches: true);
        Assert.Equal(3, all.Count, "two commits and the stash");
        Assert.True(all[1].IsStash && all[1].Subject.Contains("Work in progress"), "the stash sits between them");
        Assert.Equal(new RefBadge("stash@{0}", RefKind.Stash), all[1].RefBadges[0], "with a stash badge");
        Assert.Equal(all[2].Hash, all[1].Parents[0], "branching off the commit it was made on");
        Assert.Equal(1, all[1].Parents.Count, "the index commit is left out");

        Assert.Equal(2, (await temp.Repository.GetHistoryAsync()).Count, "not in the current-branch history");
        Assert.Equal(2, (await temp.Repository.GetHistoryAsync(filter: new HistoryFilter(HistorySearch.Message, "e"),
                                                                allBranches: true)).Count(c => !c.IsStash),
                     "nor while searching");
        CommitGraph.Assign(all);
        Assert.Equal(1, all[1].Graph!.Lane, "the stash gets its own lane");
    }
}
