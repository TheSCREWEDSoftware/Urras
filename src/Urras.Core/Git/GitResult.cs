namespace Urras.Core.Git;

public sealed class GitResult
{
    public GitResult(int exitCode, string output, string error)
    {
        ExitCode = exitCode;
        Output = output;
        Error = error;
    }

    public int ExitCode { get; }
    public string Output { get; }
    public string Error { get; }
    public bool Success => ExitCode == 0;

    // Git writes progress and hints to stderr even on success, so prefer it only for failures
    public string Message => Success ? Output.Trim() : (Error.Trim().Length > 0 ? Error.Trim() : Output.Trim());
}
