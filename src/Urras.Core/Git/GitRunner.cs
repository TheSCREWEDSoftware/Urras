using System.Diagnostics;
using System.Text;

namespace Urras.Core.Git;

public static class GitRunner
{
    // Raised for every command, possibly from a background thread; quiet commands ran in the background
    public static event Action<string, GitResult, bool>? CommandFinished;

    public static string GitExecutable { get; set; } = "git";

    public static async Task<GitResult> RunAsync(string workingDirectory, IEnumerable<string> arguments,
                                                 string? standardInput = null,
                                                 CancellationToken cancellationToken = default, bool quiet = false,
                                                 IReadOnlyDictionary<string, string>? environment = null)
    {
        List<string> argumentList = new List<string> { "-c", "core.quotepath=false", "-c", "color.ui=false" };
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

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

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
