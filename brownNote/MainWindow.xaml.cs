using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
    private const double _MINIMUM_INTEGRATOR_CUTOFF = 10;
    private const double _MAXIMUM_INTEGRATOR_CUTOFF = 500;

    private readonly GeneratedAudioPlayer _generatedAudioPlayer = new();
    private readonly string _aboutInstallPath = AppContext.BaseDirectory.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar);
    private readonly string _aboutSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "brownNote");
    private readonly SoundEffects _soundEffects = new();
    private bool _transportSwitched;
    private FrameworkElement? _activePage;
    private int _pageTransition;

    public MainWindow()
    {
        InitializeComponent();
        InitializeAboutPage();
        RestorePreferences();
        WindowLocationStore.Restore(this);
        SourceInitialized += (_, _) => WindowsTitleBarTheme.ApplyImmersiveDarkMode(this);
        _generatedAudioPlayer.FadedOut += () => Dispatcher.BeginInvoke(() =>
        {
            if (_generatedAudioPlayer.IsPlaying)
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
        var preferences = AppPreferencesStore.Load();
        ModeTabs.SelectedIndex = preferences.SelectedTab;
        GeneratedVolumeSlider.Value = preferences.GeneratedVolume;
        NoiseDensitySlider.Value = preferences.NoiseDensity;
        LowPassCutoffSlider.Value = preferences.LowPassCutoff;
        HighPassCutoffSlider.Value = preferences.HighPassCutoff;
        BrownnessSlider.Value = preferences.Brownness;
        VisualizerModeComboBox.SelectedIndex = preferences.VisualizerMode;
    }

    private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _generatedAudioPlayer.Volume = (float)GeneratedVolumeSlider.Value;
        _generatedAudioPlayer.NoiseDensity = (int)NoiseDensitySlider.Value;
        _generatedAudioPlayer.LowPassCutoff = (float)LowPassCutoffSlider.Value;
        _generatedAudioPlayer.HighPassCutoff = (float)HighPassCutoffSlider.Value;
        _generatedAudioPlayer.IntegratorCutoff = IntegratorCutoffFromBrownness(BrownnessSlider.Value);
        Visualizer.Tap = _generatedAudioPlayer.Tap;
        Visualizer.LowPassCutoff = LowPassCutoffSlider.Value;
        Visualizer.HighPassCutoff = HighPassCutoffSlider.Value;
        UpdateTabForegrounds(false);
        UpdateTabRowLayout(false);
        ShowModePage(false);
    }

    private void GeneratedVolumeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        _generatedAudioPlayer.Volume = (float)e.NewValue;
    }

    private void NoiseDensitySlider_ValueChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        _generatedAudioPlayer.NoiseDensity = (int)e.NewValue;
    }

    private void LowPassCutoffSlider_ValueChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (HighPassCutoffSlider is not null && e.NewValue <= HighPassCutoffSlider.Value)
        {
            LowPassCutoffSlider.Value = e.OldValue;
            return;
        }

        _generatedAudioPlayer.LowPassCutoff = (float)e.NewValue;
        if (Visualizer is not null)
        {
            Visualizer.LowPassCutoff = e.NewValue;
        }
    }

    private void HighPassCutoffSlider_ValueChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (e.NewValue >= LowPassCutoffSlider.Value)
        {
            HighPassCutoffSlider.Value = e.OldValue;
            return;
        }

        _generatedAudioPlayer.HighPassCutoff = (float)e.NewValue;
        if (Visualizer is not null)
        {
            Visualizer.HighPassCutoff = e.NewValue;
        }
    }

    private void VisualizerModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Visualizer.Mode = (VisualizerMode)Math.Max(0, VisualizerModeComboBox.SelectedIndex);
    }

    private void BrownnessSlider_ValueChanged(
        object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        _generatedAudioPlayer.IntegratorCutoff = IntegratorCutoffFromBrownness(e.NewValue);
    }

    private void AudioValueEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        ((TextBox)sender).SelectAll();
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

        if (!double.IsFinite(value) || value < slider.Minimum || value > slider.Maximum ||
            (metric == AudioMetric.Count && value != Math.Truncate(value)) ||
            (ReferenceEquals(slider, LowPassCutoffSlider) && value <= HighPassCutoffSlider.Value) ||
            (ReferenceEquals(slider, HighPassCutoffSlider) && value >= LowPassCutoffSlider.Value))
        {
            RefreshAudioValue(editor);
            return;
        }

        slider.Value = value;
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
        FrameworkElement? nextPage = ModeTabs.SelectedIndex switch
        {
            0 => GeneratedAudioPage,
            1 => AboutPage,
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
            HideInactivePage(GeneratedAudioPage, nextPage);
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

    private void UpdateTabForegrounds(bool animate)
    {
        var selectedColor = ((SolidColorBrush)FindResource("PrimaryTextBrush")).Color;
        var unselectedColor = ((SolidColorBrush)FindResource("TertiaryTextBrush")).Color;
        foreach (var item in ModeTabs.Items)
        {
            if (item is TabItem tab)
            {
                SetTabForeground(tab, tab.IsSelected ? selectedColor : unselectedColor, animate);
            }
        }
    }

    private static void SetTabForeground(TabItem tab, Color color, bool animate)
    {
        if (tab.Foreground is not SolidColorBrush brush)
        {
            return;
        }
        if (brush.IsFrozen)
        {
            brush = brush.Clone();
            tab.Foreground = brush;
        }

        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            animate
                ? new ColorAnimation(color, _tabTransitionDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                }
                : null);
        if (!animate)
        {
            brush.Color = color;
        }
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
        OnTransportSwitched();
        try
        {
            _generatedAudioPlayer.Play();
            Visualizer.IsActive = true;
        }
        catch (Exception)
        {
            _generatedAudioPlayer.Stop();
            StopKey.IsChecked = true;
        }
    }

    private void StopKey_Checked(object sender, RoutedEventArgs e)
    {
        OnTransportSwitched();
        _generatedAudioPlayer.Pause();
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
        AppPreferencesStore.Save(new AppPreferences(
            ModeTabs.SelectedIndex,
            GeneratedVolumeSlider.Value,
            (int)NoiseDensitySlider.Value,
            LowPassCutoffSlider.Value,
            HighPassCutoffSlider.Value,
            BrownnessSlider.Value,
            VisualizerModeComboBox.SelectedIndex));
        WindowLocationStore.Save(this);
        StopAudio();
    }

    private bool StopAudio()
    {
        var stopped = _generatedAudioPlayer.Stop() is null;
        Visualizer.IsActive = false;
        return stopped;
    }

    private static float IntegratorCutoffFromBrownness(double brownness)
    {
        var normalized = Math.Clamp(brownness, 0, 100) / 100;
        var cutoff = _MAXIMUM_INTEGRATOR_CUTOFF * Math.Pow(
            _MINIMUM_INTEGRATOR_CUTOFF / _MAXIMUM_INTEGRATOR_CUTOFF,
            normalized);
        return (float)cutoff;
    }

}
