using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class AskPassTests
{
    public static async Task AnswersGitsQuestionsThroughUrras()
    {
        List<string> asked = new List<string>();
        using AskPass.Server server = new AskPass.Server("/path/to/urras", prompt =>
        {
            asked.Add(prompt);
            return Task.FromResult<string?>(prompt.StartsWith("Password") ? "secret" : null);
        });
        Assert.Equal("force", server.Variables["SSH_ASKPASS_REQUIRE"], "SSH asks even with a terminal");
        Assert.Equal("/path/to/urras", server.Variables["GIT_ASKPASS"], "Git runs Urras to ask");

        StringWriter output = new StringWriter();
        int code = await Task.Run(() => AskPass.RunClient("Password for 'https://github.com': ", output,
                                                          server.Pipe, server.Token));
        Assert.Equal(AskPass.ANSWERED, code, "answered");
        Assert.Equal("secret\n", output.ToString(), "printed for Git, with the line end it reads up to");

        StringWriter cancelled = new StringWriter();
        code = await Task.Run(() => AskPass.RunClient("Username for 'https://github.com': ", cancelled,
                                                      server.Pipe, server.Token));
        Assert.Equal(AskPass.NOT_ANSWERED, code, "cancelled");
        Assert.Equal(string.Empty, cancelled.ToString(), "nothing printed");

        code = await Task.Run(() => AskPass.RunClient("Password: ", new StringWriter(), server.Pipe, "wrong"));
        Assert.Equal(AskPass.NOT_ANSWERED, code, "a caller without the token gets nothing");
        Assert.Equal(2, asked.Count, "and isn't even asked about");
    }

    public static void OnlyBackgroundWorkIsLeftOut()
    {
        Assert.True(!AskPass.IsAskPassCall(new[] { "Password: ", "extra" }), "Git passes exactly the question");
        Assert.Equal(AskPass.NOT_ANSWERED, AskPass.RunClient("Password: ", new StringWriter(), null, null),
                     "no Urras to ask");
    }

    // The Windows script, run here with sh and a stand-in for Urras that writes its answer to the file it is given
    public static async Task TheScriptPrintsWhatUrrasWrites()
    {
        if (OperatingSystem.IsWindows())
            return;

        string folder = Path.Combine(Path.GetTempPath(), "urras-askpass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string urras = Path.Combine(folder, "fake urras");
            File.WriteAllText(urras, "#!/bin/sh\nprintf 'answer to %s' \"$1\" > \"$2\"\n");
            string script = Path.Combine(folder, "askpass.sh");
            File.WriteAllText(script, AskPass.WindowsScript(urras));
            File.SetUnixFileMode(urras, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo("sh")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("Username for 'https://github.com': ");
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)!;
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode, "passes on Urras's exit code");
            Assert.Equal("answer to Username for 'https://github.com': ", output, "prints what Urras wrote");
            Assert.Equal(0, Directory.GetFiles(Path.GetTempPath(), "tmp.*").Count(file =>
                File.Exists(file) && File.ReadAllText(file).StartsWith("answer to Username")), "cleans up");
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }
}
