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
            throw new Exception($"{what}: expected '{expected}', got '{actual}'");
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
        try
        {
            foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}
