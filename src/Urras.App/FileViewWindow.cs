using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Urras.Core.Git;
using Urras.Core.Syntax;

namespace Urras.App;

// A read-only view of a file at some revision: text with line numbers, or an image
public sealed class FileViewWindow : Window
{
    // A zero byte in the start of a file means it isn't text
    private const int BINARY_CHECK_BYTES = 8000;

    private const double WINDOW_WIDTH = 1000;
    private const double WINDOW_HEIGHT = 720;

    private static IBrush MutedBrush => ThemeBrushes.MutedText;
    private static readonly FontFamily MonoFont =
        new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace");

    private sealed record NumberedLine(int Number, IReadOnlyList<TextSegment> Segments);

    public FileViewWindow(string title, string path, byte[]? contents)
    {
        Title = title;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        Content = contents == null ? Message("This file does not exist at this revision.")
                  : ImageFiles.IsImage(path) ? ImageView(contents)
                                             : TextView(path, contents);
    }

    private static Control TextView(string path, byte[] contents)
    {
        // A NUL byte early on means a binary file, which has no lines to show
        if (contents.Take(BINARY_CHECK_BYTES).Contains((byte)0))
            return Message("Binary file (" + contents.Length + " bytes).");

        List<string> lines = Encoding.UTF8.GetString(contents).Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 1 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        ListBox list = new ListBox
        {
            ItemsSource = Numbered(lines, null),
            ItemTemplate = new FuncDataTemplate<NumberedLine>(Row)
        };
        _ = ColourAsync(list, path, lines);
        return list;
    }

    // The file shows at once in plain text; colouring a long one takes a moment, so it follows from the background
    private static async Task ColourAsync(ListBox list, string path, List<string> lines)
    {
        List<SyntaxColour[]>? colours = await Task.Run(() => CodeColourer.For(path)?.ColourLines(lines));
        if (colours != null)
            list.ItemsSource = Numbered(lines, colours);
    }

    private static List<NumberedLine> Numbered(List<string> lines, List<SyntaxColour[]>? colours)
    {
        return lines.Select((text, index) => new NumberedLine(index + 1, Segments(text, colours?[index]))).ToList();
    }

    private static Control Row(NumberedLine? line, INameScope scope)
    {
        // A list recycling its rows can ask for a row with no item
        if (line == null)
            return new Panel();

        Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*") };
        TextBlock number = new TextBlock
        {
            Text = line.Number.ToString(),
            Foreground = MutedBrush,
            FontFamily = MonoFont,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 12, 0)
        };
        SegmentedTextBlock text = new SegmentedTextBlock { Segments = line.Segments, FontFamily = MonoFont };
        Grid.SetColumn(text, 1);
        grid.Children.Add(number);
        grid.Children.Add(text);
        return grid;
    }

    // Runs of one syntax colour; tabs become four spaces
    private static IReadOnlyList<TextSegment> Segments(string text, SyntaxColour[]? kinds)
    {
        List<TextSegment> segments = new List<TextSegment>();
        int start = 0;
        for (int i = 1; i <= text.Length; ++i)
        {
            if (i < text.Length && (kinds == null || kinds[i] == kinds[start]))
                continue;

            IBrush? brush = kinds == null ? null : DiffRow.ColourBrush(kinds[start]);
            segments.Add(new TextSegment(text[start..i].Replace("\t", new string(' ', DiffRow.TabWidth)), brush));
            start = i;
        }

        return segments;
    }

    private static Control ImageView(byte[] contents)
    {
        try
        {
            Bitmap bitmap = new Bitmap(new MemoryStream(contents));
            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Margin = new Thickness(12)
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            return Message("Can't show this image.");
        }
    }

    private static Control Message(string text)
    {
        return new TextBlock
        {
            Text = text,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
