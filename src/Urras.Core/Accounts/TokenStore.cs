using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Urras.Core.Accounts;

// Keeps account tokens in the system's credential store, never in Urras's settings file
public interface ITokenStore
{
    // Null when the store can't be used here; the reason says why
    string? Unavailable { get; }
    Task<bool> SaveAsync(string key, string token);
    Task<string?> LoadAsync(string key);
    Task DeleteAsync(string key);
}

public static class TokenStores
{
    public static ITokenStore ForThisSystem()
    {
        return OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new SecretToolStore();
    }
}

// For tests
public sealed class MemoryTokenStore : ITokenStore
{
    private readonly Dictionary<string, string> _tokens = new Dictionary<string, string>();

    public string? Unavailable => null;

    public Task<bool> SaveAsync(string key, string token)
    {
        _tokens[key] = token;
        return Task.FromResult(true);
    }

    public Task<string?> LoadAsync(string key)
    {
        return Task.FromResult(_tokens.GetValueOrDefault(key));
    }

    public Task DeleteAsync(string key)
    {
        _tokens.Remove(key);
        return Task.CompletedTask;
    }
}

// Windows Credential Manager, as generic credentials (visible under "Windows Credentials")
public sealed class WindowsCredentialStore : ITokenStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public string? Unavailable => OperatingSystem.IsWindows() ? null : "Windows Credential Manager is Windows only.";

    public Task<bool> SaveAsync(string key, string token)
    {
        byte[] blob = Encoding.Unicode.GetBytes(token);
        IntPtr buffer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, buffer, blob.Length);
            Credential credential = new Credential
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = key,
                CredentialBlobSize = blob.Length,
                CredentialBlob = buffer,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = "Urras"
            };
            return Task.FromResult(CredWrite(ref credential, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task<string?> LoadAsync(string key)
    {
        if (!CredRead(key, CRED_TYPE_GENERIC, 0, out IntPtr pointer))
            return Task.FromResult<string?>(null);

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            return Task.FromResult<string?>(Marshal.PtrToStringUni(credential.CredentialBlob,
                                                                   credential.CredentialBlobSize / 2));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task DeleteAsync(string key)
    {
        CredDelete(key, CRED_TYPE_GENERIC, 0);
        return Task.CompletedTask;
    }
}

// The Secret Service keyring (GNOME Keyring, KWallet) through the secret-tool command from libsecret
public sealed class SecretToolStore : ITokenStore
{
    private const string PROGRAM = "secret-tool";

    public string? Unavailable => Find() ? null
        : "Install secret-tool (package libsecret-tools or libsecret) so Urras can keep tokens in your keyring.";

    private static bool Find()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        return path != null && path.Split(Path.PathSeparator).Any(folder => File.Exists(Path.Combine(folder, PROGRAM)));
    }

    public async Task<bool> SaveAsync(string key, string token)
    {
        (int code, string _) = await RunAsync(token, "store", "--label=" + key, "service", "urras", "key", key);
        return code == 0;
    }

    public async Task<string?> LoadAsync(string key)
    {
        (int code, string output) = await RunAsync(null, "lookup", "service", "urras", "key", key);
        return code == 0 && output.Length > 0 ? output.TrimEnd('\n') : null;
    }

    public async Task DeleteAsync(string key)
    {
        await RunAsync(null, "clear", "service", "urras", "key", key);
    }

    private static async Task<(int, string)> RunAsync(string? input, params string[] arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(PROGRAM)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        try
        {
            using Process process = Process.Start(start)!;
            if (input != null)
                await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();

            // Both streams are drained together so a chatty error stream can't fill up and stall the program
            Task<string> errors = process.StandardError.ReadToEndAsync();
            string output = await process.StandardOutput.ReadToEndAsync();
            await errors;
            await process.WaitForExitAsync();
            return (process.ExitCode, output);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return (-1, string.Empty);
        }
    }
}
