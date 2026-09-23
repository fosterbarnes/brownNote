using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using NAudio.Wave;

using brownNote.Audio;
using brownNote.Helpers;

namespace brownNote;

public enum FooterButtonScope
{
    TabSpecific,
    ProjectWide
}

public partial class MainWindow : System.Windows.Window
{
    private static readonly string _assetPath = Path.Combine(AppContext.BaseDirectory, ".noise", "brown.opus");
    private static readonly TimeSpan _tabTransitionDuration = TimeSpan.FromMilliseconds(180);

    private readonly OpusAssetLoader _assetLoader = new();
    private WasapiPlayer? _player;
    private LoopingSampleProvider? _provider;
    private OpusAsset? _asset;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        WindowLocationStore.Restore(this);
        SourceInitialized += (_, _) => WindowsTitleBarTheme.ApplyImmersiveDarkMode(this);
    }

    private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        UpdateTabForegrounds(false);
        UpdateTabRowLayout(false);
    }

    private void ModeTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.Source == ModeTabs)
        {
            UpdateTabForegrounds(true);
            UpdateTabRowLayout(true);
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

    private void PlayButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PlayButton.Tag is not FooterButtonScope.TabSpecific)
            return;

        switch (ModeTabs.SelectedIndex)
        {
            case 0:
                PlayOpus();
                break;
        }
    }

    private void PlayOpus()
    {
        if (!StopAudio())
        {
            return;
        }

        try
        {
            var asset = _assetLoader.Load(_assetPath);
            var provider = new LoopingSampleProvider(asset.Samples, asset.Channels);
            var player = new WasapiPlayerBuilder().Build();

            _asset = asset;
            _provider = provider;
            _player = player;
            player.PlaybackStopped += Player_PlaybackStopped;
            player.Init(provider);
            player.Play();
        }
        catch (Exception)
        {
            DisposeAudio();
        }
    }

    private void StopButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).Tag is not FooterButtonScope.ProjectWide)
            return;

        StopAudio();
    }

    private void Player_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        DisposeAudio();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        WindowLocationStore.Save(this);
        _isClosing = true;
        DisposeAudio();
    }

    private bool StopAudio()
    {
        var cleanupError = DisposeAudio();
        if (cleanupError is not null)
        {
            return false;
        }

        return true;
    }

    private Exception? DisposeAudio()
    {
        var player = _player;
        var provider = _provider;
        _player = null;
        _provider = null;
        _asset = null;

        Exception? cleanupError = null;
        if (player is not null)
        {
            player.PlaybackStopped -= Player_PlaybackStopped;
            try
            {
                player.Stop();
            }
            catch (Exception exception)
            {
                cleanupError = exception;
            }

            try
            {
                player.Dispose();
            }
            catch (Exception exception)
            {
                cleanupError ??= exception;
            }
        }

        if (provider is not null)
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception exception)
            {
                cleanupError ??= exception;
            }
        }

        return cleanupError;
    }

}
