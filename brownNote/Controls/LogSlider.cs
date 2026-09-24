using System.Windows;
using System.Windows.Controls;

namespace brownNote.Controls;

// Value, Minimum and Maximum hold the thumb position (0 to 1); Frequency is the Hz value it maps to.
public sealed class LogSlider : Slider
{
    private const double StepsPerOctave = 12;

    public static readonly DependencyProperty FrequencyProperty = DependencyProperty.Register(
        nameof(Frequency),
        typeof(double),
        typeof(LogSlider),
        new PropertyMetadata(100d, OnFrequencyChanged, CoerceFrequency));

    public static readonly DependencyProperty FrequencyMinimumProperty = DependencyProperty.Register(
        nameof(FrequencyMinimum),
        typeof(double),
        typeof(LogSlider),
        new PropertyMetadata(1d, OnRangeChanged));

    public static readonly DependencyProperty FrequencyMaximumProperty = DependencyProperty.Register(
        nameof(FrequencyMaximum),
        typeof(double),
        typeof(LogSlider),
        new PropertyMetadata(24000d, OnRangeChanged));

    public static readonly RoutedEvent FrequencyChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(FrequencyChanged),
        RoutingStrategy.Bubble,
        typeof(RoutedPropertyChangedEventHandler<double>),
        typeof(LogSlider));

    private bool _syncing;

    public LogSlider()
    {
        Minimum = 0;
        Maximum = 1;
        IsSnapToTickEnabled = false;
        UpdateSteps();
        SyncPosition();
    }

    public event RoutedPropertyChangedEventHandler<double> FrequencyChanged
    {
        add => AddHandler(FrequencyChangedEvent, value);
        remove => RemoveHandler(FrequencyChangedEvent, value);
    }

    public double Frequency
    {
        get => (double)GetValue(FrequencyProperty);
        set => SetValue(FrequencyProperty, value);
    }

    public double FrequencyMinimum
    {
        get => (double)GetValue(FrequencyMinimumProperty);
        set => SetValue(FrequencyMinimumProperty, value);
    }

    public double FrequencyMaximum
    {
        get => (double)GetValue(FrequencyMaximumProperty);
        set => SetValue(FrequencyMaximumProperty, value);
    }

    public static double ToFrequency(double position, double minimum, double maximum) =>
        minimum * Math.Pow(maximum / minimum, position);

    public static double ToPosition(double frequency, double minimum, double maximum) =>
        maximum > minimum && minimum > 0
            ? Math.Clamp(Math.Log(frequency / minimum) / Math.Log(maximum / minimum), 0, 1)
            : 0;

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (_syncing)
            return;

        var requested = (double)CoerceFrequency(this, ToFrequency(newValue, FrequencyMinimum, FrequencyMaximum));
        _syncing = true;
        try
        {
            Frequency = requested;
        }
        finally
        {
            _syncing = false;
        }

        // A FrequencyChanged handler may have rejected the move; put the thumb back.
        if (Frequency != requested)
        {
            SyncPosition();
        }
    }

    private static object CoerceFrequency(DependencyObject d, object baseValue)
    {
        var slider = (LogSlider)d;
        var rounded = Math.Round((double)baseValue);
        return Math.Min(Math.Max(rounded, slider.FrequencyMinimum), slider.FrequencyMaximum);
    }

    private static void OnFrequencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (LogSlider)d;
        if (!slider._syncing)
        {
            slider.SyncPosition();
        }
        slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(
            (double)e.OldValue,
            (double)e.NewValue,
            FrequencyChangedEvent));
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (LogSlider)d;
        slider.UpdateSteps();
        slider.CoerceValue(FrequencyProperty);
        slider.SyncPosition();
    }

    private void SyncPosition()
    {
        _syncing = true;
        try
        {
            Value = ToPosition(Frequency, FrequencyMinimum, FrequencyMaximum);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateSteps()
    {
        var octaves = FrequencyMaximum > FrequencyMinimum && FrequencyMinimum > 0
            ? Math.Log2(FrequencyMaximum / FrequencyMinimum)
            : 1;
        SmallChange = Math.Min(1, 1 / (StepsPerOctave * octaves));
        LargeChange = Math.Min(1, 1 / octaves);
    }
}
