using System.Text;
using Urras.Core.Accounts;

namespace Urras.Core.Git;

// Publishing a repository that has no remote: the name the site will accept, then origin and the first push
public static class RepositoryPublisher
{
    public const string REMOTE = "origin";
    private const char REPLACEMENT = '-';

    // Sites only take letters, digits, '.', '-' and '_'; GitHub turns the rest into '-', so Urras shows that first
    public static string SuggestName(string folderName)
    {
        StringBuilder name = new StringBuilder();
        foreach (char character in folderName.Trim())
            name.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character
                                                                                                : REPLACEMENT);
        return name.ToString();
    }

    // Adds origin, or repoints a deleted one, and pushes the current branch unless there's nothing to push yet
    public static async Task<GitResult> ConnectAsync(GitRepository repository, PlatformRepository online,
                                                     Action<string>? progress = null)
    {
        GitResult added = await repository.GetRemoteUrlAsync(REMOTE) == null
            ? await repository.AddRemoteAsync(REMOTE, online.CloneUrl)
            : await repository.SetRemoteUrlAsync(REMOTE, online.CloneUrl);
        if (!added.Success || !await repository.HasCommitsAsync())
            return added;

        return await repository.PushAsync(progress);
    }
}
