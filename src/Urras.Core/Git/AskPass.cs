using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Urras.Core.Git;

// Git and SSH questions (password, passphrase, host key) answered inside Urras, the way VS Code does it
public static class AskPass
{
    public const string PIPE_VARIABLE = "URRAS_ASKPASS_PIPE";
    public const string TOKEN_VARIABLE = "URRAS_ASKPASS_TOKEN";
    public const int ANSWERED = 0;
    public const int NOT_ANSWERED = 1;

    private const int CONNECT_MILLISECONDS = 10000;
    private const int TOKEN_BYTES = 32;
    private const int MAX_WAITING = 8;

    // The variables that send Git's and SSH's questions to Urras; null until a server runs
    public static IReadOnlyDictionary<string, string>? Environment { get; private set; }

    // Git or SSH started this program to ask a question: one argument, and the variables only Urras sets
    public static bool IsAskPassCall(string[] arguments)
    {
        return arguments.Length == 1 &&
               !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(PIPE_VARIABLE)) &&
               !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(TOKEN_VARIABLE));
    }

    // The helper's side: asks the running Urras, prints the answer for Git, and says whether there was one
    public static int RunClient(string prompt, TextWriter output, string? pipe = null, string? token = null)
    {
        pipe ??= System.Environment.GetEnvironmentVariable(PIPE_VARIABLE);
        token ??= System.Environment.GetEnvironmentVariable(TOKEN_VARIABLE);
        if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(token))
            return NOT_ANSWERED;

        try
        {
            using NamedPipeClientStream client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut,
                                                                           PipeOptions.CurrentUserOnly);
            client.Connect(CONNECT_MILLISECONDS);
            using BinaryWriter writer = new BinaryWriter(client, Encoding.UTF8, true);
            using BinaryReader reader = new BinaryReader(client, Encoding.UTF8, true);
            writer.Write(token);
            writer.Write(prompt);
            writer.Flush();
            if (!reader.ReadBoolean())
                return NOT_ANSWERED;

            output.Write(reader.ReadString() + "\n");
            output.Flush();
            return ANSWERED;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or
                                              UnauthorizedAccessException or EndOfStreamException)
        {
            return NOT_ANSWERED;
        }
    }

    // Urras's side: answers questions with ask, which returns null when the user cancels
    public sealed class Server : IDisposable
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Func<string, Task<string?>> _ask;

        public Server(string helperPath, Func<string, Task<string?>> ask)
        {
            _ask = ask;
            Pipe = "urras-askpass-" + Guid.NewGuid().ToString("N");
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(TOKEN_BYTES));
            Variables = new Dictionary<string, string>
            {
                ["GIT_ASKPASS"] = helperPath,
                ["SSH_ASKPASS"] = helperPath,
                // SSH would otherwise only ask a helper when there is no terminal and a display is set
                ["SSH_ASKPASS_REQUIRE"] = "force",
                [PIPE_VARIABLE] = Pipe,
                [TOKEN_VARIABLE] = Token
            };
            _ = ListenAsync();
        }

        public string Pipe { get; }
        public string Token { get; }
        public IReadOnlyDictionary<string, string> Variables { get; }

        // From now on Git commands Urras runs in the foreground ask through this server
        public void Use()
        {
            Environment = Variables;
        }

        public void Dispose()
        {
            if (ReferenceEquals(Environment, Variables))
                Environment = null;
            _stop.Cancel();
            _stop.Dispose();
        }

        private async Task ListenAsync()
        {
            NamedPipeServerStream next = CreatePipe();
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await next.WaitForConnectionAsync(_stop.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or
                                                      ObjectDisposedException)
                {
                    await next.DisposeAsync();
                    return;
                }

                // The next listens before this one is answered, so the pipe never goes missing between callers
                NamedPipeServerStream current = next;
                next = CreatePipe();
                _ = Task.Run(() => AnswerAsync(current));
            }

            await next.DisposeAsync();
        }

        private NamedPipeServerStream CreatePipe()
        {
            return new NamedPipeServerStream(Pipe, PipeDirection.InOut, MAX_WAITING, PipeTransmissionMode.Byte,
                                             PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        }

        private async Task AnswerAsync(NamedPipeServerStream server)
        {
            await using (server)
            {
                try
                {
                    using BinaryReader reader = new BinaryReader(server, Encoding.UTF8, true);
                    using BinaryWriter writer = new BinaryWriter(server, Encoding.UTF8, true);
                    string token = reader.ReadString();
                    string prompt = reader.ReadString();
                    bool trusted = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token),
                                                                          Encoding.UTF8.GetBytes(Token));
                    string? answer = trusted ? await _ask(prompt) : null;
                    writer.Write(answer != null);
                    if (answer != null)
                        writer.Write(answer);
                    writer.Flush();
                }
                catch (Exception exception) when (exception is IOException or EndOfStreamException or
                                                      ObjectDisposedException)
                {
                    // The helper went away (Git gave up); nothing to answer
                }
            }
        }
    }
}
