using System.IO.Pipes;

namespace Urras.Core.Settings;

// One Urras at a time: a second start asks the running one to come forward, then closes
public sealed class SingleInstance : IDisposable
{
    private const int HAND_OVER_WAIT_MS = 300;
    private const byte ACTIVATE = 1;

    private readonly CancellationTokenSource _stop = new CancellationTokenSource();

    private SingleInstance()
    {
    }

    // Per user, so two people on one computer each get their own Urras
    public static string PipeName => "Urras.Activate." + Environment.UserName;

    // True when a running Urras answered and was asked to come forward, so this one should close
    public static bool HandOver(string? pipeName = null)
    {
        try
        {
            using NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName ?? PipeName,
                                                                           PipeDirection.Out);
            client.Connect(HAND_OVER_WAIT_MS);
            client.WriteByte(ACTIVATE);
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or
                                              UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Listens for later starts and calls activate for each, until disposed
    public static SingleInstance Listen(Action activate, string? pipeName = null)
    {
        SingleInstance instance = new SingleInstance();
        _ = instance.ServeAsync(activate, pipeName ?? PipeName);
        return instance;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task ServeAsync(Action activate, string pipeName)
    {
        CancellationToken token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream server = new NamedPipeServerStream(
                    pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);
                byte[] message = new byte[1];
                if (await server.ReadAsync(message, token) == 1 && message[0] == ACTIVATE)
                    activate();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // Another Urras holds the name (or it's in a bad state): this one simply doesn't listen
                return;
            }
        }
    }
}
