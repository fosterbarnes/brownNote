using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace brownNote.Helpers;

public enum AudioMetric
{
    Percentage,
    WholePercentage,
    Hertz,
    Count
}

public static class SliderMetric
{
    public static readonly DependencyProperty MetricProperty = DependencyProperty.RegisterAttached(
        "Metric",
        typeof(AudioMetric),
        typeof(SliderMetric),
        new PropertyMetadata(AudioMetric.Hertz));

    public static void SetMetric(DependencyObject element, AudioMetric value) =>
        element.SetValue(MetricProperty, value);

    public static AudioMetric GetMetric(DependencyObject element) =>
        (AudioMetric)element.GetValue(MetricProperty);
}

public sealed class AudioMetricConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double value || values[1] is not AudioMetric metric)
        {
            return string.Empty;
        }

        return metric switch
        {
            AudioMetric.Percentage => value.ToString("P0", culture),
            AudioMetric.WholePercentage => $"{value.ToString("N0", culture)}%",
            AudioMetric.Hertz => $"{value.ToString("N0", culture)} Hz",
            AudioMetric.Count => $"{value.ToString("N0", culture)} voices",
            _ => string.Empty
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
