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
