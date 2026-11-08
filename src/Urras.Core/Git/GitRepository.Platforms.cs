namespace Urras.Core.Git;

// Git steps behind platform features: checking out a pull request, finding the main branch
public partial class GitRepository
{
    // Fetches a pull request's commits (refs such as pull/5/head) into a local branch and switches to it
    public async Task<GitResult> CheckOutPullRequestAsync(string pullRequestRef, string localBranch)
    {
        GitResult fetch = await Run("fetch", "origin", "+" + pullRequestRef + ":" + localBranch);
        return fetch.Success ? await Run("switch", localBranch) : fetch;
    }

    // The remote's main branch (origin/HEAD), falling back to main or master
    public async Task<string> GetDefaultBranchAsync()
    {
        GitResult head = await Run("symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD");
        if (head.Success && head.Output.Trim().StartsWith("origin/"))
            return head.Output.Trim()["origin/".Length..];

        GitResult main = await Run("rev-parse", "--verify", "--quiet", "refs/remotes/origin/main");
        return main.Success ? "main" : "master";
    }
}
