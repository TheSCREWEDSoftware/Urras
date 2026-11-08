using Avalonia.Media;

namespace Urras.App;

public sealed class DiffLine
{
    private const int MAX_LINES = 20000;

    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.Parse("#7EE787"));
    private static readonly IBrush RemovedBrush = new SolidColorBrush(Color.Parse("#FF7B72"));
    private static readonly IBrush HunkBrush = new SolidColorBrush(Color.Parse("#79C0FF"));
    private static readonly IBrush MetaBrush = new SolidColorBrush(Color.Parse("#8B949E"));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#E6EDF3"));

    private DiffLine(string text, IBrush brush)
    {
        Text = text;
        Brush = brush;
    }

    public string Text { get; }
    public IBrush Brush { get; }

    public static List<DiffLine> FromText(string text)
    {
        List<DiffLine> lines = new List<DiffLine>();
        string[] rawLines = text.Replace("\r\n", "\n").Split('\n');

        foreach (string rawLine in rawLines)
        {
            if (lines.Count >= MAX_LINES)
            {
                lines.Add(new DiffLine("… diff truncated (" + rawLines.Length + " lines)", MetaBrush));
                break;
            }

            string line = rawLine.Replace("\t", "    ");
            lines.Add(new DiffLine(line, BrushFor(line)));
        }

        return lines;
    }

    private static IBrush BrushFor(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) ||
            line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal))
            return MetaBrush;
        if (line.StartsWith("@@", StringComparison.Ordinal))
            return HunkBrush;
        if (line.StartsWith('+'))
            return AddedBrush;
        if (line.StartsWith('-'))
            return RemovedBrush;

        return NormalBrush;
    }
}
