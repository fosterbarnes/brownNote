using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using brownNote.Audio;
using NAudio.Dsp;

namespace brownNote.Controls;

public enum VisualizerMode
{
    Waveform,
    Spectrum,
    Meter
}

public sealed class NoiseVisualizer : FrameworkElement
{
    private const int SampleRate = BrownNoiseProvider.SampleRate;
    private const double LevelTimeConstant = 0.35;
    private const int ScopeWindow = SampleRate / 10;
    private const int FftExponent = 13;
    private const int FftLength = 1 << FftExponent;
    private const double MinimumFrequency = 10;
    private const double MaximumFrequency = SampleRate / 2.0;
    private const double SpectrumFloor = -120;
    private const double SpectrumCeiling = 0;
    private const double SpectrumTimeConstant = 0.12;
    private const double ResponseFloor = -60;
    private const double MeterFloor = -60;
    private const double MeterCeiling = 6;
    private const int MeterWindow = SampleRate / 20;
    private const double MeterTimeConstant = 0.3;
    private const double PeakHoldSeconds = 1.5;
    private const double PeakFallRate = 20;
    private const double LabelFontSize = 11;
    private static readonly double[] _frequencyGrid = [10, 100, 1000, 10000];
    private static readonly double[] _meterTicks = [-60, -48, -36, -24, -12, -6, 0, 6];

    private static readonly Typeface _labelTypeface = new("Segoe UI");

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, Brush> _brushes = [];
    private readonly Dictionary<(string Key, double Thickness), Pen> _pens = [];
    private readonly float[] _samples = new float[Math.Max(FftLength, ScopeWindow * 2)];
    private readonly Complex[] _fft = new Complex[FftLength];
    private double[] _spectrum = [];
    private VisualizerMode _mode;
    private bool _isActive;
    private bool _isRendering;
    private double _lastFrameTime;
    private double _level;
    private double _playhead;
    private long _lastWritten;
    private double _playheadLag = SampleRate / 20.0;
    private double _rmsDb = MeterFloor;
    private double _peakDb = MeterFloor;
    private double _holdDb = MeterFloor;
    private double _holdTime;
    private double _highPassCutoff = GeneratedAudioPlayer.DefaultHighPassCutoff;
    private double _lowPassCutoff = GeneratedAudioPlayer.DefaultLowPassCutoff;

    public NoiseVisualizer()
    {
        ClipToBounds = true;
        IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
        Unloaded += (_, _) => SetRendering(false);
        Loaded += (_, _) => UpdateRenderingSubscription();
    }

    public AudioTap? Tap { get; set; }

    public VisualizerMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            InvalidateVisual();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            UpdateRenderingSubscription();
        }
    }

    public double HighPassCutoff
    {
        get => _highPassCutoff;
        set
        {
            _highPassCutoff = value;
            InvalidateVisual();
        }
    }

    public double LowPassCutoff
    {
        get => _lowPassCutoff;
        set
        {
            _lowPassCutoff = value;
            InvalidateVisual();
        }
    }

    private void UpdateRenderingSubscription()
    {
        SetRendering(IsVisible && (_isActive || _level > 0));
    }

    private void SetRendering(bool enabled)
    {
        if (enabled == _isRendering)
        {
            return;
        }

        _isRendering = enabled;
        if (enabled)
        {
            _lastFrameTime = _clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = Math.Min(now - _lastFrameTime, 0.1);
        _lastFrameTime = now;

        _level += ((_isActive ? 1 : 0) - _level) * Smoothing(elapsed, LevelTimeConstant);
        if (!_isActive && _level < 0.002)
        {
            _level = 0;
            SetRendering(false);
        }

        if (Tap is not null)
        {
            AdvancePlayhead(Tap, elapsed);
            if (_mode == VisualizerMode.Spectrum)
            {
                UpdateSpectrum(Tap, elapsed);
            }
            else if (_mode == VisualizerMode.Meter)
            {
                UpdateMeter(Tap, elapsed, now);
            }
        }

        InvalidateVisual();
    }

    private void AdvancePlayhead(AudioTap tap, double elapsed)
    {
        var written = tap.Position;
        var burst = written - _lastWritten;
        _lastWritten = written;
        if (burst > 0)
        {
            _playheadLag = Math.Max(_playheadLag * 0.995, Math.Min(burst * 1.5, SampleRate / 2.0));
        }

        var target = written - _playheadLag;
        _playhead += elapsed * SampleRate;
        if (Math.Abs(target - _playhead) > SampleRate / 4.0)
        {
            _playhead = target;
        }
        else
        {
            _playhead += (target - _playhead) * Smoothing(elapsed, 0.5);
        }
        _playhead = Math.Clamp(_playhead, 0, written);
    }

    private long PlayheadPosition => (long)_playhead;

    private static double Smoothing(double elapsed, double timeConstant)
    {
        return 1 - Math.Exp(-elapsed / timeConstant);
    }

    private void UpdateSpectrum(AudioTap tap, double elapsed)
    {
        var columns = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        if (_spectrum.Length != columns)
        {
            _spectrum = new double[columns];
        }

        tap.CopyLatest(PlayheadPosition, _samples.AsSpan(0, FftLength));
        for (var index = 0; index < FftLength; index++)
        {
            _fft[index].X = (float)(_samples[index] * FastFourierTransform.HannWindow(index, FftLength));
            _fft[index].Y = 0;
        }
        FastFourierTransform.FFT(true, FftExponent, _fft);

        var binWidth = (double)SampleRate / FftLength;
        var smoothing = Smoothing(elapsed, SpectrumTimeConstant);
        for (var column = 0; column < columns; column++)
        {
            var startBin = FrequencyAt(column, columns) / binWidth;
            var endBin = FrequencyAt(column + 1, columns) / binWidth;
            double magnitude;
            if ((int)endBin <= (int)startBin)
            {
                magnitude = InterpolatedMagnitude((startBin + endBin) / 2);
            }
            else
            {
                magnitude = 0;
                for (var bin = (int)startBin; bin <= (int)endBin && bin < FftLength / 2; bin++)
                {
                    magnitude = Math.Max(magnitude, Magnitude(bin));
                }
            }

            var decibels = 20 * Math.Log10(Math.Max(magnitude * 4, 1e-12));
            var target = Math.Clamp((decibels - SpectrumFloor) / (SpectrumCeiling - SpectrumFloor), 0, 1);
            _spectrum[column] += (target - _spectrum[column]) * smoothing;
        }
    }

    private double Magnitude(int bin)
    {
        var value = _fft[bin];
        return Math.Sqrt(value.X * value.X + value.Y * value.Y);
    }

    private double InterpolatedMagnitude(double bin)
    {
        var lower = Math.Clamp((int)bin, 0, FftLength / 2 - 2);
        var fraction = Math.Clamp(bin - lower, 0, 1);
        return Magnitude(lower) * (1 - fraction) + Magnitude(lower + 1) * fraction;
    }

    private static double FrequencyAt(double column, int columns)
    {
        return MinimumFrequency * Math.Pow(MaximumFrequency / MinimumFrequency, column / columns);
    }

    private void UpdateMeter(AudioTap tap, double elapsed, double now)
    {
        var window = _samples.AsSpan(0, MeterWindow);
        tap.CopyLatest(PlayheadPosition, window);
        double sumOfSquares = 0;
        double peak = 0;
        foreach (var sample in window)
        {
            sumOfSquares += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var rmsDb = 10 * Math.Log10(Math.Max(sumOfSquares / MeterWindow, 1e-12));
        var peakDb = 20 * Math.Log10(Math.Max(peak, 1e-6));
        _rmsDb += (rmsDb - _rmsDb) * Smoothing(elapsed, MeterTimeConstant);
        _peakDb = Math.Max(peakDb, _peakDb - PeakFallRate * elapsed);
        if (peakDb >= _holdDb)
        {
            _holdDb = peakDb;
            _holdTime = now;
        }
        else if (now - _holdTime > PeakHoldSeconds)
        {
            _holdDb = Math.Max(peakDb, _holdDb - PeakFallRate * elapsed);
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        switch (_mode)
        {
            case VisualizerMode.Waveform:
                RenderWaveform(drawingContext);
                break;
            case VisualizerMode.Spectrum:
                RenderSpectrum(drawingContext);
                break;
            case VisualizerMode.Meter:
                RenderMeter(drawingContext);
                break;
        }
    }

    private void RenderWaveform(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var middle = height / 2;
        drawingContext.DrawLine(Pen("BorderSubtleBrush", 1), new Point(0, middle), new Point(width, middle));
        if (Tap is null || _level <= 0)
        {
            return;
        }

        var samples = _samples.AsSpan(0, ScopeWindow * 2);
        Tap.CopyLatest(PlayheadPosition, samples);
        var trigger = FindTrigger(samples);
        var window = samples.Slice(trigger - ScopeWindow / 2, ScopeWindow);

        var columns = Math.Max(1, (int)Math.Ceiling(width));
        var columnWidth = width / columns;
        var scale = middle * _level;
        var trace = new StreamGeometry();
        var area = new StreamGeometry();
        using (var traceContext = trace.Open())
        using (var areaContext = area.Open())
        {
            areaContext.BeginFigure(new Point(0, middle), true, true);
            float previousMinimum = 0;
            float previousMaximum = 0;
            for (var column = 0; column < columns; column++)
            {
                var start = column * ScopeWindow / columns;
                var end = Math.Max(start + 1, (column + 1) * ScopeWindow / columns);
                var slice = window[start..end];
                float minimum = slice[0];
                float maximum = slice[0];
                double sum = 0;
                foreach (var sample in slice)
                {
                    minimum = Math.Min(minimum, sample);
                    maximum = Math.Max(maximum, sample);
                    sum += sample;
                }

                var x = column * columnWidth;
                var connectedMinimum = Math.Min(minimum, previousMaximum);
                var connectedMaximum = Math.Max(maximum, previousMinimum);
                previousMinimum = minimum;
                previousMaximum = maximum;
                minimum = column == 0 ? minimum : connectedMinimum;
                maximum = column == 0 ? maximum : connectedMaximum;
                var top = middle - maximum * scale;
                var bottom = Math.Max(middle - minimum * scale, top + 1.5);
                traceContext.BeginFigure(new Point(x, top), true, true);
                traceContext.LineTo(new Point(x + columnWidth, top), false, false);
                traceContext.LineTo(new Point(x + columnWidth, bottom), false, false);
                traceContext.LineTo(new Point(x, bottom), false, false);
                areaContext.LineTo(new Point(x + columnWidth / 2, middle - sum / slice.Length * scale), true, false);
            }
            areaContext.LineTo(new Point(width, middle), true, false);
        }
        trace.Freeze();
        area.Freeze();
        drawingContext.DrawGeometry(Brush("AccentStrongBrush"), null, area);
        drawingContext.DrawGeometry(Brush("AccentHoverBrush"), null, trace);
    }

    private static int FindTrigger(ReadOnlySpan<float> samples)
    {
        var latest = samples.Length - ScopeWindow / 2;
        for (var index = latest; index > ScopeWindow / 2; index--)
        {
            if (samples[index - 1] < 0 && samples[index] >= 0)
            {
                return index;
            }
        }

        return latest;
    }

    private void RenderSpectrum(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var labelHeight = LabelFontSize + 6;
        var height = Math.Max(0, ActualHeight - labelHeight);
        var gridPen = Pen("BorderSubtleBrush", 1);
        foreach (var frequency in _frequencyGrid)
        {
            var x = XForFrequency(frequency, width);
            drawingContext.DrawLine(gridPen, new Point(x, 0), new Point(x, height));
            var label = CreateLabel(frequency >= 1000 ? $"{frequency / 1000:0}k" : $"{frequency:0}");
            drawingContext.DrawText(label, new Point(Math.Min(x + 3, width - label.Width), height + 3));
        }

        if (_spectrum.Length > 0 && _level > 0)
        {
            var columnWidth = width / _spectrum.Length;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(0, height), true, true);
                for (var column = 0; column < _spectrum.Length; column++)
                {
                    context.LineTo(new Point(column * columnWidth, height - _spectrum[column] * _level * height), true, false);
                }
                context.LineTo(new Point(width, height), true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(Brush("AccentStrongBrush"), Pen("AccentHoverBrush", 1), geometry);
        }

        var response = new StreamGeometry();
        using (var context = response.Open())
        {
            var columns = Math.Max(1, (int)Math.Ceiling(width));
            for (var column = 0; column <= columns; column++)
            {
                var frequency = FrequencyAt(column, columns);
                var highPass = frequency / Math.Sqrt(frequency * frequency + _highPassCutoff * _highPassCutoff);
                var lowPass = 1 / Math.Sqrt(1 + Math.Pow(frequency / _lowPassCutoff, 2));
                var decibels = 20 * Math.Log10(Math.Max(highPass * lowPass, 1e-12));
                var y = height * 0.1 + Math.Clamp(decibels / ResponseFloor, 0, 1) * height * 0.9;
                var point = new Point(column * width / columns, y);
                if (column == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }
        response.Freeze();
        drawingContext.DrawGeometry(null, Pen("FocusBrush", 1.5), response);
    }

    private static double XForFrequency(double frequency, double width)
    {
        return Math.Log(frequency / MinimumFrequency) / Math.Log(MaximumFrequency / MinimumFrequency) * width;
    }

    private void RenderMeter(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var barHeight = Math.Min(24, ActualHeight / 3);
        var top = (ActualHeight - barHeight) / 2;
        var track = new Rect(0, top, width, barHeight);
        drawingContext.DrawRectangle(Brush("ControlAltBrush"), Pen("BorderSubtleBrush", 1), track);

        var zeroX = XForDecibels(0, width);
        var peakX = XForDecibels(DisplayedDecibels(_peakDb), width);
        var rmsX = XForDecibels(DisplayedDecibels(_rmsDb), width);
        drawingContext.DrawRectangle(Brush("AccentStrongBrush"), null, new Rect(0, top, peakX, barHeight));
        drawingContext.DrawRectangle(Brush("AccentBrush"), null, new Rect(0, top, rmsX, barHeight));
        if (peakX > zeroX)
        {
            drawingContext.DrawRectangle(Brush("ClipBrush"), null, new Rect(zeroX, top, peakX - zeroX, barHeight));
        }

        if (_level > 0)
        {
            var holdX = XForDecibels(DisplayedDecibels(_holdDb), width);
            var holdBrush = _holdDb > 0 ? Brush("ClipBrush") : Brush("FocusBrush");
            drawingContext.DrawRectangle(holdBrush, null, new Rect(Math.Max(0, holdX - 1), top, 2, barHeight));
        }

        var tickPen = Pen("BorderStrongBrush", 1);
        foreach (var tick in _meterTicks)
        {
            var x = XForDecibels(tick, width);
            drawingContext.DrawLine(tickPen, new Point(x, top + barHeight), new Point(x, top + barHeight + 4));
            var label = CreateLabel(tick > 0 ? $"+{tick:0}" : $"{tick:0}");
            drawingContext.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, width - label.Width), top + barHeight + 6));
        }

        var readout = _level > 0
            ? string.Create(CultureInfo.CurrentCulture, $"RMS {_rmsDb:0.0} dBFS    Peak {_holdDb:0.0} dBFS")
            : "RMS -    Peak -";
        var readoutText = CreateLabel(readout);
        drawingContext.DrawText(readoutText, new Point(width - readoutText.Width, top - readoutText.Height - 6));
    }

    private double DisplayedDecibels(double decibels)
    {
        return MeterFloor + (Math.Max(decibels, MeterFloor) - MeterFloor) * _level;
    }

    private static double XForDecibels(double decibels, double width)
    {
        return Math.Clamp((decibels - MeterFloor) / (MeterCeiling - MeterFloor), 0, 1) * width;
    }

    private FormattedText CreateLabel(string text)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            LabelFontSize,
            Brush("TertiaryTextBrush"),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private Brush Brush(string key)
    {
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = (Brush)FindResource(key);
            _brushes[key] = brush;
        }
        return brush;
    }

    private Pen Pen(string key, double thickness)
    {
        if (!_pens.TryGetValue((key, thickness), out var pen))
        {
            pen = new Pen(Brush(key), thickness);
            pen.Freeze();
            _pens[(key, thickness)] = pen;
        }
        return pen;
    }
}
