namespace Urras.Core.Git;

// Reading and saving a conflicted file for the conflict view
public partial class GitRepository
{
    public async Task<ConflictFile?> GetConflictAsync(string path)
    {
        string fullPath = System.IO.Path.Combine(Path, path);
        if (!File.Exists(fullPath))
            return null;

        // Read as UTF-8 and write back the same way; other encodings are left to an external editor
        return ConflictFile.Parse(await File.ReadAllTextAsync(fullPath));
    }

    public async Task<GitResult> SaveResolvedAsync(string path, ConflictFile file)
    {
        if (!file.IsResolved)
            return new GitResult(1, string.Empty, "Choose a side for every conflict first.");

        await File.WriteAllTextAsync(System.IO.Path.Combine(Path, path), file.Render(),
                                     new System.Text.UTF8Encoding(false));
        return await MarkResolvedAsync(new[] { path });
    }
}
