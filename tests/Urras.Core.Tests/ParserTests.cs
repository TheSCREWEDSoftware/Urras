using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class ParserTests
{
    public static void StatusParsesBranchHeaders()
    {
        string output = "# branch.oid 1234abcd\0# branch.head main\0# branch.upstream origin/main\0# branch.ab +2 -3\0";
        RepositoryStatus status = GitParsers.ParseStatus(output);

        Assert.Equal("main", status.Branch, "branch");
        Assert.Equal("1234abcd", status.HeadCommit, "head");
        Assert.Equal("origin/main", status.Upstream, "upstream");
        Assert.Equal(2, status.Ahead, "ahead");
        Assert.Equal(3, status.Behind, "behind");
        Assert.True(!status.IsUnborn, "not unborn");
    }

    public static void StatusParsesUnbornAndDetached()
    {
        RepositoryStatus unborn = GitParsers.ParseStatus("# branch.oid (initial)\0# branch.head main\0");
        Assert.True(unborn.IsUnborn, "unborn");
        Assert.Equal(null, unborn.HeadCommit, "no head");

        RepositoryStatus detached = GitParsers.ParseStatus("# branch.oid abc\0# branch.head (detached)\0");
        Assert.True(detached.IsDetached, "detached");
    }

    public static void StatusParsesChangesWithSpacesAndRenames()
    {
        string output =
            "1 M. N... 100644 100644 100644 aaa bbb src/my file.txt\0" +
            "1 .M N... 100644 100644 100644 aaa bbb other.txt\0" +
            "1 MM N... 100644 100644 100644 aaa bbb both.txt\0" +
            "2 R. N... 100644 100644 100644 aaa bbb R100 new name.txt\0old name.txt\0" +
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc conflict.txt\0" +
            "? untracked dir/file.txt\0";
        RepositoryStatus status = GitParsers.ParseStatus(output);

        Assert.Equal(3, status.Staged.Count, "staged count");
        Assert.Equal("src/my file.txt", status.Staged[0].Path, "path with space");
        Assert.Equal(ChangeKind.Renamed, status.Staged[2].Kind, "rename kind");
        Assert.Equal("old name.txt", status.Staged[2].OriginalPath, "rename original");
        Assert.Equal("new name.txt", status.Staged[2].Path, "rename path");

        Assert.Equal(4, status.Unstaged.Count, "unstaged count");
        Assert.Equal("other.txt", status.Unstaged[0].Path, "unstaged path");
        Assert.Equal("both.txt", status.Unstaged[1].Path, "partially staged");
        Assert.Equal(ChangeKind.Conflicted, status.Unstaged[2].Kind, "conflict");
        Assert.Equal(ChangeKind.Untracked, status.Unstaged[3].Kind, "untracked");
        Assert.Equal("untracked dir/file.txt", status.Unstaged[3].Path, "untracked path");
    }

    public static void LogParsesRecords()
    {
        string output =
            "abc123\u001fabc\u001fAna\u001fana@example.com\u001f2026-11-08T07:00:00+00:00\u001fFirst: subject\u001e\n" +
            "def456\u001fdef\u001fBo\u001fbo@example.com\u001f2026-11-07T07:00:00+00:00\u001fSecond\u001e\n";
        List<CommitInfo> commits = GitParsers.ParseLog(output);

        Assert.Equal(2, commits.Count, "commit count");
        Assert.Equal("First: subject", commits[0].Subject, "subject");
        Assert.Equal("Bo", commits[1].Author, "author");
        Assert.Equal(2026, commits[0].Date.Year, "year");
    }

    public static void BranchesSkipRemoteHead()
    {
        string output = "refs/heads/main\u001fmain\u001f*\u001forigin/main\u001f[ahead 1]\n" +
                        "refs/heads/dev\u001f" + "dev\u001f \u001f\u001f\n" +
                        "refs/remotes/origin/HEAD\u001forigin\u001f \u001f\u001f\n" +
                        "refs/remotes/origin/main\u001forigin/main\u001f \u001f\u001f\n";
        List<BranchInfo> branches = GitParsers.ParseBranches(output);

        Assert.Equal(3, branches.Count, "branch count");
        Assert.True(branches[0].IsCurrent, "main is current");
        Assert.Equal("[ahead 1]", branches[0].Tracking, "tracking");
        Assert.True(!branches[1].IsCurrent, "dev not current");
        Assert.True(branches[2].IsRemote, "remote branch");
    }
}
