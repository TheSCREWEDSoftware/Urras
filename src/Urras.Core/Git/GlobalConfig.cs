namespace Urras.Core.Git;

// The user's global Git settings (~/.gitconfig): name, email, default branch for new repositories
public static class GlobalConfig
{
    public const string USER_NAME = "user.name";
    public const string USER_EMAIL = "user.email";
    public const string DEFAULT_BRANCH = "init.defaultBranch";
    // Commit signing: on or off, "ssh" or "openpgp", and the key (an SSH public key file or a GPG key id)
    public const string SIGN_COMMITS = "commit.gpgsign";
    public const string SIGNING_FORMAT = "gpg.format";
    public const string SIGNING_KEY = "user.signingkey";

    // git config --unset exits with 5 when the key wasn't set, which is what was asked for
    private const int NOT_SET_EXIT_CODE = 5;

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
            return unset.ExitCode == NOT_SET_EXIT_CODE ? new GitResult(0, string.Empty, string.Empty) : unset;
        }

        return await GitRunner.RunAsync(Home, new[] { "config", "--global", key, value.Trim() });
    }
}
