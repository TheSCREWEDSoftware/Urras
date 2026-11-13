using System.Diagnostics;
using System.Text;

namespace Urras.Core.Git;

public static class GitRunner
{
    private const int PROGRESS_BUFFER = 256;

    // Raised for every command, possibly from a background thread; quiet commands ran in the background
    public static event Action<string, GitResult, bool>? CommandFinished;

    public static string GitExecutable { get; set; } = "git";

    // Settings added to every command, as -c name=value (see CredentialSetup)
    public static IReadOnlyList<string> ExtraSettings { get; set; } = Array.Empty<string>();

    // How long to wait for another Git to finish with a lock before trying again
    private static readonly TimeSpan LOCK_WAIT = TimeSpan.FromSeconds(3);

    // Commands that can safely run again after a stale lock stopped them; a half-done merge or rebase can't
    private static readonly HashSet<string> Repeatable = new HashSet<string>(StringComparer.Ordinal)
    {
        "fetch", "pull", "push"
    };

    // A lock left by a Git that crashed is removed and a fetch, pull or push run once more
    public static async Task<GitResult> RunAsync(string workingDirectory, IEnumerable<string> arguments,
                                                 string? standardInput = null,
                                                 CancellationToken cancellationToken = default, bool quiet = false,
                                                 IReadOnlyDictionary<string, string>? environment = null,
                                                 Action<string>? progress = null)
    {
        List<string> given = arguments.ToList();
        GitResult result = await RunOnceAsync(workingDirectory, given, standardInput, cancellationToken, quiet,
                                              environment, progress);
        if (result.Success || CommandOf(given) is not { } command || !Repeatable.Contains(command) ||
            !await FreeLockAsync(result.Message, workingDirectory, cancellationToken))
            return result;

        return await RunOnceAsync(workingDirectory, given, standardInput, cancellationToken, quiet, environment,
                                  progress);
    }

    // The Git command itself, past any -c settings and options, so a branch named "push" isn't taken for one
    private static string? CommandOf(List<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == "-c" || arguments[index] == "-C")
            {
                index++;
                continue;
            }

            if (!arguments[index].StartsWith('-'))
                return arguments[index];
        }

        return null;
    }

    // True when the lock in Git's "File exists" message is out of the way and the command is worth running again
    private static async Task<bool> FreeLockAsync(string message, string workingDirectory,
                                                  CancellationToken cancellationToken)
    {
        if (StaleLock.PathIn(message, workingDirectory) is not { } path)
            return false;
        if (StaleLock.TryClear(message, workingDirectory))
            return true;

        // A young lock belongs to a Git still running, such as a fetch started elsewhere; give it a moment
        if (File.Exists(path))
        {
            await Task.Delay(LOCK_WAIT, cancellationToken);
            return true;
        }

        // Git removed its own lock, so it clashed with itself: two remote branches differing only in capitals
        if (RefNameClash.RefIn(message) is not { } failed)
            return true;
        GitResult refs = await RunOnceAsync(workingDirectory, new[] { "for-each-ref", "--format=%(refname)",
                                                                      RefNameClash.REMOTE_REFS },
                                            null, cancellationToken, true, null, null);
        List<string> clashing = RefNameClash.Matching(refs.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries |
                                                                               StringSplitOptions.TrimEntries), failed);
        foreach (string name in clashing)
            await RunOnceAsync(workingDirectory, new[] { "update-ref", "-d", name }, null, cancellationToken, false,
                               null, null);
        return true;
    }

    private static async Task<GitResult> RunOnceAsync(string workingDirectory, IEnumerable<string> arguments,
                                                      string? standardInput, CancellationToken cancellationToken,
                                                      bool quiet, IReadOnlyDictionary<string, string>? environment,
                                                      Action<string>? progress)
    {
        List<string> argumentList = new List<string> { "-c", "core.quotepath=false", "-c", "color.ui=false" };
        foreach (string setting in ExtraSettings)
            argumentList.AddRange(new[] { "-c", setting });
        argumentList.AddRange(arguments);

        ProcessStartInfo startInfo = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // Patches and commit messages go in as UTF-8 without a BOM, whatever the console code page is
            StandardInputEncoding = new UTF8Encoding(false)
        };

        foreach (string argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Never block waiting for a terminal prompt; credential helpers still work
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // Git's and SSH's questions go to Urras, but not from background work, which would ask every few minutes
        if (!quiet && AskPass.Environment is { } askPass)
        {
            foreach (KeyValuePair<string, string> variable in askPass)
                startInfo.Environment[variable.Key] = variable.Value;
        }
        if (environment != null)
        {
            foreach (KeyValuePair<string, string> variable in environment)
                startInfo.Environment[variable.Key] = variable.Value;
        }

        string commandLine = "git " + string.Join(' ', arguments.Select(Quote));
        GitResult result;

        try
        {
            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            // A cancelled clone or fetch stops Git itself, not only the wait for it
            using CancellationTokenRegistration stop = cancellationToken.Register(() => StopProcess(process));

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = progress == null ? process.StandardError.ReadToEndAsync(cancellationToken)
                                                      : ReadWithProgressAsync(process.StandardError, progress);

            if (standardInput != null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            }

            process.StandardInput.Close();

            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(cancellationToken);

            result = new GitResult(process.ExitCode, outputTask.Result, errorTask.Result);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            result = new GitResult(-1, string.Empty, "Could not start git: " + exception.Message);
        }

        CommandFinished?.Invoke(commandLine, result, quiet);
        return result;
    }

    private static void StopProcess(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                                       System.ComponentModel.Win32Exception)
        {
            // Already finished
        }
    }

    // Each progress line goes to progress as Git rewrites it; only other lines are kept, so errors stay readable
    private static async Task<string> ReadWithProgressAsync(StreamReader reader, Action<string> progress)
    {
        StringBuilder kept = new StringBuilder();
        StringBuilder line = new StringBuilder();
        char[] buffer = new char[PROGRESS_BUFFER];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] is '\r' or '\n')
                    Flush();
                else
                    line.Append(buffer[index]);
            }
        }

        Flush();
        return kept.ToString();

        void Flush()
        {
            if (line.Length == 0)
                return;
            string text = line.ToString();
            line.Clear();
            progress(text);
            if (GitProgress.Parse(text) == null)
                kept.AppendLine(text);
        }
    }

    // For file contents such as images, which must not be decoded as text; null when the command fails
    public static async Task<byte[]?> RunBytesAsync(string workingDirectory, IEnumerable<string> arguments)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        try
        {
            using Process process = new Process { StartInfo = startInfo };
            process.Start();
            using MemoryStream output = new MemoryStream();
            Task copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            Task<string> error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(copy, error);
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? output.ToArray() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static Task<GitResult> RunAsync(string workingDirectory, params string[] arguments)
    {
        return RunAsync(workingDirectory, arguments, null, CancellationToken.None);
    }

    private static string Quote(string argument)
    {
        return argument.Contains(' ') ? "\"" + argument + "\"" : argument;
    }
}
