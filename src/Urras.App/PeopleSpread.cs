using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace Urras.App;

// Pictures spread when the pointer reaches them and stay spread over the names, until it leaves both
public static class PeopleSpread
{
    public const string SPREAD_CLASS = "spread";

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("Enabled", typeof(PeopleSpread));

    static PeopleSpread()
    {
        EnabledProperty.Changed.AddClassHandler<ItemsControl>((pictures, change) =>
        {
            if (change.NewValue is not true)
                return;
            pictures.PointerEntered += (_, _) => pictures.Classes.Add(SPREAD_CLASS);
            pictures.AttachedToVisualTree += (_, _) => WatchArea(pictures);
        });
    }

    public static bool GetEnabled(ItemsControl control)
    {
        return control.GetValue(EnabledProperty);
    }

    public static void SetEnabled(ItemsControl control, bool value)
    {
        control.SetValue(EnabledProperty, value);
    }

    // The pictures' parent holds the names too; leaving it folds the pictures back
    private static void WatchArea(ItemsControl pictures)
    {
        // A recycled row is attached again, so each area is watched once
        if (pictures.Parent is not Control area || Watched.TryGetValue(area, out _))
            return;
        Watched.Add(area, pictures);
        area.PointerExited += (_, _) => pictures.Classes.Remove(SPREAD_CLASS);
    }

    private static readonly ConditionalWeakTable<Control, ItemsControl> Watched = new();
}
