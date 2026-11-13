using System.ComponentModel;
using System.Diagnostics;

namespace Urras.App;

// Opens folders, files, terminals and editors with the system's own programs
public static class SystemShell
{
    public static string? OpenFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
            return Start("explorer.exe", folder);
        if (OperatingSystem.IsMacOS())
            return Start("open", folder);
        return Start("xdg-open", folder);
    }

    // Opens the folder with the file selected where the system supports it
    public static string? ShowFile(string file)
    {
        // Explorer expects /select,"path" exactly, which ArgumentList would quote differently
        if (OperatingSystem.IsWindows())
            return Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + file + "\"" });
        if (OperatingSystem.IsMacOS())
            return Start("open", "-R", file);
        return OpenFolder(Path.GetDirectoryName(file) ?? file);
    }

    // Opens a web page; addresses from platforms are checked first, so a link can't start a local program
    public static string? OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps &&
                                                                     uri.Scheme != Uri.UriSchemeHttp))
            return "Not a web address: " + url;
        return OpenFile(uri.AbsoluteUri);
    }

    // Opens a file with its default program
    public static string? OpenFile(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return exception.Message;
        }
    }

    public static string? OpenTerminal(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows Terminal when installed, otherwise a classic command prompt
            if (Start("wt.exe", "-d", folder) == null)
                return null;
            return Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = folder, UseShellExecute = true });
        }

        if (OperatingSystem.IsMacOS())
            return Start("open", "-a", "Terminal", folder);

        foreach (string terminal in new[] { "x-terminal-emulator", "gnome-terminal", "konsole", "xterm" })
        {
            if (Start(new ProcessStartInfo(terminal) { WorkingDirectory = folder, UseShellExecute = false }) == null)
                return null;
        }

        return "No terminal program was found.";
    }

    public static string? OpenInVisualStudioCode(string path)
    {
        // "code" is a .cmd script on Windows, so it has to go through the command prompt
        if (OperatingSystem.IsWindows())
            return Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", "code", path },
                CreateNoWindow = true,
                UseShellExecute = false
            });
        return Start("code", path);
    }

    public const string DEFAULT = "Default";

    // Editors Urras knows how to start: name shown, program, and whether it is a .cmd script on Windows
    private static readonly (string Name, string Windows, string Unix, bool Script)[] KnownEditors =
    {
        ("Visual Studio Code", "code", "code", true),
        ("Visual Studio Code Insiders", "code-insiders", "code-insiders", true),
        ("VSCodium", "codium", "codium", true),
        ("Cursor", "cursor", "cursor", true),
        ("Windsurf", "windsurf", "windsurf", true),
        ("Visual Studio", "devenv", "", false),
        ("JetBrains Rider", "rider64", "rider", false),
        ("IntelliJ IDEA", "idea64", "idea", false),
        ("CLion", "clion64", "clion", false),
        ("Notepad++", "notepad++", "", false),
        ("Sublime Text", "subl", "subl", false),
        ("Zed", "zed", "zed", false)
    };

    public static IReadOnlyList<string> EditorNames => KnownEditors
        .Where(editor => (OperatingSystem.IsWindows() ? editor.Windows : editor.Unix).Length > 0)
        .Select(editor => editor.Name).Append(Urras.Core.Settings.ProgramChoice.CUSTOM).ToList();

    public static IReadOnlyList<string> ShellNames => OperatingSystem.IsWindows()
        ? new[] { DEFAULT, "Command Prompt", "PowerShell", "PowerShell 7", "Git Bash", "WSL", "Windows Terminal",
                  Urras.Core.Settings.ProgramChoice.CUSTOM }
        : new[] { DEFAULT, Urras.Core.Settings.ProgramChoice.CUSTOM };

    // Editors that open files only: given a folder, Notepad++ opens every file in it
    private static readonly HashSet<string> FileOnlyEditors = new HashSet<string> { "Notepad++" };

    // Opens a folder or file in the chosen editor
    public static string? OpenInEditor(Urras.Core.Settings.ProgramChoice editor, string path)
    {
        if (editor.IsCustom)
            return StartCustom(editor, path);
        if (FileOnlyEditors.Contains(editor.Name) && Directory.Exists(path))
            return editor.Name + " opens files, not whole repositories: right-click a file and choose Open in " +
                   editor.Name + ", or pick another editor for repositories in Options.";

        (string Name, string Windows, string Unix, bool Script) known =
            KnownEditors.FirstOrDefault(item => item.Name == editor.Name);
        if (known.Name == null)
            return OpenInVisualStudioCode(path);

        // The copy found where it installs works even when its folder isn't on PATH (such as a Toolbox install)
        if (ProgramIcons.Find(known.Name) is { } installed)
            return Start(installed, path);

        string program = OperatingSystem.IsWindows() ? known.Windows : known.Unix;
        if (OperatingSystem.IsWindows() && known.Script)
            return Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/c", program, path },
                CreateNoWindow = true,
                UseShellExecute = false
            });
        return Start(program, path);
    }

    // Opens a terminal in the folder with the chosen shell
    public static string? OpenShell(Urras.Core.Settings.ProgramChoice shell, string folder)
    {
        if (shell.IsCustom)
            return StartCustom(shell, folder);
        if (!OperatingSystem.IsWindows())
            return OpenTerminal(folder);

        return shell.Name switch
        {
            "Command Prompt" => Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = folder,
                                                                         UseShellExecute = true }),
            "PowerShell" => Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = folder,
                                                                            UseShellExecute = true }),
            "PowerShell 7" => Start(new ProcessStartInfo(ProgramIcons.Find(shell.Name) ?? "pwsh.exe")
            {
                WorkingDirectory = folder,
                UseShellExecute = true
            }),
            // A Linux shell in the same folder, which WSL reaches through /mnt
            "WSL" => Start(new ProcessStartInfo("wsl.exe", "--cd \"" + folder + "\"") { UseShellExecute = true }),
            "Git Bash" => Start(GitBash(), "--cd=" + folder),
            "Windows Terminal" => Start("wt.exe", "-d", folder),
            _ => OpenTerminal(folder)
        };
    }

    private static string GitBash()
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string bash = Path.Combine(programFiles, "Git", "git-bash.exe");
        return File.Exists(bash) ? bash : "git-bash.exe";
    }

    private static string? StartCustom(Urras.Core.Settings.ProgramChoice program, string target)
    {
        if (string.IsNullOrWhiteSpace(program.Path))
            return "Choose the program's path in Options first.";

        ProcessStartInfo start = new ProcessStartInfo(program.Path)
        {
            UseShellExecute = false,
            WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? string.Empty
        };
        foreach (string argument in program.ArgumentsFor(target))
            start.ArgumentList.Add(argument);
        return Start(start);
    }

    private static string? Start(string program, params string[] arguments)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return Start(startInfo);
    }

    private static string? Start(ProcessStartInfo startInfo)
    {
        try
        {
            using Process? process = Process.Start(startInfo);
            return process == null ? "Could not start " + startInfo.FileName + "." : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return "Could not start " + startInfo.FileName + ": " + exception.Message;
        }
    }
}
