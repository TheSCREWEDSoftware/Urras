using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{what}: expected '{Visible(expected)}', got '{Visible(actual)}'");
    }

    // Line endings and tabs written out, so a difference in them shows and the message stays on one line
    public static string Visible(object? value)
    {
        return (value?.ToString() ?? "null").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    public static void Success(GitResult result, string what)
    {
        if (!result.Success)
            throw new Exception($"{what} failed: {result.Message}");
    }
}

// A throwaway repository in the temp folder, deleted on dispose
public sealed class TempRepository : IDisposable
{
    private TempRepository(string path)
    {
        Path = path;
        Repository = new GitRepository(path);
    }

    public string Path { get; }
    public GitRepository Repository { get; }

    public static async Task<TempRepository> CreateAsync()
    {
        string name = "urras-test-" + Guid.NewGuid().ToString("N");
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        Directory.CreateDirectory(path);
        TempRepository temp = new TempRepository(path);

        Assert.Success(await GitRunner.RunAsync(path, "init", "-q", "-b", "main"), "init");
        Assert.Success(await GitRunner.RunAsync(path, "config", "user.name", "Urras Test"), "config name");
        Assert.Success(await GitRunner.RunAsync(path, "config", "user.email", "test@example.com"), "config email");
        Assert.Success(await GitRunner.RunAsync(path, "config", "commit.gpgsign", "false"), "config gpg");
        return temp;
    }

    public void Write(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public async Task CommitAllAsync(string message)
    {
        Assert.Success(await Repository.StageAllAsync(), "stage all");
        Assert.Success(await Repository.CommitAsync(message, false), "commit");
    }

    public void Dispose()
    {
        TestFolders.Delete(Path);
    }
}

public static class TestFolders
{
    // Git makes its object files read-only, which Windows refuses to delete; this clears that first
    public static void Delete(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;

            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the system to clean up; a temporary folder isn't worth failing a test over
        }
    }
}

// Tests run Git with their own settings, so the machine's (core.autocrlf on Windows, for one) can't change results
public static class TestEnvironment
{
    private static readonly Lazy<string> GlobalConfig = new Lazy<string>(Create);

    public static void Prepare()
    {
        _ = GlobalConfig.Value;
    }

    private static string Create()
    {
        string folder = Path.Combine(Path.GetTempPath(), "urras-test-git-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        string config = Path.Combine(folder, "gitconfig");
        File.WriteAllText(config, "[user]\n\tname = Urras Test\n\temail = test@example.com\n" +
                                  "[init]\n\tdefaultBranch = main\n[commit]\n\tgpgsign = false\n" +
                                  "[core]\n\tautocrlf = false\n");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        return config;
    }
}
