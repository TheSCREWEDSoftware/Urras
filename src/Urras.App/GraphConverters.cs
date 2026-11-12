using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Urras.Core.Git;

namespace Urras.App;

// Bindings for the commit graph: a lane's colour for badges, and where the author's picture sits on the graph
public static class GraphConverters
{
    public static readonly IValueConverter LaneBrush = new FuncValueConverter<int, Avalonia.Media.IBrush>(
        lane => GraphView.LaneBrush(lane));

    // A faint fill of the lane's colour behind a badge's text
    private const byte TINT_ALPHA = 0x26;

    public static readonly IValueConverter LaneTint = new FuncValueConverter<int, Avalonia.Media.IBrush>(lane =>
    {
        Avalonia.Media.Color colour = ((Avalonia.Media.ISolidColorBrush)GraphView.LaneBrush(lane)).Color;
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(TINT_ALPHA, colour.R, colour.G,
                                                                                colour.B));
    });

    // The picture is centred on the commit's ring
    public static readonly IValueConverter AvatarMargin = new FuncValueConverter<GraphRow?, Thickness>(
        row => row == null ? default : new Thickness(GraphView.X(row.Lane) - GraphView.AVATAR_SIZE / 2, 0, 0, 0));
}
