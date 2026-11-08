using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Urras.App;

// Old and new versions of an image side by side, with their size in pixels and bytes
public sealed class ImageDiffView : UserControl
{
    private const double BYTES_PER_KILOBYTE = 1024.0;

    private static readonly IBrush OldBrush = new SolidColorBrush(Color.Parse("#FF7B72"));
    private static readonly IBrush NewBrush = new SolidColorBrush(Color.Parse("#7EE787"));
    private static readonly IBrush FrameBrush = new SolidColorBrush(Color.Parse("#14FFFFFF"));

    private readonly Image _oldImage = CreateImage();
    private readonly Image _newImage = CreateImage();
    private readonly TextBlock _oldInfo = CreateInfo();
    private readonly TextBlock _newInfo = CreateInfo();
    private Bitmap? _oldBitmap;
    private Bitmap? _newBitmap;

    public ImageDiffView()
    {
        Grid grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(8)
        };

        AddColumn(grid, 0, "Old", OldBrush, _oldImage, _oldInfo);
        AddColumn(grid, 1, "New", NewBrush, _newImage, _newInfo);
        Content = grid;
    }

    public void Show(byte[]? oldBytes, byte[]? newBytes)
    {
        _oldBitmap?.Dispose();
        _newBitmap?.Dispose();
        _oldBitmap = Load(oldBytes, _oldImage, _oldInfo);
        _newBitmap = Load(newBytes, _newImage, _newInfo);
    }

    public void Clear()
    {
        Show(null, null);
    }

    private static Bitmap? Load(byte[]? bytes, Image image, TextBlock info)
    {
        image.Source = null;
        if (bytes == null)
        {
            info.Text = "No file";
            return null;
        }

        string size = (bytes.Length / BYTES_PER_KILOBYTE).ToString("0.0") + " KB";
        try
        {
            Bitmap bitmap = new Bitmap(new MemoryStream(bytes));
            image.Source = bitmap;
            info.Text = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height} px · {size}";
            return bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            // Unsupported or broken files still show their size
            info.Text = "Can't show this image · " + size;
            return null;
        }
    }

    private static void AddColumn(Grid grid, int column, string title, IBrush brush, Image image, TextBlock info)
    {
        TextBlock header = new TextBlock
        {
            Text = title,
            Foreground = brush,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Border frame = new Border { Background = FrameBrush, Margin = new Thickness(6), Child = image };

        Grid.SetColumn(header, column);
        Grid.SetColumn(frame, column);
        Grid.SetRow(frame, 1);
        Grid.SetColumn(info, column);
        Grid.SetRow(info, 2);
        grid.Children.Add(header);
        grid.Children.Add(frame);
        grid.Children.Add(info);
    }

    private static Image CreateImage()
    {
        // Small images keep their size; large ones shrink to fit
        return new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    }

    private static TextBlock CreateInfo()
    {
        return new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7 };
    }
}
