using System.Net;
using System.Text;
using Urras.Core.Assistant;
using Urras.Core.Git;

namespace Urras.Core.Tests;

public static class AssistantTests
{
    private sealed class FakeService : HttpMessageHandler
    {
        private readonly string _answer;

        public FakeService(string answer)
        {
            _answer = answer;
        }

        public HttpRequestMessage? Last { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                     CancellationToken cancellationToken)
        {
            Last = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_answer, Encoding.UTF8, "application/json")
            };
        }
    }

    public static async Task AsksOpenAiCompatibleServices()
    {
        FakeService fake = new FakeService("{\"choices\":[{\"message\":{\"content\":\" fix(Git): Do it \\n\"}}]}");
        AssistantSettings settings = new AssistantSettings
        {
            Kind = AssistantKind.OpenAiCompatible, Endpoint = "http://localhost:11434/v1/", Model = "llama"
        };
        string answer = await new AiAssistant(settings, "key", new HttpClient(fake)).AskAsync("rules", "diff");
        Assert.Equal("fix(Git): Do it", answer, "answer trimmed");
        Assert.Equal("http://localhost:11434/v1/chat/completions", fake.Last!.RequestUri!.ToString(), "endpoint");
        Assert.Equal("key", fake.Last.Headers.Authorization!.Parameter, "key sent");
        Assert.True(fake.Body.Contains("\"model\":\"llama\"") && fake.Body.Contains("\"system\""), "request body");
    }

    public static async Task AsksAnthropic()
    {
        FakeService fake = new FakeService("{\"content\":[{\"type\":\"text\",\"text\":\"Explained.\"}]}");
        AssistantSettings settings = new AssistantSettings { Kind = AssistantKind.Anthropic, Model = "a-model" };
        string answer = await new AiAssistant(settings, "secret", new HttpClient(fake)).AskAsync("rules", "diff");
        Assert.Equal("Explained.", answer, "text parts joined");
        Assert.Equal("https://api.anthropic.com/v1/messages", fake.Last!.RequestUri!.ToString(), "default endpoint");
        Assert.True(fake.Last.Headers.GetValues("x-api-key").Single() == "secret", "key header");
        Assert.True(fake.Body.Contains("\"max_tokens\""), "answer length set");
    }

    public static async Task ExplainsWhatIsMissing()
    {
        await ExpectAsync(new AssistantSettings(), "Choose an AI service");
        await ExpectAsync(new AssistantSettings { Kind = AssistantKind.OpenAiCompatible }, "model");
        await ExpectAsync(new AssistantSettings { Kind = AssistantKind.CommandLine }, "command");
        await ExpectAsync(new AssistantSettings { Kind = AssistantKind.CommandLine, Command = "urras-missing-tool" },
                          "Couldn't start");
    }

    private static async Task ExpectAsync(AssistantSettings settings, string message)
    {
        try
        {
            await new AiAssistant(settings, null, new HttpClient(new FakeService("{}"))).AskAsync("a", "b");
            Assert.True(false, "should have failed: " + message);
        }
        catch (AssistantException exception)
        {
            Assert.True(exception.Message.Contains(message), exception.Message);
        }
    }

    public static async Task RunsCommandLineAgents()
    {
        if (OperatingSystem.IsWindows())
            return;

        // cat answers with what it was given, standing in for an agent
        AssistantSettings settings = new AssistantSettings { Kind = AssistantKind.CommandLine, Command = "cat" };
        string answer = await new AiAssistant(settings, null, new HttpClient()).AskAsync("Rules", "Input");
        Assert.Equal("Rules\n\nInput", answer, "request on standard input, answer from standard output");
    }

    public static void BuildsPromptsAndReadsAnswers()
    {
        Assert.True(AssistantPrompts.CommitMessageInstructions("## Type(Scope): Summary").Contains("template"),
                    "template included");
        Assert.True(AssistantPrompts.CommitMessageInput("+x", new[] { "a", "b" }).Contains("- a\n- b"), "recent");
        Assert.Equal("one\ntwo", AssistantPrompts.Unfence("```cs\none\ntwo\n```"), "code fence removed");
        Assert.Equal("plain", AssistantPrompts.Unfence(" plain "), "no fence");

        ConflictFile file = ConflictFile.Parse("a\n<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> b\nz\n")!;
        string input = AssistantPrompts.ConflictInput("f.txt", file.Conflicts.First(), new[] { "a" }, new[] { "z" });
        Assert.True(input.Contains("Current side:\nx") && input.Contains("Incoming side:\ny"), "both sides");
        Assert.True(!input.Contains("<<<<<<<"), "no markers sent");
    }
}
