namespace Urras.Core.Git;

// The user's global Git settings (~/.gitconfig): name, email, default branch for new repositories
public static class GlobalConfig
{
    public const string USER_NAME = "user.name";
    public const string USER_EMAIL = "user.email";
    public const string DEFAULT_BRANCH = "init.defaultBranch";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static async Task<string?> GetAsync(string key)
    {
        GitResult result = await GitRunner.RunAsync(Home, new[] { "config", "--global", "--get", key }, quiet: true);
        string value = result.Output.Trim();
        return result.Success && value.Length > 0 ? value : null;
    }

    // An empty value removes the setting
    public static async Task<GitResult> SetAsync(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            GitResult unset = await GitRunner.RunAsync(Home, new[] { "config", "--global", "--unset", key });
            // Exit code 5 means it wasn't set, which is what was asked for
            return unset.ExitCode == 5 ? new GitResult(0, string.Empty, string.Empty) : unset;
        }

        return await GitRunner.RunAsync(Home, new[] { "config", "--global", key, value.Trim() });
    }
}
