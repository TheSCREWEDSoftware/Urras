using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Urras.Core.Git;

namespace Urras.App;

// A read-only view of a file at some revision: text with line numbers, or an image
public sealed class FileViewWindow : Window
{
    private const double WINDOW_WIDTH = 1000;
    private const double WINDOW_HEIGHT = 720;

    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8B949E"));
    private static readonly FontFamily MonoFont =
        new FontFamily("Cascadia Mono,Consolas,DejaVu Sans Mono,Liberation Mono,monospace");

    private sealed record NumberedLine(int Number, string Text);

    public FileViewWindow(string title, string path, byte[]? contents)
    {
        Title = title;
        Width = WINDOW_WIDTH;
        Height = WINDOW_HEIGHT;
        Content = contents == null ? Message("This file does not exist at this revision.")
                  : ImageFiles.IsImage(path) ? ImageView(contents)
                                             : TextView(contents);
    }

    private static Control TextView(byte[] contents)
    {
        // A NUL byte early on means a binary file, which has no lines to show
        if (contents.Take(8000).Contains((byte)0))
            return Message("Binary file (" + contents.Length + " bytes).");

        string[] lines = Encoding.UTF8.GetString(contents).Replace("\r\n", "\n").Split('\n');
        List<NumberedLine> numbered = lines.Select((text, index) => new NumberedLine(index + 1, text)).ToList();
        if (numbered.Count > 1 && numbered[^1].Text.Length == 0)
            numbered.RemoveAt(numbered.Count - 1);

        return new ListBox { ItemsSource = numbered, ItemTemplate = new FuncDataTemplate<NumberedLine>(Row) };
    }

    private static Control Row(NumberedLine line, INameScope scope)
    {
        Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*") };
        TextBlock number = new TextBlock
        {
            Text = line.Number.ToString(),
            Foreground = MutedBrush,
            FontFamily = MonoFont,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 12, 0)
        };
        TextBlock text = new TextBlock { Text = line.Text.Replace("\t", "    "), FontFamily = MonoFont };
        Grid.SetColumn(text, 1);
        grid.Children.Add(number);
        grid.Children.Add(text);
        return grid;
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
