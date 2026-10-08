using System.Windows;
using System.Windows.Media;

namespace brownNote.Helpers;

public static class KeyAccent
{
    public static readonly DependencyProperty BrushProperty = DependencyProperty.RegisterAttached(
        "Brush",
        typeof(Brush),
        typeof(KeyAccent),
        new PropertyMetadata(null));

    public static void SetBrush(DependencyObject element, Brush? value) =>
        element.SetValue(BrushProperty, value);

    public static Brush? GetBrush(DependencyObject element) =>
        (Brush?)element.GetValue(BrushProperty);
}
