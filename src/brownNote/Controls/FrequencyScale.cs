using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace brownNote.Controls;

public sealed class FrequencyScale : FrameworkElement
{
    private const double MajorTickLength = 5;
    private const double MinorTickLength = 3;
    private const double LabelGap = 2;
    private const double LabelSpacing = 6;
    private const double LabelFontSize = 10;
    private static readonly double[] _decadeSteps = [1, 2, 5];
    private static readonly Duration RangeFadeDuration = TimeSpan.FromMilliseconds(180);

    public static readonly DependencyProperty SliderProperty = DependencyProperty.Register(
        nameof(Slider),
        typeof(LogSlider),
        typeof(FrequencyScale),
        new PropertyMetadata(null, OnSliderChanged));

    private double _renderedMinimum = double.NaN;
    private double _renderedMaximum = double.NaN;
    private double _renderedWidth = double.NaN;

    public FrequencyScale()
    {
        Height = MajorTickLength + LabelGap + LabelFontSize * 1.4;
        SnapsToDevicePixels = true;
    }

    public LogSlider? Slider
    {
        get => (LogSlider?)GetValue(SliderProperty);
        set => SetValue(SliderProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Slider is not { } slider || slider.FrequencyMaximum <= slider.FrequencyMinimum || slider.FrequencyMinimum <= 0)
            return;

        var (left, right) = GetTrackSpan(slider);
        if (right <= left)
            return;

        var minimum = slider.FrequencyMinimum;
        var maximum = slider.FrequencyMaximum;
        var ticks = GetTicks(minimum, maximum);
        var labelMinors = ticks.Count(tick => tick.Major) < 2;
        var tickPen = new Pen((Brush)(TryFindResource("BorderStrongBrush") ?? Brushes.Gray), 1);
        tickPen.Freeze();
        var labelBrush = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray);
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var lastLabelRight = double.NegativeInfinity;

        foreach (var (frequency, major) in ticks)
        {
            var x = Math.Round(left + LogSlider.ToPosition(frequency, minimum, maximum) * (right - left)) + 0.5;
            drawingContext.DrawLine(tickPen, new Point(x, 0), new Point(x, major ? MajorTickLength : MinorTickLength));
            if (!major && !labelMinors)
                continue;

            var text = new FormattedText(
                FormatFrequency(frequency),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                LabelFontSize,
                labelBrush,
                pixelsPerDip);
            var labelLeft = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width));
            if (labelLeft < lastLabelRight + LabelSpacing)
                continue;

            drawingContext.DrawText(text, new Point(labelLeft, MajorTickLength + LabelGap));
            lastLabelRight = labelLeft + text.Width;
        }
    }

    private (double Left, double Right) GetTrackSpan(LogSlider slider)
    {
        if (slider.Template?.FindName("PART_Track", slider) is Track { Thumb: { } thumb } track &&
            track.ActualWidth > 0 &&
            PresentationSource.FromVisual(track) is not null &&
            PresentationSource.FromVisual(this) is not null)
        {
            var inset = thumb.ActualWidth / 2;
            var left = track.TranslatePoint(new Point(inset, 0), this).X;
            var right = track.TranslatePoint(new Point(track.ActualWidth - inset, 0), this).X;
            return (left, right);
        }

        const double fallbackInset = 5.5;
        return (fallbackInset, ActualWidth - fallbackInset);
    }

    private static List<(double Frequency, bool Major)> GetTicks(double minimum, double maximum)
    {
        var ticks = new List<(double, bool)>();
        for (var decade = Math.Pow(10, Math.Floor(Math.Log10(minimum))); decade <= maximum; decade *= 10)
        {
            foreach (var step in _decadeSteps)
            {
                var frequency = decade * step;
                if (frequency >= minimum && frequency <= maximum)
                {
                    ticks.Add((frequency, step == 1));
                }
            }
        }
        return ticks;
    }

    private static string FormatFrequency(double frequency) => frequency >= 1000
        ? $"{(frequency / 1000).ToString("0.#", CultureInfo.CurrentCulture)}k"
        : frequency.ToString("0", CultureInfo.CurrentCulture);

    private static void OnSliderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var scale = (FrequencyScale)d;
        if (e.OldValue is LogSlider oldSlider)
        {
            oldSlider.LayoutUpdated -= scale.Slider_LayoutUpdated;
        }
        if (e.NewValue is LogSlider newSlider)
        {
            newSlider.LayoutUpdated += scale.Slider_LayoutUpdated;
        }
        scale.InvalidateVisual();
    }

    private void Slider_LayoutUpdated(object? sender, EventArgs e)
    {
        if (Slider is not { } slider)
            return;

        if (slider.FrequencyMinimum == _renderedMinimum &&
            slider.FrequencyMaximum == _renderedMaximum &&
            slider.ActualWidth == _renderedWidth)
            return;

        var rangeChanged = !double.IsNaN(_renderedMinimum) &&
            (slider.FrequencyMinimum != _renderedMinimum || slider.FrequencyMaximum != _renderedMaximum);
        if (rangeChanged && IsVisible && SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, RangeFadeDuration));
        }

        _renderedMinimum = slider.FrequencyMinimum;
        _renderedMaximum = slider.FrequencyMaximum;
        _renderedWidth = slider.ActualWidth;
        InvalidateVisual();
    }
}
