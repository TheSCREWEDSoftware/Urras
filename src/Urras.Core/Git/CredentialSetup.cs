namespace Urras.Core.Git;

// HTTPS logins go to Git Credential Manager when installed and nothing else is set up; GitHub lets its app in
public static class CredentialSetup
{
    public const string MANAGER_SETTING = "credential.helper=manager";

    // True when Urras turned the manager on for its own commands; the user's Git settings are left as they are
    public static async Task<bool> UseManagerIfNeededAsync(string folder)
    {
        GitResult configured = await GitRunner.RunAsync(folder, new[] { "config", "--get-all", "credential.helper" },
                                                        null, CancellationToken.None, true);
        if (configured.Success && configured.Output.Trim().Length > 0)
            return false;

        GitResult manager = await GitRunner.RunAsync(folder, new[] { "credential-manager", "--version" }, null,
                                                     CancellationToken.None, true);
        if (!manager.Success)
            return false;

        GitRunner.ExtraSettings = GitRunner.ExtraSettings.Append(MANAGER_SETTING).Distinct().ToList();
        return true;
    }
}
