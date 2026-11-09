using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class SafetyTests
{
    // Names and addresses that git would read as options never reach the command line
    public static async Task OptionLikeInputIsRefused()
    {
        using TempRepository temp = await TempRepository.CreateAsync();
        temp.Write("file.txt", "one\n");
        await temp.CommitAllAsync("First");

        Assert.True(!(await temp.Repository.CreateBranchAsync("--orphan", false)).Success, "branch refused");
        Assert.True(!(await temp.Repository.CreateBranchAtAsync("-f", "HEAD")).Success, "branch at refused");
        Assert.True(!(await temp.Repository.CreateTagAsync("--delete", "HEAD", null)).Success, "tag refused");
        Assert.Equal(1, (await temp.Repository.GetBranchesAsync()).Count, "no branch made");

        string parent = Path.Combine(Path.GetTempPath(), "urras-safety-" + Guid.NewGuid().ToString("N"));
        GitResult clone = await GitRepository.CloneAsync("--upload-pack=touch pwned", Path.Combine(parent, "x"));
        Assert.True(!clone.Success && !File.Exists(Path.Combine(parent, "pwned")), "clone address refused");
        if (Directory.Exists(parent))
            TestFolders.Delete(parent);
    }

    // Owner and repository names from a platform become exactly one folder level
    public static void PlatformNamesAreSingleFolders()
    {
        Assert.True(RepositoryImport.IsPlainFolderName("aceddian"), "plain name");
        Assert.True(RepositoryImport.IsPlainFolderName("ac-wotlk.v2"), "dots and dashes");
        Assert.True(!RepositoryImport.IsPlainFolderName(".."), "parent folder");
        Assert.True(!RepositoryImport.IsPlainFolderName("a/b"), "slash");
        Assert.True(!RepositoryImport.IsPlainFolderName("a\\b"), "backslash");
        Assert.True(!RepositoryImport.IsPlainFolderName("C:"), "drive");
        Assert.True(!RepositoryImport.IsPlainFolderName(" "), "blank");
    }
}
