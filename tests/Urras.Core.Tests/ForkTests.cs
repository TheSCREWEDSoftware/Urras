using System.Net;
using System.Text;
using Urras.Core.Accounts;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class ForkTests
{
    private sealed class FakeApi : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _answers;

        public FakeApi(Dictionary<string, string> answers)
        {
            _answers = answers;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            bool found = _answers.TryGetValue(request.RequestUri!.AbsolutePath, out string? body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    public static async Task GitHubTellsForksAndPushAccess()
    {
        GitHubClient client = new GitHubClient(new HttpClient(new FakeApi(new Dictionary<string, string>
        {
            ["/repos/TheSCREWEDSoftware/wiki"] = "{\"fork\":true,\"permissions\":{\"push\":true}," +
                                                 "\"parent\":{\"full_name\":\"azerothcore/wiki\"}}",
            ["/repos/azerothcore/wiki"] = "{\"fork\":false,\"permissions\":{\"push\":false}," +
                                          "\"default_branch\":\"master\"}"
        })), "t");

        PlatformAccess fork = (await client.GetAccessAsync("TheSCREWEDSoftware", "wiki"))!;
        Assert.Equal(new PlatformAccess(true, "azerothcore", "wiki"), fork, "fork with push access");
        Assert.True(fork.IsFork, "is a fork");
        PlatformAccess parent = (await client.GetAccessAsync("azerothcore", "wiki"))!;
        Assert.True(!parent.IsFork && !parent.CanPush, "parent, read only");
        Assert.Equal("master", parent.DefaultBranch, "the parent's main branch, for pull requests into it");
        Assert.Equal("https://github.com/azerothcore/wiki/compare/master...TheSCREWEDSoftware:fix%2Ftypo",
                     client.NewForkPullRequestUrl("azerothcore", "wiki", "TheSCREWEDSoftware", "fix/typo", "master"),
                     "pull request from the fork into the parent");
        Assert.Equal(("group/sub", "project"), PlatformAccess.Split("group/sub/project"), "nested owners");
        Assert.Equal(((string?)null, (string?)null), PlatformAccess.Split("plain"), "not a full name");
    }

    public static void ChoosesWhereContributionsGo()
    {
        RemoteIdentity fork = new RemoteIdentity("TheSCREWEDSoftware", "wiki", "github.com");
        RemoteIdentity parent = new RemoteIdentity("azerothcore", "wiki", "github.com");
        Assert.Equal(parent, new ForkSettings(parent, ForkTarget.Unset).ContributionTarget(fork), "parent by default");
        Assert.Equal(parent, new ForkSettings(parent, ForkTarget.Parent).ContributionTarget(fork), "parent");
        Assert.Equal(fork, new ForkSettings(parent, ForkTarget.Own).ContributionTarget(fork), "own purposes");
        Assert.Equal(fork, new ForkSettings(null, ForkTarget.Unset).ContributionTarget(fork), "not a fork");
        Assert.Equal(fork, new ForkSettings(parent, ForkTarget.Unset, false).ContributionTarget(fork),
                     "an upstream remote alone doesn't redirect anything");
        Assert.Equal(parent, new ForkSettings(parent, ForkTarget.Parent, false).ContributionTarget(fork),
                     "unless chosen");

        Dictionary<string, RemoteIdentity> remotes = new Dictionary<string, RemoteIdentity>
        {
            [RemoteIdentity.ORIGIN] = fork,
            [ForkSettings.UPSTREAM] = parent
        };
        Assert.Equal(parent, ForkSettings.ParentFrom(" azerothcore/wiki\n", fork, remotes), "saved parent");
        Assert.Equal(parent, ForkSettings.ParentFrom(null, fork, remotes), "upstream remote");
        Assert.True(ForkSettings.ParentFrom(null, fork, new Dictionary<string, RemoteIdentity>()) == null, "none");
        Assert.Equal(ForkTarget.Own, ForkSettings.ParseTarget("own\n"), "parse");
        Assert.Equal("https://github.com/azerothcore/wiki", parent.WebUrl, "web page");
        Assert.True(RemoteIdentity.FromUrl(@"F:\Repos\Engine")!.WebUrl == null, "no page for local paths");
    }

    public static async Task KeepsTheChoiceInTheRepository()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        GitRepository repository = new GitRepository(temp.Path);
        Assert.Success(await GitRunner.RunAsync(temp.Path, "remote", "add", "origin",
                                                "git@github.com:TheSCREWEDSoftware/wiki.git"), "origin");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "remote", "add", "upstream",
                                                "https://github.com/vmangos/wiki.git"), "upstream");
        Assert.Equal(2, RemoteIdentity.ReadAll(temp.Path).Count, "both remotes read");

        ForkSettings fresh = await repository.GetForkSettingsAsync();
        Assert.Equal("vmangos", fresh.Parent?.Owner, "upstream until the platform says otherwise");
        Assert.True(!fresh.Confirmed, "only a guess");
        Assert.Equal(ForkTarget.Unset, fresh.Target, "not chosen");

        await repository.SaveForkSettingsAsync(new RemoteIdentity("azerothcore", "wiki", "github.com"), ForkTarget.Own);
        ForkSettings saved = await repository.GetForkSettingsAsync();
        Assert.Equal(new ForkSettings(new RemoteIdentity("azerothcore", "wiki", "github.com"), ForkTarget.Own), saved,
                     "saved in the repository's configuration");

        await repository.SaveForkSettingsAsync(null, ForkTarget.Unset);
        Assert.Equal("vmangos", (await repository.GetForkSettingsAsync()).Parent?.Owner, "cleared");
    }

    public static async Task FindsTheFirstCommits()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        GitRepository repository = new GitRepository(temp.Path);
        Assert.Equal(0, (await repository.GetRootCommitsAsync()).Count, "no commits yet");
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "a");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "a.txt"), "add");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "commit", "-m", "First"), "commit");
        Assert.Equal(1, (await repository.GetRootCommitsAsync()).Count, "one first commit");
    }
}

public static class BranchChangesTests
{
    public static async Task KeepsChangesWithTheirBranch()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        GitRepository repository = new GitRepository(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "a");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "a.txt"), "add");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "commit", "-m", "First"), "commit");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "branch", "other"), "branch");
        string main = (await GitRunner.RunAsync(temp.Path, "branch", "--show-current")).Output.Trim();

        File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "changed");
        File.WriteAllText(Path.Combine(temp.Path, "new.txt"), "new");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "add", "a.txt"), "stage");
        Assert.Success(await repository.KeepChangesWithBranchAsync(main), "keep");
        Assert.Equal(string.Empty, (await GitRunner.RunAsync(temp.Path, "status", "--porcelain")).Output.Trim(),
                     "clean once kept, untracked files too");

        Assert.Success(await repository.SwitchAsync("other"), "switch away");
        Assert.True(await repository.RestoreKeptChangesAsync("other") == null, "nothing kept with other");
        Assert.Success(await repository.SwitchAsync(main), "switch back");
        Assert.Success((await repository.RestoreKeptChangesAsync(main))!, "restore");
        Assert.Equal("M  a.txt|?? new.txt",
                     string.Join("|", (await GitRunner.RunAsync(temp.Path, "status", "--porcelain")).Output
                                      .Split('\n', StringSplitOptions.RemoveEmptyEntries)),
                     "back as they were, staging included");
        Assert.Equal(0, (await repository.GetStashesAsync()).Count, "the kept stash is used up");
    }

    public static void WritesTheCrashLog()
    {
        string path = Path.Combine(Path.GetTempPath(), "urras-crash-" + Guid.NewGuid().ToString("N"), "crash.log");
        try
        {
            Assert.Equal(path, Urras.Core.Settings.CrashLog.Write(new InvalidOperationException("Boom"), "Test", path),
                         "written");
            Assert.True(File.ReadAllText(path).Contains("Boom"), "has the error");
        }
        finally
        {
            TestFolders.Delete(Path.GetDirectoryName(path)!);
        }
    }
}

public static class SettingsFolderTests
{
    public static void MovesWithTheVariable()
    {
        string? before = Environment.GetEnvironmentVariable(Urras.Core.Settings.AppSettings.FOLDER_VARIABLE);
        string folder = Path.Combine(Path.GetTempPath(), "urras-settings-folder");
        try
        {
            Environment.SetEnvironmentVariable(Urras.Core.Settings.AppSettings.FOLDER_VARIABLE, folder);
            Assert.Equal(folder, Path.GetDirectoryName(Urras.Core.Settings.AppSettings.DefaultPath), "moved");
            Assert.Equal(folder, Path.GetDirectoryName(Urras.Core.Settings.CrashLog.DefaultPath), "log follows");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Urras.Core.Settings.AppSettings.FOLDER_VARIABLE, before);
        }
    }
}

public static class SettingsSafetyTests
{
    // The tests' own settings folder is in use, so no test can write over the real settings again
    public static void TestsNeverTouchTheRealSettings()
    {
        string real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                   Urras.Core.Settings.AppSettings.APP_FOLDER_NAME);
        Assert.True(!Urras.Core.Settings.AppSettings.DefaultPath.StartsWith(real, StringComparison.OrdinalIgnoreCase),
                    "settings in a test folder, not " + real);
    }

    public static void KeepsDailyBackupsAndSetsBrokenFilesAside()
    {
        string folder = Path.Combine(Path.GetTempPath(), "urras-backups-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.json");
        try
        {
            Urras.Core.Settings.AppSettings first = new Urras.Core.Settings.AppSettings();
            first.Repositories.Add("/repos/one");
            first.Save(path);
            new Urras.Core.Settings.AppSettings().Save(path);

            string[] backups = Directory.GetFiles(Urras.Core.Settings.AppSettings.BackupFolder(path));
            Assert.Equal(1, backups.Length, "one backup a day");
            Assert.True(File.ReadAllText(backups[0]).Contains("/repos/one"), "the day's first state is kept");

            File.WriteAllText(path, "{ broken");
            Assert.Equal(0, Urras.Core.Settings.AppSettings.Load(path).Repositories.Count, "defaults");
            Assert.True(Directory.GetFiles(folder, "settings.json.broken-*").Length == 1, "broken file set aside");
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }
}

public static class BranchListTests
{
    public static void OffersLocalAndMainRemoteBranches()
    {
        List<BranchInfo> branches = new List<BranchInfo>
        {
            new BranchInfo("master", true, false, "origin/master", null),
            new BranchInfo("quest-min-level-hook", false, false, null, null),
            new BranchInfo("origin/master", false, true, null, null),
            new BranchInfo("origin/release", false, true, null, null),
            new BranchInfo("upstream/master", false, true, null, null),
            new BranchInfo("upstream/fix/crash", false, true, null, null),
            new BranchInfo("github-desktop-sogladev/feat-melee-leeway", false, true, null, null)
        };
        Assert.Equal("master|quest-min-level-hook|origin/release|upstream/fix/crash",
                     string.Join("|", BranchLists.ForPicker(branches).Select(branch => branch.Name)),
                     "local ones, then origin's and upstream's without a local twin; pull request remotes left out");
        Assert.Equal("fix/crash", BranchLists.LocalName("upstream/fix/crash"), "local name");
    }
}

public static class CommitSubjectTests
{
    private static string Show(string subject)
    {
        return string.Concat(CommitSubject.Parts(subject)
                                          .Select(part => part.IsCode ? "[" + part.Text + "]" : part.Text));
    }

    public static void MarksCodeAndBranchNames()
    {
        Assert.Equal("Merge branch [master] into [fix/togc-unlock-conditions]",
                     Show("Merge branch 'master' into fix/togc-unlock-conditions"), "merge of a branch");
        Assert.Equal("Merge branch [main] of https://github.com/o/r into [dev]",
                     Show("Merge branch 'main' of https://github.com/o/r into dev"), "merge from another address");
        Assert.Equal("Merge remote-tracking branch [origin/main]", Show("Merge remote-tracking branch 'origin/main'"),
                     "remote-tracking");
        Assert.Equal("Merge pull request #12 from [owner/feature]", Show("Merge pull request #12 from owner/feature"),
                     "pull request");
        Assert.Equal("fix: set [WorldMapAreaId] in [quest_poi]", Show("fix: set `WorldMapAreaId` in `quest_poi`"),
                     "backticks, shown without them");
        Assert.Equal("plain summary", Show("plain summary"), "nothing to mark");
        Assert.Equal("", Show(""), "empty");
    }
}

public static class BranchPickerTests
{
    private static readonly List<BranchInfo> Branches = new List<BranchInfo>
    {
        new BranchInfo("quest-min-level-hook", true, false, null, null),
        new BranchInfo("master", false, false, "origin/master", null),
        new BranchInfo("feedback_messages", false, false, null, null),
        new BranchInfo("Bonestrippers", false, false, null, null),
        new BranchInfo("origin/master", false, true, null, null),
        new BranchInfo("origin/A-Spark-of-Hope", false, true, null, null),
        new BranchInfo("github-desktop-sogladev/feat-melee-leeway", false, true, null, null)
    };

    private static string Show(List<BranchSection> sections)
    {
        return string.Join("; ", sections.Select(section => section.Title + ": " +
                                                            string.Join(", ", section.Branches.Select(b => b.Name))));
    }

    public static void GroupsLikeGitHubDesktop()
    {
        Assert.Equal("Default branch: master; Recent branches: feedback_messages, quest-min-level-hook; " +
                     "Other branches: Bonestrippers, origin/A-Spark-of-Hope",
                     Show(BranchLists.Sections(Branches, "master",
                                               new[] { "feedback_messages", "master", "quest-min-level-hook", "gone" },
                                               null)),
                     "default, recently used (only branches that exist), then the rest with local ones first");
        Assert.Equal("Matching branches: github-desktop-sogladev/feat-melee-leeway",
                     Show(BranchLists.Sections(Branches, "master", Array.Empty<string>(), "melee")),
                     "a filter searches every remote too");
        Assert.Equal(0, BranchLists.Sections(Branches, "master", Array.Empty<string>(), "nothing").Count, "none");
    }

    public static async Task ReadsDatesAndRecentBranches()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a");
        await temp.CommitAllAsync("First");
        Assert.Success(await temp.Repository.CreateBranchAsync("feature", true), "branch");
        Assert.Success(await temp.Repository.SwitchAsync("main"), "back");

        Dictionary<string, DateTimeOffset> dates = await temp.Repository.GetBranchDatesAsync();
        Assert.True(dates.ContainsKey("main") && dates.ContainsKey("feature"), "a date for each branch");
        List<string> recent = await temp.Repository.GetRecentBranchesAsync();
        Assert.Equal("main|feature", string.Join("|", recent), "newest first");
    }

    public static void SaysHowLongAgo()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", Urras.Core.Settings.RelativeTime.Describe(now.AddSeconds(-5), now), "seconds");
        Assert.Equal("3 minutes ago", Urras.Core.Settings.RelativeTime.Describe(now.AddMinutes(-3), now), "minutes");
        Assert.Equal("1 hour ago", Urras.Core.Settings.RelativeTime.Describe(now.AddHours(-1), now), "an hour");
        Assert.Equal("yesterday", Urras.Core.Settings.RelativeTime.Describe(now.AddDays(-1), now), "a day");
        Assert.Equal("21 days ago", Urras.Core.Settings.RelativeTime.Describe(now.AddDays(-21), now), "days");
        Assert.Equal("last month", Urras.Core.Settings.RelativeTime.Describe(now.AddDays(-40), now), "a month");
        Assert.Equal("11 months ago", Urras.Core.Settings.RelativeTime.Describe(now.AddDays(-340), now), "months");
        Assert.Equal("2 years ago", Urras.Core.Settings.RelativeTime.Describe(now.AddDays(-800), now), "years");
    }
}
