using System.Net;
using System.Text;
using Urras.Core.Accounts;
using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class NewRepositoryTests
{
    // GitHub's template endpoints, answered with canned JSON
    private sealed class FakeTemplates : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            string body = request.RequestUri!.PathAndQuery switch
            {
                "/gitignore/templates" => "[\"C++\",\"Node\"]",
                "/gitignore/templates/C%2B%2B" => "{\"name\":\"C++\",\"source\":\"*.o\\n\"}",
                "/licenses" => "[{\"key\":\"mit\",\"name\":\"MIT License\"}]",
                "/licenses/mit" => "{\"key\":\"mit\",\"body\":\"Copyright (c) [year] [fullname]\\n\"}",
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    public static void ResolvesCloneAddresses()
    {
        Assert.Equal("https://github.com/azerothcore/wiki", CloneAddress.Resolve(" azerothcore/wiki "),
                     "owner/name is GitHub");
        Assert.Equal("https://github.com/a/b", CloneAddress.Resolve("github.com/a/b"), "without https");
        Assert.Equal("git@gitlab.com:a/b.git", CloneAddress.Resolve("git@gitlab.com:a/b.git"), "kept");
        Assert.Equal("https://x.org/a/b", CloneAddress.Resolve("https://x.org/a/b"), "kept");
        Assert.Equal("wiki", CloneAddress.FolderName("https://github.com/azerothcore/wiki.git/"), "folder name");
        Assert.Equal("b", CloneAddress.FolderName("git@gitlab.com:a/b.git"), "from SSH");
    }

    public static void ReadsRepositoryPagesAsCloneAddresses()
    {
        CloneSource tree = CloneAddress.Parse("https://github.com/TheSCREWEDSoftware/azerothcore-wotlk/tree/master");
        Assert.Equal("https://github.com/TheSCREWEDSoftware/azerothcore-wotlk", tree.Url, "repository of a page");
        Assert.Equal("master", string.Join(",", tree.BranchCandidates), "branch of the page");
        Assert.Equal("azerothcore-wotlk", CloneAddress.FolderName(tree.Url), "folder from the repository");

        CloneSource file = CloneAddress.Parse("https://github.com/a/b/blob/fix/x/src/main.cpp?plain=1#L4");
        Assert.Equal("https://github.com/a/b", file.Url, "file page");
        Assert.Equal("fix/x/src/main.cpp,fix/x/src,fix/x,fix", string.Join(",", file.BranchCandidates),
                     "every length, longest first");

        CloneSource gitlab = CloneAddress.Parse("https://gitlab.com/group/sub/game/-/tree/dev/docs");
        Assert.Equal("https://gitlab.com/group/sub/game", gitlab.Url, "GitLab nested groups");
        Assert.Equal("dev/docs,dev", string.Join(",", gitlab.BranchCandidates), "GitLab branch");

        CloneSource gitea = CloneAddress.Parse("https://git.example.org/a/b/src/branch/main/README.md");
        Assert.Equal("https://git.example.org/a/b", gitea.Url, "Gitea");
        Assert.Equal("main", gitea.BranchCandidates[^1], "Gitea branch after src/branch");

        Assert.Equal("https://github.com/a/b", CloneAddress.Resolve("https://github.com/a/b/"), "trailing slash");
        Assert.Equal("https://github.com/a/b", CloneAddress.Resolve("https://github.com/a/b/pulls"), "other page");
        Assert.Equal("https://github.com/a/b.git", CloneAddress.Resolve("https://github.com/a/b.git"), "kept");
        Assert.Equal(0, CloneAddress.Parse("https://github.com/a/b").BranchCandidates.Count, "no branch");
    }

    public static void MeasuresAWholeClone()
    {
        Assert.Equal(5.0, CloneProgress.Overall(new GitProgress("Receiving objects", 0)), "receiving starts");
        Assert.Equal(80.0, CloneProgress.Overall(new GitProgress("Resolving deltas", 0)), "resolving starts");
        Assert.Equal(100.0, CloneProgress.Overall(new GitProgress("Updating files", 100)), "done");
        Assert.Equal(null, CloneProgress.Overall(new GitProgress("Enumerating objects", 50)), "not a clone step");
    }

    public static async Task ClonesTheBranchAPageShowed()
    {
        using TempRepository source = await TempRepository.CreateAsync();
        source.Write("a.txt", "a");
        await source.CommitAllAsync("first");
        Assert.Success(await GitRunner.RunAsync(source.Path, "branch", "feature/x"), "branch");
        string target = Path.Combine(Path.GetTempPath(), "urras-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            string? branch = await GitRepository.ChooseBranchAsync(source.Path,
                                                                   new[] { "feature/x/src", "feature/x", "feature" });
            Assert.Equal("feature/x", branch, "the candidate the remote has");
            List<string> lines = new List<string>();
            Assert.Success(await GitRepository.CloneAsync(source.Path, target, branch, lines.Add), "clone");
            Assert.Equal("feature/x", (await GitRunner.RunAsync(target, "branch", "--show-current")).Output.Trim(),
                         "checked out");
        }
        finally
        {
            TestFolders.Delete(target);
        }
    }

    public static async Task ReadsGitHubsTemplates()
    {
        GitHubClient client = new GitHubClient(new HttpClient(new FakeTemplates()), null);
        Assert.Equal("C++,Node", string.Join(",", await client.GetGitIgnoreNamesAsync()), "names");
        Assert.Equal("*.o\n", await client.GetGitIgnoreAsync("C++"), "escaped name");
        Assert.Equal("mit", (await client.GetLicensesAsync())[0].Key, "licence key");
        string license = RepositoryCreator.FillLicense(await client.GetLicenseAsync("mit"), "Ryan", 2026);
        Assert.Equal("Copyright (c) 2026 Ryan\n", license, "placeholders filled");
    }

    public static async Task CreatesARepositoryWithItsFirstFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "urras-new-" + Guid.NewGuid().ToString("N"), "Game");
        try
        {
            GitResult result = await RepositoryCreator.CreateAsync(
                new NewRepository(folder, "Game", "A small game.", true, "*.o\n", "MIT"));
            Assert.Success(result, "created");
            Assert.Equal("# Game\n\nA small game.\n", File.ReadAllText(Path.Combine(folder, "README.md")),
                         "README");
            Assert.True(File.Exists(Path.Combine(folder, ".gitignore")) && File.Exists(Path.Combine(folder,
                                                                                                "LICENSE")),
                        "the other files");
            GitResult log = await GitRunner.RunAsync(folder, "log", "--format=%s");
            Assert.Equal(RepositoryCreator.FIRST_COMMIT, log.Output.Trim(), "one first commit");

            GitResult again = await RepositoryCreator.CreateAsync(
                new NewRepository(folder, "Game", string.Empty, false, null, null));
            Assert.True(!again.Success, "an existing repository is refused");
        }
        finally
        {
            TestFolders.Delete(Path.GetDirectoryName(folder)!);
        }
    }
}
