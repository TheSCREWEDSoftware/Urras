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
