using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Urras.Core.Accounts;

namespace Urras.App;

// A round profile picture for an email address, or coloured initials when no picture is known
public sealed class AvatarView : Control
{
    private static readonly IBrush[] InitialsBrushes =
    {
        Brush("#539BF5"), Brush("#57AB5A"), Brush("#B083F0"), Brush("#F0883E"), Brush("#E5534B"),
        Brush("#39C5CF"), Brush("#E275AD"), Brush("#C69026")
    };

    public static readonly StyledProperty<string?> EmailProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(Email));

    public static readonly StyledProperty<string?> PersonNameProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(PersonName));

    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<AvatarView, string?>(nameof(Url));

    private Bitmap? _picture;
    private string? _loaded;

    static AvatarView()
    {
        AffectsRender<AvatarView>(EmailProperty, PersonNameProperty, UrlProperty);
    }

    public AvatarView()
    {
        Width = 18;
        Height = 18;
    }

    // Set once by the main window, so pictures can be found from accounts
    public static AccountManager? Accounts { get; set; }

    public string? Email
    {
        get => GetValue(EmailProperty);
        set => SetValue(EmailProperty, value);
    }

    // Used for the initials
    public string? PersonName
    {
        get => GetValue(PersonNameProperty);
        set => SetValue(PersonNameProperty, value);
    }

    // A picture address given directly, such as for a co-author found on GitHub
    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EmailProperty || change.Property == UrlProperty)
            _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        string? url = Url ?? (Email == null ? null : Accounts?.AvatarUrlFor(Email));
        if (url == _loaded)
            return;

        _loaded = url;
        _picture = null;
        InvalidateVisual();
        if (url == null)
            return;

        Bitmap? picture = await AvatarCache.GetAsync(url);
        if (_loaded == url)
        {
            _picture = picture;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        Rect area = new Rect(0, 0, size, size);
        if (_picture != null)
        {
            using (context.PushClip(new RoundedRect(area, size / 2)))
                context.DrawImage(_picture, area);
            return;
        }

        string key = (Email ?? PersonName ?? "?").ToLowerInvariant();
        IBrush brush = InitialsBrushes[(int)((uint)StableHash(key) % InitialsBrushes.Length)];
        context.DrawEllipse(brush, null, new Point(size / 2, size / 2), size / 2, size / 2);
        FormattedText text = new FormattedText(Initials(PersonName ?? Email ?? "?"),
                                               System.Globalization.CultureInfo.CurrentCulture,
                                               FlowDirection.LeftToRight, Typeface.Default, size * 0.45,
                                               Brushes.White);
        context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
    }

    private static string Initials(string name)
    {
        string[] words = name.Split(new[] { ' ', '.', '_', '-', '@' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "?";
        string initials = words.Length == 1 ? words[0][..1] : words[0][..1] + words[^1][..1];
        return initials.ToUpperInvariant();
    }

    // The same colour for the same person on every run (string.GetHashCode changes between runs)
    private static int StableHash(string text)
    {
        int hash = 17;
        foreach (char character in text)
            hash = hash * 31 + character;
        return hash;
    }

    private static IBrush Brush(string colour)
    {
        return new SolidColorBrush(Color.Parse(colour));
    }
}
