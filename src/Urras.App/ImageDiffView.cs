using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Urras.App;

// Old and new versions of an image: side by side, as a swipe or as an onion skin, with sizes
public sealed class ImageDiffView : UserControl
{
    private const double BYTES_PER_KILOBYTE = 1024.0;

    private static IBrush OldBrush => ThemeBrushes.Get(Urras.Core.Settings.ThemeColours.DIFF_REMOVED_TEXT);
    private static IBrush NewBrush => ThemeBrushes.Get(Urras.Core.Settings.ThemeColours.DIFF_ADDED_TEXT);
    private static readonly IBrush FrameBrush = new SolidColorBrush(Color.Parse("#14FFFFFF"));

    private enum Mode
    {
        SideBySide,
        Swipe,
        OnionSkin
    }

    // Shared by both diff panes and kept while Urras runs
    private static Mode _mode = Mode.SideBySide;

    private readonly Image _oldImage = CreateImage();
    private readonly Image _newImage = CreateImage();
    private readonly Image _overlayOld = CreateImage();
    private readonly Image _overlayNew = CreateImage();
    private readonly Border _overlayNewHost;
    private readonly Grid _sideBySide;
    private readonly Panel _overlay;
    private readonly TextBlock _oldInfo = CreateInfo();
    private readonly TextBlock _newInfo = CreateInfo();
    private readonly ComboBox _modeBox;
    private readonly Slider _slider;
    private Bitmap? _oldBitmap;
    private Bitmap? _newBitmap;

    public ImageDiffView()
    {
        _modeBox = new ComboBox
        {
            ItemsSource = new[] { "Side by side", "Swipe", "Onion skin" },
            SelectedIndex = (int)_mode,
            Width = 140
        };
        _slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0.5,
            Width = 240,
            Margin = new Thickness(12, 0, 0, 0)
        };
        ToolTip.SetTip(_slider, "Drag to compare the old and new images");
        StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 6) };
        tools.Children.Add(_modeBox);
        tools.Children.Add(_slider);

        _sideBySide = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        AddColumn(_sideBySide, 0, "Old", OldBrush, _oldImage);
        AddColumn(_sideBySide, 1, "New", NewBrush, _newImage);

        _overlayNewHost = new Border { Child = _overlayNew };
        _overlay = new Panel { Margin = new Thickness(6) };
        _overlay.Children.Add(new Border { Background = FrameBrush });
        _overlay.Children.Add(_overlayOld);
        _overlay.Children.Add(_overlayNewHost);

        Grid info = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(_newInfo, 1);
        info.Children.Add(_oldInfo);
        info.Children.Add(_newInfo);

        // Both views fill the space left, so side by side always splits the full width (docked, an empty side shrank)
        Panel views = new Panel();
        views.Children.Add(_sideBySide);
        views.Children.Add(_overlay);
        DockPanel root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(tools, Dock.Top);
        DockPanel.SetDock(info, Dock.Bottom);
        root.Children.Add(tools);
        root.Children.Add(info);
        root.Children.Add(views);
        Content = root;

        _modeBox.SelectionChanged += (_, _) =>
        {
            _mode = (Mode)Math.Max(0, _modeBox.SelectedIndex);
            UpdateMode();
        };
        _slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                UpdateOverlay();
        };
        _overlay.SizeChanged += (_, _) => UpdateOverlay();
        UpdateMode();
    }

    public void Show(byte[]? oldBytes, byte[]? newBytes)
    {
        _oldBitmap?.Dispose();
        _newBitmap?.Dispose();
        _oldBitmap = Load(oldBytes, _oldInfo);
        _newBitmap = Load(newBytes, _newInfo);
        _oldImage.Source = _overlayOld.Source = _oldBitmap;
        _newImage.Source = _overlayNew.Source = _newBitmap;

        // The other pane may have changed the mode
        _modeBox.SelectedIndex = (int)_mode;
        UpdateMode();
    }

    public void Clear()
    {
        Show(null, null);
    }

    private void UpdateMode()
    {
        _sideBySide.IsVisible = _mode == Mode.SideBySide;
        _overlay.IsVisible = _mode != Mode.SideBySide;
        _slider.IsVisible = _mode != Mode.SideBySide;
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        double value = _slider.Value;
        if (_mode == Mode.OnionSkin)
        {
            _overlayNewHost.Opacity = value;
            _overlayNewHost.Clip = null;
            return;
        }

        // Swipe: the new image shows to the left of the slider's position, the old one to the right
        _overlayNewHost.Opacity = 1;
        Rect bounds = _overlay.Bounds;
        _overlayNewHost.Clip = new RectangleGeometry(new Rect(0, 0, bounds.Width * value, bounds.Height));
    }

    private static Bitmap? Load(byte[]? bytes, TextBlock info)
    {
        if (bytes == null)
        {
            info.Text = "No file";
            return null;
        }

        string size = (bytes.Length / BYTES_PER_KILOBYTE).ToString("0.0") + " KB";
        try
        {
            Bitmap bitmap = new Bitmap(new MemoryStream(bytes));
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

    private static void AddColumn(Grid grid, int column, string title, IBrush brush, Image image)
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
        grid.Children.Add(header);
        grid.Children.Add(frame);
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
