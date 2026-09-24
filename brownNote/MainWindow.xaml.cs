   using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Media;
using System.Windows.Media.Animation;

using brownNote.Audio;
using brownNote.Controls;
using brownNote.Helpers;

namespace brownNote;

public partial class MainWindow : System.Windows.Window
{
    private static readonly TimeSpan _tabTransitionDuration = TimeSpan.FromMilliseconds(180);

    private readonly NoiseMachine _noiseMachine = new();
    private readonly string _aboutInstallPath = AppContext.BaseDirectory.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);
    private readonly string _aboutSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "brownNote");
    private readonly SoundEffects _soundEffects = new();
    private AppPreferences _preferences = AppPreferences.Defaults;
    private NoiseColor _activeNoiseColor = NoiseColor.Brown;
    private bool _transportSwitched;
    private bool _applyingSettings;
    private FrameworkElement? _activePage;
    private int _pageTransition;
    private readonly Dictionary<Slider, (DoubleAnimation Animation, double? Frequency)> _sliderAnimations = [];

    public MainWindow()
    {
        InitializeComponent();
        NoiseMachinePage.PreviewMouseDown += (_, _) => StopSliderAnimations();
        NoiseMachinePage.PreviewKeyDown += (_, _) => StopSliderAnimations();
        InitializeAboutPage();
        RestorePreferences();
        WindowLocationStore.Restore(this);
        SourceInitialized += (_, _) => WindowsTitleBarTheme.ApplyImmersiveDarkMode(this);
        _noiseMachine.FadedOut += () => Dispatcher.BeginInvoke(() =>
        {
            if (_noiseMachine.IsPlaying)
                return;

            StopAudio();
        });
    }

    private void InitializeAboutPage()
    {
        AboutTitleText.Text = $"brownNote ({GetPlatformLabel()})";
        AboutVersionText.Text = $"v{ReadVersion()}";
        AboutInstallPathText.Text = _aboutInstallPath;
        AboutSettingsPathText.Text = _aboutSettingsPath;
    }

    private static string ReadVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Version");
        return File.ReadLines(path).First(line => !string.IsNullOrWhiteSpace(line)).Trim().TrimStart('v', 'V');
    }

    private static string GetPlatformLabel() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "ARM64",
        var architecture => architecture.ToString()
    };

    private void RestorePreferences()
    {
        _preferences = AppPreferencesStore.Load();
        foreach (var color in Enum.GetValues<NoiseColor>())
        {
            ApplyChannelSettings(color, _preferences.GetNoiseSettings(color));
        }
        foreach (var key in ColorKeys)
        {
            var color = (NoiseColor)key.Tag;
            var selected = _preferences.SelectedColors?.Contains(color) == true;
            key.IsChecked = selected;
            _noiseMachine.SetSelected(color, selected);
        }
        ModeTabs.SelectedItem = ModeTabs.Items.OfType<TabItem>()
            .First(tab => PageName(tab) == _preferences.SelectedPage);
        ApplyNoiseSettings(SelectedNoiseColor ?? NoiseColor.Brown);
        RefreshBoundsEditors();
        VisualizerModeComboBox.SelectedIndex = _preferences.VisualizerMode;
    }

    private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Visualizer.Tap = _noiseMachine.Tap;
        UpdateTabForegrounds(false);
        UpdateTabRowLayout(false);
        ShowModePage(false);
    }

    private void GeneratedVolumeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsApplyingSettings(sender))
            return;

        _noiseMachine[_activeNoiseColor].OutputGain = (float)e.NewValue;
    }

    private void NoiseDensitySlider_ValueChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsApplyingSettings(sender))
            return;

        _noiseMachine[_activeNoiseColor].NoiseDensity = (int)e.NewValue;
    }

    private void LowPassCutoffSlider_FrequencyChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsApplyingSettings(sender))
            return;

        if (HighPassCutoffSlider is not null && e.NewValue <= HighPassCutoffSlider.Frequency)
        {
            LowPassCutoffSlider.Frequency = e.OldValue;
            return;
        }

        _noiseMachine[_activeNoiseColor].LowPassCutoff = (float)e.NewValue;
        if (Visualizer is not null)
        {
            Visualizer.LowPassCutoff = e.NewValue;
        }
    }

    private void HighPassCutoffSlider_FrequencyChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsApplyingSettings(sender))
            return;

        if (e.NewValue >= LowPassCutoffSlider.Frequency)
        {
            HighPassCutoffSlider.Frequency = e.OldValue;
            return;
        }

        _noiseMachine[_activeNoiseColor].HighPassCutoff = (float)e.NewValue;
        if (Visualizer is not null)
        {
            Visualizer.HighPassCutoff = e.NewValue;
        }
    }

    private void VisualizerModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Visualizer.Mode = (VisualizerMode)Math.Max(0, VisualizerModeComboBox.SelectedIndex);
    }

    private void ColornessSlider_ValueChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsApplyingSettings(sender))
            return;

        _noiseMachine[_activeNoiseColor].Colorness = (float)e.NewValue;
        if (Visualizer is not null)
        {
            Visualizer.Colorness = e.NewValue;
        }
    }

    private void AudioValueEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        ((TextBox)sender).SelectAll();
    }

    private void ValueEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
            return;

        var editor = (TextBox)sender;
        editor.Focus();
        editor.SelectAll();
        e.Handled = true;
    }

    private void AudioValueEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitAudioValue((TextBox)sender);
    }

    private void AudioValueEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var editor = (TextBox)sender;
        if (e.Key == Key.Enter)
        {
            CommitAudioValue(editor);
            editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RefreshAudioValue(editor);
            editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void CommitAudioValue(TextBox editor)
    {
        if (editor.Tag is not Slider slider)
        {
            return;
        }

        var metric = SliderMetric.GetMetric(slider);
        if (!TryParseAudioValue(editor.Text, metric, out var value))
        {
            RefreshAudioValue(editor);
            return;
        }

        if (metric == AudioMetric.Percentage)
        {
            value /= 100;
        }

        if (!double.IsFinite(value))
        {
            RefreshAudioValue(editor);
            return;
        }

        if (slider is LogSlider logSlider)
        {
            value = Math.Round(Math.Clamp(value, logSlider.FrequencyMinimum, logSlider.FrequencyMaximum));
        }
        else
        {
            value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }

        if ((metric == AudioMetric.Count && value != Math.Truncate(value)) ||
            (ReferenceEquals(slider, LowPassCutoffSlider) && value <= HighPassCutoffSlider.Frequency) ||
            (ReferenceEquals(slider, HighPassCutoffSlider) && value >= LowPassCutoffSlider.Frequency))
        {
            RefreshAudioValue(editor);
            return;
        }

        if (slider is LogSlider frequencySlider)
        {
            frequencySlider.Frequency = value;
        }
        else
        {
            slider.Value = value;
        }
        RefreshAudioValue(editor);
    }

    private static bool TryParseAudioValue(string text, AudioMetric metric, out double value)
    {
        var suffix = metric switch
        {
            AudioMetric.Percentage or AudioMetric.WholePercentage => CultureInfo.CurrentCulture.NumberFormat.PercentSymbol,
            AudioMetric.Hertz => "Hz",
            AudioMetric.Count => "voices",
            _ => string.Empty
        };
        var numericText = text.Trim();
        if (suffix.Length > 0 && numericText.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            numericText = numericText[..^suffix.Length].Trim();
        }

        return double.TryParse(
            numericText,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture,
            out value);
    }

    private static void RefreshAudioValue(TextBox editor)
    {
        editor.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void ModeTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source == ModeTabs)
        {
            UpdateTabForegrounds(true);
            UpdateTabRowLayout(true);
            if (IsLoaded)
            {
                SaveActiveNoiseSettings();
                if (SelectedNoiseColor is { } color)
                {
                    ApplyNoiseSettings(color, animate: ReferenceEquals(_activePage, NoiseMachinePage));
                }
                RefreshBoundsEditors();
                ShowModePage(true);
            }
        }
    }

    private void ModeTabs_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        UpdateTabRowLayout(false);
    }

    private void UpdateTabRowLayout(bool animate)
    {
        if (ModeTabs.SelectedIndex < 0 || ModeTabs.Items.Count == 0)
            return;

        var headerHost = ModeTabs.Template.FindName("HeaderHost", ModeTabs) as FrameworkElement;
        var dividerLayer = ModeTabs.Template.FindName("TabDividers", ModeTabs) as Panel;
        var indicator = ModeTabs.Template.FindName("SelectedTabIndicator", ModeTabs) as Border;
        var transform = ModeTabs.Template.FindName("SelectedTabIndicatorTransform", ModeTabs) as TranslateTransform;
        if (headerHost is null || dividerLayer is null || indicator is null || transform is null || headerHost.ActualWidth <= 0)
            return;

        var tabCount = ModeTabs.Items.Count;
        var tabWidth = headerHost.ActualWidth / tabCount;

        indicator.Width = tabWidth;
        UpdateTabDividers(dividerLayer, tabWidth, tabCount);
        var (fillColor, borderColor) = TabIndicatorColors();
        indicator.Background = Unfrozen(indicator.Background);
        indicator.BorderBrush = Unfrozen(indicator.BorderBrush);
        AnimateColor((SolidColorBrush)indicator.Background, fillColor, animate);
        AnimateColor((SolidColorBrush)indicator.BorderBrush, borderColor, animate);
        if (animate)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(ModeTabs.SelectedIndex * tabWidth, _tabTransitionDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                },
                HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = ModeTabs.SelectedIndex * tabWidth;
        }
    }

    private static void UpdateTabDividers(Panel dividerLayer, double tabWidth, int tabCount)
    {
        dividerLayer.Children.Clear();
        var inset = (Thickness)dividerLayer.FindResource("TabDividerInset");
        var brush = (Brush)dividerLayer.FindResource("BorderSubtleBrush");
        for (var index = 1; index < tabCount; index++)
        {
            dividerLayer.Children.Add(new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(tabWidth * index, inset.Top, inset.Right, inset.Bottom),
                Background = brush,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            });
        }
    }

    private void ShowModePage(bool animate)
    {
        FrameworkElement? nextPage = (ModeTabs.SelectedItem as TabItem)?.Tag switch
        {
            NoiseColor => NoiseMachinePage,
            AppPreferences.SettingsPage => SettingsPage,
            AppPreferences.AboutPage => AboutPage,
            _ => null
        };
        if (nextPage is null || ReferenceEquals(nextPage, _activePage))
        {
            return;
        }

        var previousPage = _activePage;
        _activePage = nextPage;
        var transition = ++_pageTransition;

        nextPage.BeginAnimation(UIElement.OpacityProperty, null);
        nextPage.Visibility = Visibility.Visible;
        if (!animate || previousPage is null)
        {
            nextPage.Opacity = 1;
            HideInactivePage(AboutPage, nextPage);
            HideInactivePage(SettingsPage, nextPage);
            HideInactivePage(NoiseMachinePage, nextPage);
            return;
        }

        previousPage.BeginAnimation(UIElement.OpacityProperty, null);
        previousPage.Visibility = Visibility.Visible;
        previousPage.Opacity = 1;
        nextPage.Opacity = 0;

        var fadeOut = new DoubleAnimation(1, 0, _tabTransitionDuration);
        var fadeIn = new DoubleAnimation(0, 1, _tabTransitionDuration);
        fadeIn.Completed += (_, _) =>
        {
            if (transition != _pageTransition)
            {
                return;
            }

            previousPage.Visibility = Visibility.Collapsed;
            previousPage.Opacity = 0;
        };
        previousPage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        nextPage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private static void HideInactivePage(FrameworkElement page, FrameworkElement activePage)
    {
        if (ReferenceEquals(page, activePage))
        {
            return;
        }

        page.BeginAnimation(UIElement.OpacityProperty, null);
        page.Opacity = 0;
        page.Visibility = Visibility.Collapsed;
    }

    private static string PageName(TabItem tab) => tab.Tag.ToString()!;

    private NoiseColor? SelectedNoiseColor => (ModeTabs.SelectedItem as TabItem)?.Tag as NoiseColor?;

    private void ApplyNoiseSettings(NoiseColor color, bool animate = false)
    {
        var colorChanged = color != _activeNoiseColor;
        _activeNoiseColor = color;
        var settings = _preferences.GetNoiseSettings(color);
        var bounds = settings.Bounds;
        animate &= SystemParameters.ClientAreaAnimation;
        var sliders = NoiseSliders;
        var startPositions = sliders.Select(slider => slider.Value).ToArray();
        StopSliderAnimations();

        ColornessLabel.Text = $"{color}ness";
        ColornessControl.IsEnabled = color != NoiseColor.White;
        ColornessSlider.IsEnabled = ColornessControl.IsEnabled;
        if (animate && colorChanged)
        {
            ColornessLabel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, _tabTransitionDuration));
        }

        _applyingSettings = true;
        try
        {
            HighPassCutoffSlider.FrequencyMinimum = bounds.HighPassMin;
            HighPassCutoffSlider.FrequencyMaximum = bounds.HighPassMax;
            LowPassCutoffSlider.FrequencyMinimum = bounds.LowPassMin;
            LowPassCutoffSlider.FrequencyMaximum = bounds.LowPassMax;
            GeneratedVolumeSlider.Minimum = bounds.GainMin;
            GeneratedVolumeSlider.Maximum = bounds.GainMax;
            ColornessSlider.Minimum = color == NoiseColor.White ? 0 : bounds.ColornessMin;
            ColornessSlider.Maximum = color == NoiseColor.White ? 100 : bounds.ColornessMax;
            GeneratedVolumeSlider.Value = settings.Volume;
            NoiseDensitySlider.Value = settings.NoiseDensity;
            LowPassCutoffSlider.Frequency = settings.LowPassCutoff;
            HighPassCutoffSlider.Frequency = settings.HighPassCutoff;
            ColornessSlider.Value = settings.Colorness;
        }
        finally
        {
            _applyingSettings = false;
        }

        if (animate)
        {
            for (var index = 0; index < sliders.Length; index++)
            {
                AnimateSliderFrom(sliders[index], startPositions[index]);
            }
        }

        ApplyChannelSettings(color, settings);
        Visualizer.Colorness = settings.Colorness;
        Visualizer.LowPassCutoff = settings.LowPassCutoff;
        Visualizer.HighPassCutoff = settings.HighPassCutoff;
        Visualizer.Color = color;
    }

    private void ApplyChannelSettings(NoiseColor color, NoiseSettings settings)
    {
        var channel = _noiseMachine[color];
        channel.OutputGain = (float)settings.Volume;
        channel.NoiseDensity = settings.NoiseDensity;
        channel.UpdateCutoffs(
            (float)settings.HighPassCutoff,
            (float)settings.LowPassCutoff,
            (float)settings.Colorness);
    }

    private void SaveActiveNoiseSettings()
    {
        // Sliders show in-between values while a tab animation is running. The channel already has the target.
        var channel = _noiseMachine[_activeNoiseColor];
        var settings = new NoiseSettings(
            channel.OutputGain,
            channel.NoiseDensity,
            channel.LowPassCutoff,
            channel.HighPassCutoff,
            channel.Colorness,
            _preferences.GetNoiseSettings(_activeNoiseColor).Bounds);
        _preferences = _preferences.WithNoiseSettings(_activeNoiseColor, settings.Normalize());
    }

    private Slider[] NoiseSliders =>
        [GeneratedVolumeSlider, LowPassCutoffSlider, HighPassCutoffSlider, ColornessSlider, NoiseDensitySlider];

    private bool IsApplyingSettings(object sender) =>
        _applyingSettings || (sender is Slider slider && _sliderAnimations.ContainsKey(slider));

    // Animates only the thumb and its value text; the audio already has the target settings.
    private void AnimateSliderFrom(Slider slider, double from)
    {
        var to = slider.Value;
        if (Math.Abs(to - from) < 1e-9)
            return;

        var animation = new DoubleAnimation(from, to, _tabTransitionDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        var frequency = (slider as LogSlider)?.Frequency;
        animation.Completed += (_, _) =>
        {
            if (_sliderAnimations.TryGetValue(slider, out var active) && ReferenceEquals(active.Animation, animation))
            {
                FinishSliderAnimation(slider);
            }
        };
        _sliderAnimations[slider] = (animation, frequency);
        slider.BeginAnimation(RangeBase.ValueProperty, animation);
    }

    private void FinishSliderAnimation(Slider slider)
    {
        if (!_sliderAnimations.TryGetValue(slider, out var active))
            return;

        slider.BeginAnimation(RangeBase.ValueProperty, null);
        _sliderAnimations.Remove(slider);
        if (slider is LogSlider logSlider && active.Frequency is { } frequency)
        {
            _applyingSettings = true;
            try
            {
                logSlider.Frequency = frequency;
            }
            finally
            {
                _applyingSettings = false;
            }
        }
    }

    private void StopSliderAnimations()
    {
        foreach (var slider in _sliderAnimations.Keys.ToArray())
        {
            FinishSliderAnimation(slider);
        }
    }

    private IEnumerable<TextBox> BoundsEditors => BoundsEditorsPanel.Children
        .OfType<Grid>()
        .SelectMany(section => section.Children.OfType<TextBox>());

    private void RefreshBoundsEditors()
    {
        foreach (var editor in BoundsEditors)
        {
            var (color, field) = ParseBoundsTag(editor);
            var bounds = _preferences.GetNoiseSettings(color).Bounds;
            var value = field switch
            {
                nameof(NoiseBounds.HighPassMin) => bounds.HighPassMin,
                nameof(NoiseBounds.HighPassMax) => bounds.HighPassMax,
                nameof(NoiseBounds.LowPassMin) => bounds.LowPassMin,
                nameof(NoiseBounds.LowPassMax) => bounds.LowPassMax,
                nameof(NoiseBounds.GainMin) => bounds.GainMin,
                nameof(NoiseBounds.GainMax) => bounds.GainMax,
                nameof(NoiseBounds.ColornessMin) => bounds.ColornessMin,
                _ => bounds.ColornessMax
            };
            editor.Text = BoundsMetric(field) switch
            {
                AudioMetric.Percentage => value.ToString("P0", CultureInfo.CurrentCulture),
                AudioMetric.WholePercentage => $"{value.ToString("N0", CultureInfo.CurrentCulture)}%",
                _ => $"{value.ToString("N0", CultureInfo.CurrentCulture)} Hz"
            };
        }
    }

    private static (NoiseColor Color, string Field) ParseBoundsTag(TextBox editor)
    {
        var parts = ((string)editor.Tag).Split('/');
        return (Enum.Parse<NoiseColor>(parts[0]), parts[1]);
    }

    private void BoundsEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitBoundsValue((TextBox)sender);
    }

    private void BoundsEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var editor = (TextBox)sender;
        if (e.Key == Key.Enter)
        {
            CommitBoundsValue(editor);
            editor.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RefreshBoundsEditors();
            e.Handled = true;
        }
    }

    private void CommitBoundsValue(TextBox editor)
    {
        var (color, field) = ParseBoundsTag(editor);
        var metric = BoundsMetric(field);
        if (!TryParseAudioValue(editor.Text, metric, out var value) || !double.IsFinite(value))
        {
            RefreshBoundsEditors();
            return;
        }

        if (metric == AudioMetric.Percentage)
        {
            value /= 100;
        }

        var bounds = _preferences.GetNoiseSettings(color).Bounds;
        SetBounds(color, field switch
        {
            nameof(NoiseBounds.HighPassMin) => bounds with { HighPassMin = value },
            nameof(NoiseBounds.HighPassMax) => bounds with { HighPassMax = value },
            nameof(NoiseBounds.LowPassMin) => bounds with { LowPassMin = value },
            nameof(NoiseBounds.LowPassMax) => bounds with { LowPassMax = value },
            nameof(NoiseBounds.GainMin) => bounds with { GainMin = value },
            nameof(NoiseBounds.GainMax) => bounds with { GainMax = value },
            nameof(NoiseBounds.ColornessMin) => bounds with { ColornessMin = value },
            _ => bounds with { ColornessMax = value }
        });
    }

    private static AudioMetric BoundsMetric(string field) => field switch
    {
        nameof(NoiseBounds.GainMin) or nameof(NoiseBounds.GainMax) => AudioMetric.Percentage,
        nameof(NoiseBounds.ColornessMin) or nameof(NoiseBounds.ColornessMax) => AudioMetric.WholePercentage,
        _ => AudioMetric.Hertz
    };

    private void ResetBounds_Click(object sender, RoutedEventArgs e)
    {
        SetBounds((NoiseColor)((Button)sender).Tag, NoiseBounds.Defaults);
    }

    private void ResetAllBounds_Click(object sender, RoutedEventArgs e)
    {
        foreach (var color in Enum.GetValues<NoiseColor>())
        {
            SetBounds(color, NoiseBounds.Defaults);
        }
    }

    private void SetBounds(NoiseColor color, NoiseBounds bounds)
    {
        SaveActiveNoiseSettings();
        var settings = (_preferences.GetNoiseSettings(color) with { Bounds = bounds }).Normalize();
        _preferences = _preferences.WithNoiseSettings(color, settings);
        ApplyChannelSettings(color, settings);
        if (color == _activeNoiseColor)
        {
            ApplyNoiseSettings(color);
        }
        RefreshBoundsEditors();
    }

    private (Color Fill, Color Border) TabIndicatorColors()
    {
        return (ModeTabs.SelectedItem as TabItem)?.Tag switch
        {
            NoiseColor.Brown => (ResourceColor("BrownKeyBrush"), ResourceColor("BrownKeyAccentBrush")),
            NoiseColor.Green => (ResourceColor("GreenKeyBrush"), ResourceColor("GreenKeyAccentBrush")),
            NoiseColor.White => (ResourceColor("WhiteKeyBrush"), ResourceColor("WhiteKeyAccentBrush")),
            _ => (ResourceColor("AccentStrongBrush"), ResourceColor("AccentBorderBrush"))
        };
    }

    private Color ResourceColor(string key) => ((SolidColorBrush)FindResource(key)).Color;

    private void UpdateTabForegrounds(bool animate)
    {
        var selectedColor = ResourceColor("PrimaryTextBrush");
        var unselectedColor = ResourceColor("TertiaryTextBrush");
        var whiteSelectedColor = ResourceColor("AppBackgroundBrush");
        foreach (var item in ModeTabs.Items)
        {
            if (item is not TabItem tab)
                continue;

            var color = !tab.IsSelected
                ? unselectedColor
                : tab.Tag is NoiseColor.White ? whiteSelectedColor : selectedColor;
            SetTabForeground(tab, color, animate);
        }
    }

    private static void SetTabForeground(TabItem tab, Color color, bool animate)
    {
        if (tab.Foreground is not SolidColorBrush)
            return;

        var brush = Unfrozen(tab.Foreground);
        tab.Foreground = brush;
        AnimateColor(brush, color, animate);
    }

    private static SolidColorBrush Unfrozen(Brush source) =>
        source is SolidColorBrush brush && !brush.IsFrozen ? brush : ((SolidColorBrush)source).Clone();

    private static void AnimateColor(SolidColorBrush brush, Color color, bool animate)
    {
        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            animate
                ? new ColorAnimation(color, _tabTransitionDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                }
                : null);
        if (!animate)
            brush.Color = color;
    }

    private void TransportKey_Click(object sender, RoutedEventArgs e)
    {
        if (_transportSwitched)
        {
            _transportSwitched = false;
            return;
        }

        _soundEffects.ButtonMechanism();
    }

    private void OnTransportSwitched()
    {
        if (!IsLoaded)
            return;

        _soundEffects.ButtonDown();
        _soundEffects.ButtonUp();

        // Checked fires before Click during a click; keyboard switches have no Click to clear this.
        _transportSwitched = true;
        Dispatcher.BeginInvoke(() => _transportSwitched = false);
    }

    private void PlayKey_Checked(object sender, RoutedEventArgs e)
    {
        if (!_noiseMachine.HasSelection)
        {
            StopKey.IsChecked = true;
            return;
        }

        OnTransportSwitched();
        try
        {
            _noiseMachine.Play();
            Visualizer.IsActive = true;
        }
        catch (Exception)
        {
            _noiseMachine.Stop();
            StopKey.IsChecked = true;
        }
    }

    private void StopKey_Checked(object sender, RoutedEventArgs e)
    {
        OnTransportSwitched();
        _noiseMachine.Pause();
    }

    private ToggleButton[] ColorKeys => [BrownKey, GreenKey, WhiteKey];

    private void ColorKey_Click(object sender, RoutedEventArgs e)
    {
        var key = (ToggleButton)sender;
        _soundEffects.ButtonDown();
        _soundEffects.ButtonUp();
        _noiseMachine.SetSelected((NoiseColor)key.Tag, key.IsChecked == true);
        if (!_noiseMachine.HasSelection && PlayKey.IsChecked == true)
        {
            StopKey.IsChecked = true;
        }
    }

    private void AboutOpenInstallLocation_Click(object sender, RoutedEventArgs e)
    {
        OpenAboutFolder(_aboutInstallPath);
    }

    private void AboutOpenSettingsLocation_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_aboutSettingsPath);
        OpenAboutFolder(_aboutSettingsPath);
    }

    private static void OpenAboutFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void AboutHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveActiveNoiseSettings();
        AppPreferencesStore.Save(_preferences with
        {
            SelectedPage = PageName((TabItem)ModeTabs.SelectedItem),
            VisualizerMode = VisualizerModeComboBox.SelectedIndex,
            SelectedColors = [.. _noiseMachine.SelectedColors]
        });
        WindowLocationStore.Save(this);
        StopAudio();
    }

    private void StopAudio()
    {
        _noiseMachine.Stop();
        Visualizer.IsActive = false;
    }

}
