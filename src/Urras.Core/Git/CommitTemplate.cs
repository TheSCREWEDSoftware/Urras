namespace Urras.Core.Git;

// A commit message template: lines starting with # are guidance, the rest prefills the message
public sealed class CommitTemplate
{
    private CommitTemplate(string message, string guide)
    {
        Message = message;
        Guide = guide;
    }

    public string Message { get; }
    public string Guide { get; }
    public bool IsEmpty => Message.Length == 0 && Guide.Length == 0;

    public static CommitTemplate Parse(string text)
    {
        List<string> message = new List<string>();
        List<string> guide = new List<string>();

        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith('#'))
                guide.Add(line.TrimStart('#').Trim());
            else
                message.Add(line);
        }

        // Guidance often uses several # levels as headings; blank guide lines are dropped
        return new CommitTemplate(string.Join("\n", message).Trim('\n', ' '),
                                  string.Join("\n", guide.Where(line => line.Length > 0)));
    }
}
