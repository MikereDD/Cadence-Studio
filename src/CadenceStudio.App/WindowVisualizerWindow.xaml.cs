using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shell;
using FormsScreen = System.Windows.Forms.Screen;
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
    private ResizeMode _previousResizeMode = ResizeMode.CanResize;
    private bool _previousTopmost;
    private double _previousCaptionHeight = 58;
    private string? _fullscreenDisplayName;
    private HwndSource? _hwndSource;
    private bool _fullscreenRefreshQueued;
    private readonly bool _isDisplayMirror;
    private readonly WindowVisualizerWindow? _displayMaster;
    private readonly string? _forcedDisplayName;
    private readonly List<WindowVisualizerWindow> _displayMirrors = new();
    private bool _reconcilingDisplayMirrors;

    private const int WmDpiChanged = 0x02E0;
    private const int WmDisplayChange = 0x007E;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    public WindowVisualizerWindow(MainWindowViewModel viewModel,
        bool isDisplayMirror = false, string? forcedDisplayName = null,
        WindowVisualizerWindow? displayMaster = null)
    {
        _isDisplayMirror = isDisplayMirror;
        _forcedDisplayName = forcedDisplayName;
        _displayMaster = displayMaster;
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
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;

        Loaded += (_, _) =>
        {
            if (_isDisplayMirror)
            {
                VisualizerTopBar.Visibility = Visibility.Collapsed;
                ControlsPanel.Visibility = Visibility.Collapsed;
            }
            if (_isDisplayMirror || viewModel.WindowVisualizerWindowMode == VisualizerWindowMode.Fullscreen)
                ToggleFullscreen();
            SelectPresetInCombo();
            ApplyPresetVisuals(false);
            StartAmbientAnimations();
            ApplyPlaybackPresentation();
            UpdateDockLayout();
            ShowControls();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WindowMessageHook);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isDisplayMirror)
            CloseDisplayMirrors();
        if (!_isDisplayMirror && !_fullscreen)
            _viewModel.CaptureWindowVisualizerPlacement(Left, Top, ActualWidth, ActualHeight);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _hwndSource?.RemoveHook(WindowMessageHook);
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosing(e);
    }

    private void VisualizerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDockLayout();
        UpdateVisualizerSceneLayout();
    }

    // In fullscreen the 1180x620 scene grows to the monitor's aspect ratio.
    // Uniform scaling then fills 16:9, 16:10 and ultrawide displays without
    // clipping the center or stretching circular geometry into ellipses.
    // The chrome and transport dock stay outside the scaled scene.
    private void UpdateVisualizerSceneLayout()
    {
        if (VisualizerRoot is null || VisualizerSceneViewbox is null) return;
        if (!_fullscreen)
        {
            VisualizerRoot.Width = 1180;
            VisualizerRoot.Height = 620;
            VisualizerSceneViewbox.Stretch = Stretch.UniformToFill;
            return;
        }

        var viewportWidth = Math.Max(1.0, ActualWidth);
        var viewportHeight = Math.Max(1.0, ActualHeight);
        var aspect = viewportWidth / viewportHeight;
        VisualizerRoot.Width = Math.Max(1180.0, 620.0 * aspect);
        VisualizerRoot.Height = Math.Max(620.0, 1180.0 / aspect);
        VisualizerSceneViewbox.Stretch = Stretch.Uniform;
    }

    // At compact widths, remove the readout's entire column so the song title
    // gets all available room. Wide windows restore a balanced three-column dock.
    private void UpdateDockLayout()
    {
        if (DockStatusColumn is null || DockTechnicalStatus is null) return;
        var isWide = ActualWidth >= 980;
        DockStatusColumn.Width = isWide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        DockTechnicalStatus.Visibility = isWide ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e) => ShowControls();

    private void ShowControls()
    {
        if (_isDisplayMirror) return;
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

    private void DisplayScope_Click(object sender, RoutedEventArgs e)
    {
        if (_isDisplayMirror || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button };
        var scope = _viewModel.WindowFullscreenDisplayScope;
        var current = new MenuItem
        {
            Header = "Current display (default)",
            IsCheckable = true,
            IsChecked = scope == "Current"
        };
        current.Click += (_, _) => SelectFullscreenDisplays("Current", null);
        menu.Items.Add(current);

        var all = new MenuItem
        {
            Header = "All displays (independent visualizers)",
            IsCheckable = true,
            IsChecked = scope == "All"
        };
        all.Click += (_, _) => SelectFullscreenDisplays("All", null);
        menu.Items.Add(all);
        menu.Items.Add(new Separator());

        var targets = new MenuItem { Header = "Selected display" };
        foreach (var screen in FormsScreen.AllScreens
                     .OrderBy(item => item.Bounds.Left).ThenBy(item => item.Bounds.Top))
        {
            var name = screen.DeviceName;
            var label = name.Replace(@"\\.\", "", StringComparison.OrdinalIgnoreCase);
            var targetItem = new MenuItem
            {
                Header = $"{label} — {screen.Bounds.Width} × {screen.Bounds.Height}" +
                         (screen.Primary ? " (Primary)" : string.Empty),
                IsCheckable = true,
                IsChecked = scope == "Selected" &&
                    string.Equals(name, _viewModel.WindowFullscreenTargetDisplay,
                                  StringComparison.OrdinalIgnoreCase)
            };
            targetItem.Click += (_, _) => SelectFullscreenDisplays("Selected", name);
            targets.Items.Add(targetItem);
        }
        targets.IsEnabled = targets.Items.Count > 0;
        menu.Items.Add(targets);
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void SelectFullscreenDisplays(string scope, string? deviceName)
    {
        if (_isDisplayMirror) return;
        _viewModel.ConfigureWindowFullscreenDisplayScope(scope, deviceName);
        if (!_fullscreen) return;

        if (scope == "Selected" && !string.IsNullOrWhiteSpace(deviceName))
            _fullscreenDisplayName = deviceName;
        ApplyFullscreenBounds();
        ReconcileDisplayMirrors();
        QueueFullscreenBoundsRefresh();
        ShowControls();
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => Topmost = !Topmost;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_isDisplayMirror)
        {
            // Escape/F11 on ANY monitor restores the controlling visualizer window.
            if (e.Key == Key.Escape || e.Key == Key.F11)
            {
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(new Action(() => _displayMaster?.ExitFullscreenFromMirror()),
                    DispatcherPriority.Input);
            }
            return;
        }
        // The monitor cycle shortcut is distinct from Left/Right preset cycling.
        if (_fullscreen && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
            e.Key is Key.Left or Key.Right)
        {
            CycleFullscreenMonitor(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _fullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
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

    // Fullscreen is one monitor, never SystemParameters.VirtualScreen (which
    // combines every display, including negative coordinates).
    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var target = FormsScreen.FromHandle(handle);
            var configuredDevice = _isDisplayMirror ? _forcedDisplayName :
                _viewModel.WindowFullscreenDisplayScope == "Selected"
                    ? _viewModel.WindowFullscreenTargetDisplay : null;
            _fullscreenDisplayName = configuredDevice ?? target.DeviceName;
            _previousBounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousTopmost = Topmost;

            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome is not null)
            {
                _previousCaptionHeight = chrome.CaptionHeight;
                chrome.CaptionHeight = 0;
            }

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            _fullscreen = true;
            ApplyFullscreenBounds();
            QueueFullscreenBoundsRefresh();
            if (!_isDisplayMirror) ReconcileDisplayMirrors();
        }
        else
        {
            if (!_isDisplayMirror) CloseDisplayMirrors();
            _fullscreen = false;
            _fullscreenDisplayName = null;
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome is not null)
                chrome.CaptionHeight = _previousCaptionHeight;

            WindowState = WindowState.Normal;
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            Left = _previousBounds.Left;
            Top = _previousBounds.Top;
            Width = _previousBounds.Width;
            Height = _previousBounds.Height;
            WindowState = _previousState;
            Topmost = _previousTopmost;
            UpdateVisualizerSceneLayout();
        }
    }

    // DeviceName remains stable across DPI or layout changes while present.
    // AllScreens supports one, two, three or more monitors without any hardcoded
    // monitor numbering, origin or common resolution assumption.
    private void ApplyFullscreenBounds()
    {
        if (!_fullscreen) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0) return;
        var target = screens.FirstOrDefault(screen =>
            string.Equals(screen.DeviceName, _fullscreenDisplayName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            target = FormsScreen.FromHandle(handle);
            _fullscreenDisplayName = target.DeviceName;
        }

        var bounds = target.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Avoid redundant SetWindowPos during WM_DPICHANGED / WM_DISPLAYCHANGE.
        if (GetWindowRect(handle, out var current) &&
            current.Left == bounds.Left && current.Top == bounds.Top &&
            current.Right == bounds.Right && current.Bottom == bounds.Bottom)
        {
            UpdateVisualizerSceneLayout();
            return;
        }

        if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top,
                bounds.Width, bounds.Height, SwpNoZOrder | SwpNoActivate))
            Debug.WriteLine($"Cadence visualizer: SetWindowPos failed: {Marshal.GetLastWin32Error()}");

        UpdateVisualizerSceneLayout();
    }

    private void CycleFullscreenMonitor(int direction)
    {
        if (!_fullscreen) return;
        var screens = FormsScreen.AllScreens
            .OrderBy(screen => screen.Bounds.Left)
            .ThenBy(screen => screen.Bounds.Top)
            .ToArray();
        if (screens.Length < 2) return;

        var index = Array.FindIndex(screens, screen =>
            string.Equals(screen.DeviceName, _fullscreenDisplayName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = 0;
        _fullscreenDisplayName = screens[(index + direction + screens.Length) % screens.Length].DeviceName;
        ApplyFullscreenBounds();
        QueueFullscreenBoundsRefresh();
        if (_viewModel.WindowFullscreenDisplayScope == "Selected")
            _viewModel.ConfigureWindowFullscreenDisplayScope("Selected", _fullscreenDisplayName);
        ReconcileDisplayMirrors();
        ShowControls();
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e) => QueueFullscreenBoundsRefresh();

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_fullscreen && (message is WmDpiChanged or WmDisplayChange))
            QueueFullscreenBoundsRefresh();
        return IntPtr.Zero;
    }

    // Mirrors have independent WPF visual trees and device-specific fullscreen
    // bounds, but bind to the SAME ViewModel and live FFT data as the master.
    private void ReconcileDisplayMirrors()
    {
        if (_isDisplayMirror || _reconcilingDisplayMirrors) return;
        _reconcilingDisplayMirrors = true;
        try
        {
            if (!_fullscreen || _viewModel.WindowFullscreenDisplayScope != "All")
            {
                CloseDisplayMirrors();
                return;
            }
            var desired = FormsScreen.AllScreens
                .Select(screen => screen.DeviceName)
                .Where(device => !string.Equals(device, _fullscreenDisplayName,
                    StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var mirror in _displayMirrors.ToArray())
            {
                if (mirror._forcedDisplayName is null || !desired.Contains(mirror._forcedDisplayName))
                {
                    _displayMirrors.Remove(mirror);
                    mirror.Close();
                }
            }
            foreach (var display in desired)
            {
                if (_displayMirrors.Any(mirror => string.Equals(mirror._forcedDisplayName, display,
                        StringComparison.OrdinalIgnoreCase))) continue;
                var mirror = new WindowVisualizerWindow(_viewModel, true, display, this);
                _displayMirrors.Add(mirror);
                mirror.Closed += (_, _) => _displayMirrors.Remove(mirror);
                mirror.Show();
            }
        }
        finally { _reconcilingDisplayMirrors = false; }
    }

    private void CloseDisplayMirrors()
    {
        foreach (var mirror in _displayMirrors.ToArray())
        {
            _displayMirrors.Remove(mirror);
            mirror.Close();
        }
    }

    private void ExitFullscreenFromMirror()
    {
        if (_fullscreen && !_isDisplayMirror) ToggleFullscreen();
    }

    private void QueueFullscreenBoundsRefresh()
    {
        if (!_fullscreen || _fullscreenRefreshQueued || Dispatcher.HasShutdownStarted) return;
        _fullscreenRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            _fullscreenRefreshQueued = false;
            if (_fullscreen)
            {
                ApplyFullscreenBounds();
                if (!_isDisplayMirror) ReconcileDisplayMirrors();
            }
        }), DispatcherPriority.Background);
    }
}
