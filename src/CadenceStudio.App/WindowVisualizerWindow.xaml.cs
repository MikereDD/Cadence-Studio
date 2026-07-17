using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Core.Enums;

namespace CadenceStudio.App;

public partial class WindowVisualizerWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _controlsTimer;
    private bool _fullscreen;
    private WindowState _previousState = WindowState.Normal;
    private WindowStyle _previousStyle = WindowStyle.None;
    private Rect _previousBounds;
    private VisualizerPreset _lastAppliedPreset;
    private bool _animationsStarted;

    public WindowVisualizerWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        Width = Math.Clamp(viewModel.WindowVisualizerSavedWidth, MinWidth, SystemParameters.VirtualScreenWidth);
        Height = Math.Clamp(viewModel.WindowVisualizerSavedHeight, MinHeight, SystemParameters.VirtualScreenHeight);
        Topmost = viewModel.IsWindowVisualizerAlwaysOnTop || viewModel.WindowVisualizerWindowMode == VisualizerWindowMode.Floating;

        if (viewModel.ShouldRememberWindowVisualizerPlacement &&
            viewModel.WindowVisualizerSavedLeft is { } left &&
            viewModel.WindowVisualizerSavedTop is { } top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 120);
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(viewModel.WindowVisualizerControlsAutoHideSeconds, 0.5, 10.0)) };
        _controlsTimer.Tick += (_, _) => HideControls();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += (_, _) =>
        {
            if (viewModel.WindowVisualizerWindowMode == VisualizerWindowMode.Fullscreen)
                ToggleFullscreen();
            SelectPresetInCombo();
            ApplyPresetVisuals(false);
            StartAmbientAnimations();
            ApplyPlaybackPresentation();
            ShowControls();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_fullscreen)
            _viewModel.CaptureWindowVisualizerPlacement(Left, Top, ActualWidth, ActualHeight);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosing(e);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        _controlsTimer.Stop();
        var target = _viewModel.IsWindowPlaybackControlsEnabled ? 1.0 : 0.0;
        ControlsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        if (_viewModel.IsWindowPlaybackControlsEnabled)
            _controlsTimer.Start();
    }

    private void HideControls()
    {
        _controlsTimer.Stop();
        ControlsPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void StartAmbientAnimations()
    {
        if (_animationsStarted) return;
        _animationsStarted = true;
        if (_viewModel.ReduceMotion)
        {
            ApplyReducedMotionState();
            return;
        }
        AnimateRotation(OrbitOuter, 54, false);
        AnimateRotation(OrbitMiddle, 38, true);
        AnimateRotation(OrbitInner, 26, false);

        if (CoreShell.RenderTransform is TransformGroup group && group.Children.Count >= 2)
        {
            if (group.Children[0] is ScaleTransform scale)
            {
                var pulse = new DoubleAnimation(0.94, 1.08, TimeSpan.FromSeconds(2.4))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
            if (group.Children[1] is RotateTransform rotate)
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(22)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        AnimateOpacity(CoreGlow, 0.58, 1.0, 1.15);
        AnimateOpacity(AtmosphereHaloA, 0.10, 0.28, 5.5);
        AnimateOpacity(AtmosphereHaloB, 0.08, 0.24, 4.2);
        AnimateOpacity(ParticleA, 0.15, 0.95, 2.0);
        AnimateOpacity(ParticleB, 0.10, 0.82, 2.7);
        AnimateOpacity(ParticleC, 0.12, 0.88, 3.2);
        AnimateOpacity(ParticleD, 0.10, 0.72, 1.8);
        AnimateOpacity(ParticleE, 0.08, 0.68, 2.3);
        AnimateOpacity(ParticleF, 0.08, 0.74, 2.9);

        AnimateOpacity(AuroraCascadeLayer, 0.48, 0.92, 4.8);
        AnimateRotation(GlassHorizonRotation, 46, false);
        if (InfiniteWindowsLayer.RenderTransform is ScaleTransform infiniteScale)
        {
            var tunnel = new DoubleAnimation(0.94, 1.06, TimeSpan.FromSeconds(3.4))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            infiniteScale.BeginAnimation(ScaleTransform.ScaleXProperty, tunnel);
            infiniteScale.BeginAnimation(ScaleTransform.ScaleYProperty, tunnel);
        }
    }

    private static void AnimateRotation(RotateTransform rotate, double seconds, bool reverse)
    {
        var animation = new DoubleAnimation(reverse ? 360 : 0, reverse ? 0 : 360, TimeSpan.FromSeconds(seconds))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        rotate.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private static void AnimateRotation(FrameworkElement element, double seconds, bool reverse)
    {
        if (element.RenderTransform is not RotateTransform rotate) return;
        AnimateRotation(rotate, seconds, reverse);
    }

    private static void AnimateOpacity(UIElement element, double from, double to, double seconds)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.WindowVisualizerPreset) or nameof(MainWindowViewModel.WindowVisualizerPresetLabel))
        {
            SelectPresetInCombo();
            ApplyPresetVisuals();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsPlaybackPlaying))
        {
            ApplyPlaybackPresentation();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.ReduceMotion))
        {
            ApplyReducedMotionState();
        }
    }

    private void PresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || PresetSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string preset) return;
        _viewModel.SetWindowVisualizerPresetCommand.Execute(preset);
        ApplyPresetVisuals();
    }

    private void SelectPresetInCombo()
    {
        if (PresetSelector is null) return;
        var key = _viewModel.WindowVisualizerPreset.ToString();
        foreach (var item in PresetSelector.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), key, StringComparison.Ordinal))
            {
                PresetSelector.SelectedItem = item;
                break;
            }
        }
    }

    private void ApplyPresetVisuals(bool animate = true)
    {
        var previous = _lastAppliedPreset;
        var preset = _viewModel.WindowVisualizerPreset;
        var celestial = preset == VisualizerPreset.CelestialResonance;
        OrbitOuter.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        OrbitMiddle.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        OrbitInner.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        CelestialDottedRing.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        CoreShell.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        ReflectionPlatform.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        ReflectionInner.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;

        AuroraCascadeLayer.Visibility = preset == VisualizerPreset.AuroraCascade ? Visibility.Visible : Visibility.Collapsed;
        GlassHorizonLayer.Visibility = preset == VisualizerPreset.GlassHorizon ? Visibility.Visible : Visibility.Collapsed;
        InfiniteWindowsLayer.Visibility = preset == VisualizerPreset.InfiniteWindows ? Visibility.Visible : Visibility.Collapsed;

        PresetCreditText.Visibility = celestial ? Visibility.Visible : Visibility.Collapsed;
        ApplyPresetTuning(preset);
        _lastAppliedPreset = preset;

        if (animate && IsLoaded && !_viewModel.ReduceMotion && previous != preset)
        {
            VisualizerRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.42, 1.0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            SpectrumStage.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(520))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private void ApplyPresetTuning(VisualizerPreset preset)
    {
        switch (preset)
        {
            case VisualizerPreset.CelestialResonance:
                SpectrumStage.Opacity = 1.0;
                AtmosphereHaloA.Opacity = 0.22;
                AtmosphereHaloB.Opacity = 0.18;
                break;
            case VisualizerPreset.AuroraCascade:
                SpectrumStage.Opacity = 0.78;
                AtmosphereHaloA.Opacity = 0.34;
                AtmosphereHaloB.Opacity = 0.30;
                break;
            case VisualizerPreset.GlassHorizon:
                SpectrumStage.Opacity = 0.92;
                AtmosphereHaloA.Opacity = 0.15;
                AtmosphereHaloB.Opacity = 0.20;
                break;
            case VisualizerPreset.InfiniteWindows:
                SpectrumStage.Opacity = 0.70;
                AtmosphereHaloA.Opacity = 0.12;
                AtmosphereHaloB.Opacity = 0.24;
                break;
        }
    }

    private void ApplyPlaybackPresentation()
    {
        var playing = _viewModel.IsPlaybackPlaying;
        var targetOpacity = playing ? 1.0 : 0.48;
        var targetScale = playing ? 1.0 : 0.985;
        var duration = _viewModel.ReduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(360);
        SpectrumStage.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, duration));
        if (SpectrumStage.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration));
        }
    }

    private void ApplyReducedMotionState()
    {
        if (!_viewModel.ReduceMotion) return;
        foreach (var element in new FrameworkElement[] { OrbitOuter, OrbitMiddle, OrbitInner, GlassHorizonLayer, InfiniteWindowsLayer, CoreShell })
            element.BeginAnimation(OpacityProperty, null);
        OrbitOuter.Opacity = 0.62;
        OrbitMiddle.Opacity = 0.50;
        OrbitInner.Opacity = 0.58;
        AuroraCascadeLayer.Opacity = 0.62;
        GlassHorizonLayer.Opacity = 0.66;
        InfiniteWindowsLayer.Opacity = 0.74;
        CoreShell.Opacity = 0.88;
        ApplyPlaybackPresentation();
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => Topmost = !Topmost;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _fullscreen)
            ToggleFullscreen();
        else if (e.Key == Key.F11)
            ToggleFullscreen();
        else if (e.Key == Key.Space)
            _viewModel.TogglePlaybackCommand.Execute(null);
        else if (e.Key is Key.Left or Key.PageUp)
            CyclePreset(-1);
        else if (e.Key is Key.Right or Key.PageDown)
            CyclePreset(1);
    }

    private void CyclePreset(int direction)
    {
        var presets = new[]
        {
            VisualizerPreset.CelestialResonance,
            VisualizerPreset.AuroraCascade,
            VisualizerPreset.GlassHorizon,
            VisualizerPreset.InfiniteWindows
        };
        var index = Array.IndexOf(presets, _viewModel.WindowVisualizerPreset);
        if (index < 0) index = 0;
        index = (index + direction + presets.Length) % presets.Length;
        _viewModel.SetWindowVisualizerPresetCommand.Execute(presets[index].ToString());
        ShowControls();
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _previousBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            _fullscreen = true;
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = _previousStyle;
            WindowState = _previousState;
            Left = _previousBounds.Left;
            Top = _previousBounds.Top;
            Width = _previousBounds.Width;
            Height = _previousBounds.Height;
            _fullscreen = false;
        }
    }
}
