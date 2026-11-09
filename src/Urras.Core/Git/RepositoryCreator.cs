namespace Urras.Core.Git;

// A new repository as GitHub Desktop makes one: the folder, git init, and a first commit with the chosen files
public sealed record NewRepository(string Folder, string Name, string Description, bool Readme,
                                   string? GitIgnore, string? License);

public static class RepositoryCreator
{
    public const string README = "README.md";
    public const string GITIGNORE = ".gitignore";
    public const string LICENSE = "LICENSE";
    public const string FIRST_COMMIT = "Initial commit";

    // The README's text: the name as a heading, then the description
    public static string ReadmeText(string name, string description)
    {
        string text = "# " + name + "\n";
        return description.Trim().Length > 0 ? text + "\n" + description.Trim() + "\n" : text;
    }

    // A licence template's placeholders filled in, such as [year] and [fullname]
    public static string FillLicense(string template, string fullName, int year)
    {
        return template.Replace("[year]", year.ToString()).Replace("[yyyy]", year.ToString())
                       .Replace("[fullname]", fullName).Replace("[name of copyright owner]", fullName);
    }

    public static async Task<GitResult> CreateAsync(NewRepository repository)
    {
        string folder = repository.Folder;
        if (Directory.Exists(folder) && Directory.Exists(Path.Combine(folder, ".git")))
            return new GitResult(-1, string.Empty, folder + " is already a repository.");

        GitResult init = await GitRepository.InitAsync(folder);
        if (!init.Success)
            return init;

        List<string> files = new List<string>();
        if (repository.Readme)
            files.Add(Write(folder, README, ReadmeText(repository.Name, repository.Description)));
        if (repository.GitIgnore is { Length: > 0 } gitIgnore)
            files.Add(Write(folder, GITIGNORE, gitIgnore));
        if (repository.License is { Length: > 0 } license)
            files.Add(Write(folder, LICENSE, license));
        if (files.Count == 0)
            return init;

        List<string> add = new List<string> { "add", "--" };
        add.AddRange(files);
        GitResult added = await GitRunner.RunAsync(folder, add);
        return added.Success ? await GitRunner.RunAsync(folder, "commit", "-m", FIRST_COMMIT) : added;
    }

    // Kept only if the file isn't there yet, so a folder's own files are never overwritten
    private static string Write(string folder, string name, string text)
    {
        string path = Path.Combine(folder, name);
        if (!File.Exists(path))
            File.WriteAllText(path, text.EndsWith('\n') ? text : text + "\n");
        return name;
    }
}
