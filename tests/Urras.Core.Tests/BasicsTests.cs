using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.Core.Tests;

public static class BasicsTests
{
    public static void TemplateSplitsGuideFromMessage()
    {
        CommitTemplate template = CommitTemplate.Parse("### TITLE\n## Type(Scope): Summary\n\nfeat(Core): \n\n" +
                                                       "## Body: explain why\n");
        Assert.Equal("feat(Core):", template.Message, "message without comment lines");
        Assert.Equal("TITLE\nType(Scope): Summary\nBody: explain why", template.Guide, "guide lines without #");

        Assert.True(CommitTemplate.Parse("## only guidance\n").Message.Length == 0, "guidance-only template");
    }

    public static async Task TemplateIsFoundFromConfigOrFile()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.Equal(null, await temp.Repository.GetCommitTemplateAsync(), "no template");

        temp.Write(GitRepository.DEFAULT_TEMPLATE_FILE, "## from file\n");
        Assert.Equal("## from file\n", await temp.Repository.GetCommitTemplateAsync(), "template file in the repo");

        temp.Write("custom.txt", "## configured\n");
        Assert.Success(await GitRunner.RunAsync(temp.Path, "config", "commit.template", "custom.txt"), "config");
        Assert.Equal("## configured\n", await temp.Repository.GetCommitTemplateAsync(), "commit.template wins");
    }

    public static async Task CommitMessageByHash()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("a.txt", "a\n");
        await temp.CommitAllAsync("First\n\nWith a body");
        temp.Write("a.txt", "b\n");
        await temp.CommitAllAsync("Second");

        List<CommitInfo> history = await temp.Repository.GetHistoryAsync();
        Assert.Equal("First\n\nWith a body", await temp.Repository.GetCommitMessageAsync(history[1].Hash), "message");
    }

    public static async Task StageTrackedLeavesUntrackedAlone()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("tracked.txt", "1\n");
        await temp.CommitAllAsync("Initial");
        temp.Write("tracked.txt", "2\n");
        temp.Write("untracked.txt", "new\n");

        Assert.Success(await temp.Repository.StageTrackedAsync(), "stage tracked");
        RepositoryStatus status = await temp.Repository.GetStatusAsync();
        Assert.Equal("tracked.txt", status.Staged.Single().Path, "only the tracked file staged");
        Assert.Equal(ChangeKind.Untracked, status.Unstaged.Single().Kind, "untracked file left alone");
    }

    public static async Task BackgroundFetchIsQuiet()
    {
        using TempRepository remote = await TempRepository.CreateAsync();
        remote.Write("a.txt", "a\n");
        await remote.CommitAllAsync("Initial");
        using TempRepository local = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(local.Path, "remote", "add", "origin", remote.Path), "remote");

        List<bool> quietFlags = new List<bool>();
        void Record(string command, GitResult result, bool quiet)
        {
            if (command.StartsWith("git fetch", StringComparison.Ordinal))
                quietFlags.Add(quiet);
        }

        GitRunner.CommandFinished += Record;
        try
        {
            Assert.Success(await local.Repository.FetchAsync(true), "background fetch");
            Assert.Success(await local.Repository.FetchAsync(), "normal fetch");
        }
        finally
        {
            GitRunner.CommandFinished -= Record;
        }

        Assert.Equal("True,False", string.Join(",", quietFlags), "background fetch marked quiet");
    }

    public static void EditorConfigEndOfLineRules()
    {
        EditorConfig config = EditorConfig.Parse("root = true\n\n[*]\nend_of_line = lf\n\n[*.{bat,cmd}]\n" +
                                                 "end_of_line = crlf\n\n[docs/**.md]\nend_of_line = crlf\n");
        Assert.Equal(LineEnding.Lf, config.EndOfLine("src/main.cpp"), "everything LF");
        Assert.Equal(LineEnding.Crlf, config.EndOfLine("Tools/build.bat"), "bat files CRLF, in any folder");
        Assert.Equal(LineEnding.Crlf, config.EndOfLine("docs/guide/intro.md"), "path pattern with **");
        Assert.Equal(LineEnding.Lf, config.EndOfLine("README.md"), "other markdown stays LF");
        Assert.Equal(LineEnding.None, EditorConfig.Parse(string.Empty).EndOfLine("a.txt"), "no rules");
    }

    public static void ParsesLsFilesEol()
    {
        string output = "i/lf    w/crlf  attr/text=auto eol=lf \tsrc/a.cpp\0" +
                        "i/crlf  w/crlf  attr/                 \tnotes.txt\0" +
                        "i/mixed w/mixed attr/                 \tmixed.txt\0" +
                        "i/-text w/-text attr/-text            \timage.png\0";
        Dictionary<string, LineEndingInfo> files = LineEndings.Parse(output, EditorConfig.Parse("[*]\nend_of_line=lf"))
            .ToDictionary(info => info.Path);

        Assert.True(!files.ContainsKey("image.png"), "binary files skipped");
        Assert.Equal(null, files["src/a.cpp"].StagedWarning, "index is LF as .gitattributes asks");
        Assert.True(files["notes.txt"].StagedWarning!.StartsWith("CRLF line endings", StringComparison.Ordinal),
                    "CRLF against the .editorconfig rule");
        Assert.Equal("Mixed line endings", files["mixed.txt"].WorkingTreeWarning, "mixed on disk");
    }

    public static async Task LineEndingWarningsFromARealRepository()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        Assert.Success(await GitRunner.RunAsync(temp.Path, "config", "core.autocrlf", "false"), "autocrlf off");
        temp.Write(".editorconfig", "root = true\n[*]\nend_of_line = lf\n");
        temp.Write("good.txt", "one\ntwo\n");
        temp.Write("bad.txt", "one\r\ntwo\r\n");
        temp.Write("mixed.txt", "one\r\ntwo\n");
        Assert.Success(await temp.Repository.StageAllAsync(), "stage");

        Dictionary<string, LineEndingInfo> files =
            await LineEndings.GetAsync(temp.Path, new[] { "good.txt", "bad.txt", "mixed.txt", "new.txt" });
        Assert.Equal(null, files["good.txt"].StagedWarning, "LF file is fine");
        Assert.True(files["bad.txt"].StagedWarning != null, "CRLF file flagged");
        Assert.Equal("Mixed line endings", files["mixed.txt"].StagedWarning, "mixed file flagged");

        temp.Write("new.txt", "a\r\nb\r\n");
        files = await LineEndings.GetAsync(temp.Path, new[] { "new.txt" });
        Assert.Equal(LineEnding.Crlf, files["new.txt"].WorkingTree, "untracked file checked on disk");
    }

    public static void SettingsDefaultsForNewOptions()
    {
        AppSettings settings = new AppSettings();
        Assert.True(!settings.HideUntracked, "untracked files shown by default");
        Assert.Equal(AppSettings.DEFAULT_AUTO_FETCH_MINUTES, settings.AutoFetchMinutes, "auto fetch default");
    }
}
