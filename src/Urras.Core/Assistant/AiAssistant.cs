using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Urras.Core.Commands;

namespace Urras.Core.Assistant;

public sealed class AssistantException : Exception
{
    public AssistantException(string message) : base(message)
    {
    }
}

// Sends one request to the chosen AI service and returns its answer as text
public sealed class AiAssistant
{
    // Long diffs are cut, to stay within what services accept
    public const int MAX_INPUT_CHARACTERS = 60000;
    private const int MAX_ANSWER_TOKENS = 2000;
    private const string ANTHROPIC_VERSION = "2023-06-01";

    private readonly AssistantSettings _settings;
    private readonly string? _key;
    private readonly HttpClient _http;

    public AiAssistant(AssistantSettings settings, string? key, HttpClient http)
    {
        _settings = settings;
        _key = key;
        _http = http;
    }

    public async Task<string> AskAsync(string instructions, string input, CancellationToken cancellation = default)
    {
        string request = input.Length > MAX_INPUT_CHARACTERS
            ? input[..MAX_INPUT_CHARACTERS] + "\n[cut: the rest was too long to send]"
            : input;
        string answer = _settings.Kind switch
        {
            AssistantKind.OpenAiCompatible => await OpenAiAsync(instructions, request, cancellation),
            AssistantKind.Anthropic => await AnthropicAsync(instructions, request, cancellation),
            AssistantKind.CommandLine => await CommandLineAsync(instructions, request, cancellation),
            _ => throw new AssistantException("Choose an AI service in Options first.")
        };
        return answer.Trim();
    }

    private async Task<string> OpenAiAsync(string instructions, string input, CancellationToken cancellation)
    {
        object body = new
        {
            model = _settings.Model,
            messages = new object[]
            {
                new { role = "system", content = instructions },
                new { role = "user", content = input }
            }
        };
        using JsonDocument answer = await PostAsync(_settings.Endpoint.TrimEnd('/') + "/chat/completions", body,
                                                    request =>
                                                    {
                                                        if (!string.IsNullOrEmpty(_key))
                                                            request.Headers.Authorization =
                                                                new AuthenticationHeaderValue("Bearer", _key);
                                                    }, cancellation);
        return answer.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content")
                     .GetString() ?? string.Empty;
    }

    private async Task<string> AnthropicAsync(string instructions, string input, CancellationToken cancellation)
    {
        object body = new
        {
            model = _settings.Model,
            max_tokens = MAX_ANSWER_TOKENS,
            system = instructions,
            messages = new object[] { new { role = "user", content = input } }
        };
        string endpoint = string.IsNullOrWhiteSpace(_settings.Endpoint) || _settings.Endpoint.Contains("openai")
            ? AssistantSettings.ANTHROPIC_ENDPOINT
            : _settings.Endpoint.TrimEnd('/');
        using JsonDocument answer = await PostAsync(endpoint + "/messages", body, request =>
        {
            request.Headers.Add("x-api-key", _key ?? string.Empty);
            request.Headers.Add("anthropic-version", ANTHROPIC_VERSION);
        }, cancellation);
        return string.Concat(answer.RootElement.GetProperty("content").EnumerateArray()
                                   .Where(part => part.TryGetProperty("text", out _))
                                   .Select(part => part.GetProperty("text").GetString()));
    }

    private async Task<JsonDocument> PostAsync(string url, object body, Action<HttpRequestMessage> authorize,
                                               CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(_settings.Model))
            throw new AssistantException("Enter the model to use in Options.");

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        authorize(request);
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellation);
            string text = await response.Content.ReadAsStringAsync(cancellation);
            if (!response.IsSuccessStatusCode)
                throw new AssistantException("The AI service answered " + (int)response.StatusCode + ": " +
                                             FirstLine(text));
            return JsonDocument.Parse(text);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or
                                              KeyNotFoundException or InvalidOperationException)
        {
            throw new AssistantException("Couldn't get an answer from the AI service: " + exception.Message);
        }
    }

    private async Task<string> CommandLineAsync(string instructions, string input, CancellationToken cancellation)
    {
        List<string> parts = CustomCommand.Split(_settings.Command);
        if (parts.Count == 0)
            throw new AssistantException("Enter the command to run in Options, such as: claude -p");

        ProcessStartInfo start = new ProcessStartInfo(parts[0])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (string argument in parts.Skip(1))
            start.ArgumentList.Add(argument);

        try
        {
            using Process process = Process.Start(start)!;
            await process.StandardInput.WriteAsync(instructions + "\n\n" + input);
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation);
            if (process.ExitCode != 0)
                throw new AssistantException(parts[0] + " failed: " + FirstLine(await error));
            return await output;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new AssistantException("Couldn't start " + parts[0] + ": " + exception.Message);
        }
    }

    private static string FirstLine(string text)
    {
        string line = text.Split('\n').FirstOrDefault(part => part.Trim().Length > 0)?.Trim() ?? string.Empty;
        const int LONGEST = 200;
        return line.Length > LONGEST ? line[..LONGEST] + "…" : line;
    }
}
