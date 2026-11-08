using System.Text.Json.Serialization;

namespace Urras.Core.Assistant;

public enum AssistantKind
{
    Off,

    // OpenAI, OpenRouter, LM Studio, Ollama and anything else speaking the chat completions API
    OpenAiCompatible,
    Anthropic,

    // A command-line agent such as Claude Code, Codex or Gemini CLI, given the request on standard input
    CommandLine
}

// Which AI service Urras asks; the key is kept in the credential store under KEY_NAME
public sealed class AssistantSettings
{
    public const string KEY_NAME = "Urras:Assistant";
    public const string OPENAI_ENDPOINT = "https://api.openai.com/v1";
    public const string ANTHROPIC_ENDPOINT = "https://api.anthropic.com/v1";

    [JsonConverter(typeof(JsonStringEnumConverter<AssistantKind>))]
    public AssistantKind Kind { get; set; } = AssistantKind.Off;

    public string Endpoint { get; set; } = OPENAI_ENDPOINT;
    public string Model { get; set; } = string.Empty;

    // For CommandLine: the program and its arguments, such as: claude -p
    public string Command { get; set; } = string.Empty;

    public bool IsOn => Kind != AssistantKind.Off;
}
