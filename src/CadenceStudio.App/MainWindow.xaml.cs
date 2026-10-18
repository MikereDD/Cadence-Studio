using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Track = CadenceStudio.Core.Models.Track;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Core.Enums;

namespace CadenceStudio.App;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmHotKey = 0x0312;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPreviousTrack = 0xB1;
    private const uint VkMediaStop = 0xB2;
    private const uint VkMediaPlayPause = 0xB3;
    private const int MediaHotKeyPrevious = 0xCA01;
    private const int MediaHotKeyPlayPause = 0xCA02;
    private const int MediaHotKeyStop = 0xCA03;
    private const int MediaHotKeyNext = 0xCA04;
    private const double RestoredLibraryPanelWidth = 350;
    private const double MaximizedLibraryPanelWidth = 480;
    private const double RestoredEnrichmentPanelWidth = 232;
    private const double MaximizedEnrichmentPanelWidth = 340;
    private const double RestoredArtworkSize = 196;
    private const double MaximizedArtworkSize = 244;
    private const double RestoredExpandedArtworkSize = 260;
    private const double MaximizedExpandedArtworkSize = 300;
    private const double RestoredExpandedEnrichmentWidth = 340;
    private const double MaximizedExpandedEnrichmentWidth = 430;

    private readonly MainWindowViewModel _viewModel;
    private readonly HashSet<int> _registeredMediaHotKeyIds = [];
    private HwndSource? _windowSource;
    private IntPtr _windowHandle;
    private System.Windows.Point _queueDragStart;
    private Track? _queueDragTrack;
    private bool _queueDragBlocked;
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;
    private byte[]? _lastArtworkBytes;
    private string _lastArtworkFingerprint = "fallback";
    private WindowVisualizerWindow? _windowVisualizer;
    private NowPlayingPopupWindow? _nowPlayingPopup;
    private string _lastPopupTrackTitle = string.Empty;
    private readonly ObservableCollection<GlobalSearchResult> _globalSearchResults = [];

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        GlobalSearchResultsList.ItemsSource = _globalSearchResults;
        _lastArtworkBytes = _viewModel.NowPlayingArtwork;
        _lastArtworkFingerprint = CreateArtworkFingerprint(_lastArtworkBytes);
        _lastPopupTrackTitle = _viewModel.NowPlayingTitle;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        RestoreWindowPlacement();

        Loaded += (_, _) =>
        {
            if (_viewModel.SavedWindowMaximized)
            {
                WindowState = WindowState.Maximized;
                _lastNonMinimizedWindowState = WindowState.Maximized;
            }

            UpdateResponsivePanelWidths();
        };
        StateChanged += MainWindow_StateChanged;
        SizeChanged += MainWindow_SizeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowProcedure);
        RegisterMediaHotKeys();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        CaptureWindowPlacement();

        if (Application.Current is App app && !app.IsExiting)
        {
            if (_viewModel.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            // Explicit-shutdown mode keeps Cadence alive after its main window closes,
            // so route a real close through the application shutdown path.
            e.Cancel = true;
            app.RequestExit();
            return;
        }

        base.OnClosing(e);
    }

    private void CaptureWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;

        _viewModel.CaptureWindowPlacement(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            _lastNonMinimizedWindowState == WindowState.Maximized);
    }

    public void HideToTray()
    {
        if (WindowState != WindowState.Minimized)
        {
            _lastNonMinimizedWindowState = WindowState;
        }

        Hide();
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = _lastNonMinimizedWindowState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        UnregisterMediaHotKeys();
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
        _windowHandle = IntPtr.Zero;
        base.OnClosed(e);
    }


    private void OpenWindowVisualizer_Click(object sender, RoutedEventArgs e) => OpenOrActivateWindowVisualizer();

    private void OpenOrActivateWindowVisualizer(VisualizerWindowMode? requestedMode = null)
    {
        if (requestedMode is { } mode)
        {
            _viewModel.SetWindowVisualizerModeCommand.Execute(mode.ToString());
            if (_windowVisualizer is { IsLoaded: true })
                _windowVisualizer.Close();
        }

        if (_windowVisualizer is { IsLoaded: true })
        {
            if (_windowVisualizer.WindowState == WindowState.Minimized)
                _windowVisualizer.WindowState = WindowState.Normal;
            _windowVisualizer.Show();
            _windowVisualizer.Activate();
            _windowVisualizer.Focus();
            UpdateWindowVisualizerLaunchState(true);
            return;
        }

        _windowVisualizer = new WindowVisualizerWindow(_viewModel);
        _windowVisualizer.Closed += (_, _) =>
        {
            _windowVisualizer = null;
            UpdateWindowVisualizerLaunchState(false);
        };
        _windowVisualizer.Show();
        UpdateWindowVisualizerLaunchState(true);
    }

    private void WindowVisualizerMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OpenWindowVisualizerPopOut_Click(object sender, RoutedEventArgs e) => OpenOrActivateWindowVisualizer(VisualizerWindowMode.PopOut);
    private void OpenWindowVisualizerFloating_Click(object sender, RoutedEventArgs e) => OpenOrActivateWindowVisualizer(VisualizerWindowMode.Floating);
    private void OpenWindowVisualizerFullscreen_Click(object sender, RoutedEventArgs e) => OpenOrActivateWindowVisualizer(VisualizerWindowMode.Fullscreen);
    private void BringWindowVisualizerToFront_Click(object sender, RoutedEventArgs e) => OpenOrActivateWindowVisualizer();

    private void CloseWindowVisualizer_Click(object sender, RoutedEventArgs e)
    {
        _windowVisualizer?.Close();
        _windowVisualizer = null;
        UpdateWindowVisualizerLaunchState(false);
    }

    private void UpdateWindowVisualizerLaunchState(bool isOpen)
    {
        if (WindowVisualizerMenuButton is null)
            return;
        // v1.6: keep the audio-bars icon intact, updating only its tooltip.
        WindowVisualizerMenuButton.ToolTip = isOpen
            ? "Window Visualizer (open) - choose a mode or bring to front"
            : "Window Visualizer";
    }


    private void ShowNowPlayingPopup()
    {
        if (!_viewModel.SongChangePopupEnabled) return;
        if (_viewModel.SuppressPopupWhileCadenceFocused && IsActive) return;
        _nowPlayingPopup?.Close();
        _nowPlayingPopup = new NowPlayingPopupWindow(_viewModel, this);
        _nowPlayingPopup.Closed += (_, _) => _nowPlayingPopup = null;
        _nowPlayingPopup.Show();
    }

    private void RestoreWindowPlacement()
    {
        Width = Math.Clamp(_viewModel.SavedWindowWidth, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
        Height = Math.Clamp(_viewModel.SavedWindowHeight, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));

        if (!_viewModel.HasSavedWindowPlacement ||
            _viewModel.SavedWindowLeft is not { } savedLeft ||
            _viewModel.SavedWindowTop is not { } savedTop)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        const double minimumVisiblePixels = 96;
        var minimumLeft = SystemParameters.VirtualScreenLeft - Width + minimumVisiblePixels;
        var maximumLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - minimumVisiblePixels;
        var minimumTop = SystemParameters.VirtualScreenTop;
        var maximumTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - minimumVisiblePixels;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Clamp(savedLeft, minimumLeft, maximumLeft);
        Top = Math.Clamp(savedTop, minimumTop, maximumTop);
    }

    // ClipToBounds is rectangular in WPF; clip the expanded shell to its real 20-DIP
    // corner radius so blurred artwork/gradients cannot paint through the corners.
    private void ExpandedNowPlayingMainStage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border stage)
        {
            return;
        }

        var width = Math.Max(0d, e.NewSize.Width);
        var height = Math.Max(0d, e.NewSize.Height);
        stage.Clip = new RectangleGeometry(new Rect(0d, 0d, width, height), 20d, 20d);
    }

    // Both spectrum surfaces render gradients, particles and glow effects.  WPF's
    // ClipToBounds is rectangular, so apply an actual rounded clip to each grid.
    // SizeChanged keeps both masks correct while resizing, restoring or maximizing.
    private void ExpandedVisualizerClip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement layer)
        {
            return;
        }

        var width = Math.Max(0d, e.NewSize.Width);
        var height = Math.Max(0d, e.NewSize.Height);
        var targetRadius = layer.Name == nameof(ExpandedVisualizerViewport) ? 15d : 11d;
        var radius = Math.Min(targetRadius, Math.Min(width, height) * 0.5d);
        layer.Clip = new RectangleGeometry(new Rect(0d, 0d, width, height), radius, radius);
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            UpdateResponsivePanelWidths();
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.MinimizeToTray)
        {
            _ = Dispatcher.BeginInvoke(new Action(HideToTray));
            return;
        }

        if (WindowState != WindowState.Minimized)
        {
            _lastNonMinimizedWindowState = WindowState;
        }

        UpdateResponsivePanelWidths();
    }

    private bool _compactInsightsDocked = true;
    private bool _expandedInsightsDocked = true;
    private bool _compactInsightsOpen;
    private bool _expandedInsightsOpen;
    private bool _compactExpandedHero;

    private void NowPlayingInsightsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsNowPlayingExpanded)
        {
            _expandedInsightsOpen = !_expandedInsightsOpen;
        }
        else
        {
            _compactInsightsOpen = !_compactInsightsOpen;
        }
        UpdateResponsivePanelWidths();
    }

    private void UpdateResponsivePanelWidths()
    {
        if (LibraryPanelColumn is null || NowPlayingPanelColumn is null)
        {
            return;
        }

        // Grid dimensions are device-independent pixels, reflecting current window size
        // and DPI. WindowState alone is not a reliable measure of usable content space.
        var workspaceWidth = LibraryWorkspaceGrid.ActualWidth > 100d
            ? LibraryWorkspaceGrid.ActualWidth
            : Math.Max(760d, ActualWidth - 280d);
        var expanded = _viewModel.IsNowPlayingExpanded;

        if (expanded)
        {
            LibraryPanelColumn.Width = new GridLength(0d);
            LibraryNowPlayingSpacerColumn.Width = new GridLength(0d);
            NowPlayingQueueSpacerColumn.Width = new GridLength(0d);
            QueuePanelColumn.Width = new GridLength(0d);
        }
        else
        {
            var libraryWidth = Math.Clamp(workspaceWidth * 0.255d, 265d, 435d);
            var queueWidth = Math.Clamp(workspaceWidth * 0.220d, 235d, 300d);
            const double gap = 12d;
            // Preserve a viable center stage at the supported minimum window width.
            var centerWidth = workspaceWidth - libraryWidth - queueWidth - (gap * 2d);
            if (centerWidth < 360d)
            {
                var shortfall = 360d - centerWidth;
                libraryWidth = Math.Max(245d, libraryWidth - shortfall * 0.6d);
                queueWidth = Math.Max(215d, queueWidth - shortfall * 0.4d);
            }

            LibraryPanelColumn.Width = new GridLength(libraryWidth);
            LibraryNowPlayingSpacerColumn.Width = new GridLength(gap);
            NowPlayingQueueSpacerColumn.Width = new GridLength(gap);
            QueuePanelColumn.Width = new GridLength(queueWidth);
        }

        // Content-width breakpoints, with a 30-DIP return band to avoid flicker.
        var compactWidth = workspaceWidth - LibraryPanelColumn.Width.Value - QueuePanelColumn.Width.Value
                           - LibraryNowPlayingSpacerColumn.Width.Value - NowPlayingQueueSpacerColumn.Width.Value;
        _compactInsightsDocked = compactWidth >= (_compactInsightsDocked ? 650d : 680d);
        _expandedInsightsDocked = workspaceWidth >= (_expandedInsightsDocked ? 1010d : 1040d);

        CompactInsightSpacerColumn.Width = new GridLength(_compactInsightsDocked ? 14d : 0d);
        EnrichmentPanelColumn.Width = new GridLength(_compactInsightsDocked
            ? Math.Clamp(compactWidth * 0.38d, 258d, 340d) : 0d);
        Grid.SetColumn(CompactInsightsBorder, _compactInsightsDocked ? 2 : 0);
        CompactNowPlayingMainStage.Visibility = (!_compactInsightsDocked && _compactInsightsOpen)
            ? Visibility.Collapsed : Visibility.Visible;
        CompactInsightsBorder.Visibility = (_compactInsightsDocked || _compactInsightsOpen)
            ? Visibility.Visible : Visibility.Collapsed;

        ExpandedEnrichmentPanelColumn.Width = new GridLength(_expandedInsightsDocked
            ? Math.Clamp(workspaceWidth * 0.285d, 315d, 430d) : 0d);
        Grid.SetColumn(ExpandedInsightsBorder, _expandedInsightsDocked ? 2 : 0);
        ExpandedNowPlayingMainStage.Visibility = (!_expandedInsightsDocked && _expandedInsightsOpen)
            ? Visibility.Collapsed : Visibility.Visible;
        ExpandedInsightsBorder.Visibility = (_expandedInsightsDocked || _expandedInsightsOpen)
            ? Visibility.Visible : Visibility.Collapsed;

        var showInsightsToggle = expanded ? !_expandedInsightsDocked : !_compactInsightsDocked;
        NowPlayingInsightsToggleButton.Visibility = showInsightsToggle ? Visibility.Visible : Visibility.Collapsed;
        var insightsOpen = expanded ? _expandedInsightsOpen : _compactInsightsOpen;
        NowPlayingInsightsToggleButton.Content = insightsOpen ? "NOW PLAYING" : "INSIGHTS";
        NowPlayingInsightsToggleButton.ToolTip = insightsOpen
            ? "Return to the Now Playing artwork and visualizer"
            : "Show track details, lyrics, and artist biography";

        // 1. All responsive choices use actual available WPF DIPs, never only WindowState.
        var verticalRoom = Math.Clamp((ActualHeight - 720d) / 360d, 0d, 1d);
        var compactVisualizerHeight = 163d + 72d * verticalRoom;
        CompactVisualizerRow.Height = new GridLength(compactVisualizerHeight);
        CompactVisualizerBars.Height = Math.Max(90d, compactVisualizerHeight - 65d);

        // At restored sizes, stack artwork to the left of a readable two-line title.
        // This prevents the art/title/metadata column from growing taller than the
        // remaining stage above the visualizer. Full-sized layouts remain centered.
        var availableCompactHeroWidth = compactWidth - (_compactInsightsDocked
            ? EnrichmentPanelColumn.Width.Value + CompactInsightSpacerColumn.Width.Value : 0d) - 36d;
        var compactSideBySide = !_compactInsightsDocked && availableCompactHeroWidth < 670d;
        var compactArtworkSize = compactSideBySide
            ? Math.Clamp((ActualHeight - 700d) * 0.14d + 118d, 118d, 165d)
            : 138d + 76d * verticalRoom;
        NowPlayingArtworkBorder.Width = compactArtworkSize;
        NowPlayingArtworkBorder.Height = compactArtworkSize;
        CompactHeroArtworkColumn.Width = compactSideBySide
            ? new GridLength(compactArtworkSize + 6d) : new GridLength(1d, GridUnitType.Star);
        CompactHeroGapColumn.Width = new GridLength(compactSideBySide ? 12d : 0d);
        CompactHeroGrid.ColumnDefinitions[2].Width = compactSideBySide
            ? new GridLength(1d, GridUnitType.Star) : new GridLength(0d);
        Grid.SetColumn(CompactHeroMetadata, compactSideBySide ? 2 : 0);
        Grid.SetRow(CompactHeroMetadata, compactSideBySide ? 0 : 1);
        CompactHeroMetadata.Margin = compactSideBySide
            ? new Thickness(4d, 0d, 4d, 0d) : new Thickness(18d, 10d, 18d, 4d);

        // Queue rows use a 192-DIP intrinsic minimum after the XAML fix. Leave
        // enough content room for play/remove actions and a visible track title.
        if (!expanded && QueuePanelColumn.Width.Value < 250d)
        {
            var additional = 250d - QueuePanelColumn.Width.Value;
            var candidate = Math.Max(250d, LibraryPanelColumn.Width.Value - additional);
            var recovered = LibraryPanelColumn.Width.Value - candidate;
            if (recovered > 0d)
            {
                LibraryPanelColumn.Width = new GridLength(candidate);
                QueuePanelColumn.Width = new GridLength(QueuePanelColumn.Width.Value + recovered);
            }
        }

        // The playback dock is independent of the Library view, so it must also
        // resize. Reduce its side columns before clipping any transport buttons.
        var dockWidth = PlayerDockLayoutGrid.ActualWidth > 400d
            ? PlayerDockLayoutGrid.ActualWidth : Math.Max(700d, ActualWidth - 310d);
        var trackWidth = Math.Clamp(dockWidth * 0.215d, 174d, 300d);
        var volumeWidth = Math.Clamp(dockWidth * 0.175d, 158d, 300d);
        PlayerDockTrackColumn.Width = new GridLength(trackWidth);
        PlayerDockVolumeColumn.Width = new GridLength(volumeWidth);
        PlayerDockControlsGrid.Margin = new Thickness(dockWidth < 1120d ? 8d : 20d, 0d,
            dockWidth < 1120d ? 8d : 20d, 0d);
        var shortDock = dockWidth < 950d;
        DockRepeatText.Visibility = shortDock ? Visibility.Collapsed : Visibility.Visible;
        DockShuffleText.Visibility = shortDock ? Visibility.Collapsed : Visibility.Visible;

        // Expanded stage keeps artwork and title balanced at normal-window sizes.
        var availableExpandedStageWidth = workspaceWidth - (_expandedInsightsDocked
            ? ExpandedEnrichmentPanelColumn.Width.Value + 18d : 0d) - 44d;
        // Restored windows often have nearly the same height as maximized ones,
        // but significantly less width. Adapt the hero to available stage size,
        // not WindowState, so the entire visualizer fits above the player dock.
        // A 40-DIP hysteresis band avoids jumping repeatedly at the threshold.
        if (_compactExpandedHero)
        {
            _compactExpandedHero = availableExpandedStageWidth < 875d || ActualHeight < 790d;
        }
        else
        {
            _compactExpandedHero = availableExpandedStageWidth < 835d || ActualHeight < 770d;
        }

        var expandedArtByHeight = Math.Clamp((ActualHeight - 720d) * 0.25d + 165d,
            175d, 300d);
        var expandedArtworkSize = _compactExpandedHero
            ? Math.Clamp(Math.Min(availableExpandedStageWidth * 0.26d,
                ActualHeight * 0.19d), 155d, 178d)
            : Math.Clamp(Math.Min(availableExpandedStageWidth * 0.35d,
                expandedArtByHeight), 175d, 300d);
        ExpandedNowPlayingArtworkBorder.Width = expandedArtworkSize;
        ExpandedNowPlayingArtworkBorder.Height = expandedArtworkSize;
        ExpandedNowPlayingMainStage.Padding = new Thickness(_compactExpandedHero ? 12d : 18d);
        ExpandedHeroArtColumn.Width = new GridLength(expandedArtworkSize + (_compactExpandedHero ? 10d : 16d));
        ExpandedHeroGapColumn.Width = new GridLength(_compactExpandedHero ? 12d : 16d);
        ExpandedHeroGrid.MinHeight = expandedArtworkSize + (_compactExpandedHero ? 4d : 12d);
        ExpandedHeroToVisualizerGapRow.Height = new GridLength(_compactExpandedHero ? 8d : 14d);

        var titleSpace = availableExpandedStageWidth - expandedArtworkSize - 32d;
        ExpandedHeroTitleText.FontSize = _compactExpandedHero ? 29d :
            (titleSpace < 500d ? 30d : (titleSpace < 650d ? 34d : 38d));
        ExpandedHeroTitleText.Margin = new Thickness(0d,
            _compactExpandedHero ? 8d : (ActualHeight < 950d ? 12d : 20d), 0d, 0d);
        ExpandedHeroArtistText.Margin = new Thickness(0d, _compactExpandedHero ? 6d : 10d, 0d, 0d);
        ExpandedHeroAlbumText.Margin = new Thickness(0d, _compactExpandedHero ? 3d : 7d, 0d, 0d);
        ExpandedHeroMetadataWrap.Margin = new Thickness(0d, _compactExpandedHero ? 9d : 20d, 0d, 0d);
        ExpandedHeroAccentRule.Margin = new Thickness(0d, _compactExpandedHero ? 9d : 22d, 0d, 0d);

        var expandedVisualizerHeight = _compactExpandedHero
            ? Math.Clamp(ActualHeight * 0.16d, 132d, 155d)
            : 155d + (85d * verticalRoom);
        ExpandedVisualizerBorder.Height = expandedVisualizerHeight;
        ExpandedVisualizerBars.Height = expandedVisualizerHeight - 48d;
        _viewModel.SetExpandedVisualizerHeight(ExpandedVisualizerBars.Height - 4d);

        // Up Next occupies available remaining space but its cards do not grow.
        // The scroll viewer keeps all contents reachable when height is limited.
        ExpandedQueueBorder.Height = double.NaN;
        ExpandedQueueBorder.MinHeight = 178d + (46d * verticalRoom);
        ExpandedHorizontalQueueListBox.Height = 128d + (20d * verticalRoom);
    }
    private void NowPlayingMode_Click(object sender, RoutedEventArgs e)
    {
        // The command toggles the persisted VM state; update columns after WPF
        // has completed command execution, then introduce the new stage gently.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateResponsivePanelWidths();
            AnimateNowPlayingModeSwitch();
        }));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.NowPlayingTitle))
        {
            var title = _viewModel.NowPlayingTitle;
            if (!string.IsNullOrWhiteSpace(title) && title != "Nothing playing" && title != _lastPopupTrackTitle)
            {
                _lastPopupTrackTitle = title;
                ShowNowPlayingPopup();
            }
        }

        if (e.PropertyName == nameof(MainWindowViewModel.NowPlayingArtwork))
        {
            QueueArtworkAnimationIfChanged();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedNowPlayingTab))
        {
            _ = Dispatcher.BeginInvoke(new Action(AnimateInsightTab));
        }
    }

    private void AnimateNowPlayingModeSwitch()
    {
        var target = _viewModel.IsNowPlayingExpanded
            ? ExpandedNowPlayingView
            : CompactNowPlayingView;
        AnimateEntrance(target, 10d, 220d);
    }

    private void QueueArtworkAnimationIfChanged()
    {
        var artworkBytes = _viewModel.NowPlayingArtwork;

        // Playback snapshots update position and state frequently. Those updates
        // also raise NowPlayingArtwork for binding refresh, even when the visual
        // artwork is unchanged. Ignore the same byte-array instance immediately,
        // then compare a content fingerprint when a different track supplies a
        // separate copy of the same album image.
        if (ReferenceEquals(artworkBytes, _lastArtworkBytes))
        {
            return;
        }

        var fingerprint = CreateArtworkFingerprint(artworkBytes);
        _lastArtworkBytes = artworkBytes;
        if (string.Equals(fingerprint, _lastArtworkFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _lastArtworkFingerprint = fingerprint;
        _ = Dispatcher.BeginInvoke(new Action(AnimateArtworkChange));
    }

    private static string CreateArtworkFingerprint(byte[]? artworkBytes)
    {
        if (artworkBytes is not { Length: > 0 })
        {
            return "fallback";
        }

        return $"{artworkBytes.Length}:{Convert.ToHexString(SHA256.HashData(artworkBytes))}";
    }

    private void AnimateArtworkChange()
    {
        AnimateEntrance(CompactArtworkSurface, 0d, 260d, 0.48d);
        AnimateEntrance(ExpandedArtworkSurface, 0d, 260d, 0.48d);
    }

    private void AnimateInsightTab()
    {
        var target = _viewModel.IsNowPlayingExpanded
            ? ExpandedInsightContent
            : CompactInsightContent;
        AnimateEntrance(target, 6d, 170d, 0.55d);
    }

    private void QueueCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            AnimateEntrance(element, 5d, 150d, 0.68d);
        }
    }

    private void AnimateEntrance(FrameworkElement? element, double verticalOffset, double durationMilliseconds, double startOpacity = 0d)
    {
        if (element is null)
        {
            return;
        }

        if (_viewModel.ReduceMotion)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1d;
            if (element.RenderTransform is TranslateTransform reducedTransform)
            {
                reducedTransform.BeginAnimation(TranslateTransform.YProperty, null);
                reducedTransform.Y = 0d;
            }
            return;
        }

        var translate = element.RenderTransform as TranslateTransform;
        if (translate is null)
        {
            translate = new TranslateTransform();
            element.RenderTransform = translate;
        }

        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.Opacity = startOpacity;
        translate.Y = verticalOffset;

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(startOpacity, 1d, duration)
        {
            EasingFunction = easing
        });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(verticalOffset, 0d, duration)
        {
            EasingFunction = easing
        });
    }

    private void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        _queueDragStart = e.GetPosition(listBox);
        _queueDragBlocked = FindVisualAncestor<Button>(e.OriginalSource as DependencyObject) is not null;
        _queueDragTrack = _queueDragBlocked
            ? null
            : FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as Track;
    }

    private void QueueList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ResetQueueDragState();

    private void QueueList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox ||
            e.LeftButton != MouseButtonState.Pressed ||
            _queueDragBlocked ||
            _queueDragTrack is null)
        {
            return;
        }

        var current = e.GetPosition(listBox);
        var difference = _queueDragStart - current;
        if (Math.Abs(difference.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(difference.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var track = _queueDragTrack;
        ResetQueueDragState();

        var data = new DataObject(typeof(Track), track);
        DragDrop.DoDragDrop(listBox, data, DragDropEffects.Move);
        e.Handled = true;
    }

    private void QueueList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Track))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void QueueList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Track)) || e.Data.GetData(typeof(Track)) is not Track source)
        {
            return;
        }

        var item = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is Track target)
        {
            _viewModel.MoveQueueTrack(source, target);
        }
        else
        {
            _viewModel.MoveQueueTrackToEnd(source);
        }

        ResetQueueDragState();
        e.Handled = true;
    }

    private void HorizontalQueue_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindVisualAncestor<ScrollViewer>(e.OriginalSource as DependencyObject);
        if (scrollViewer is null || scrollViewer.ScrollableWidth <= 0)
        {
            return;
        }

        // Translate the normal wheel gesture into horizontal movement for the
        // expanded queue rail. Touchpad horizontal gestures continue to work natively.
        var targetOffset = scrollViewer.HorizontalOffset - (e.Delta * 0.75);
        scrollViewer.ScrollToHorizontalOffset(Math.Clamp(targetOffset, 0, scrollViewer.ScrollableWidth));
        e.Handled = true;
    }

    private void ResetQueueDragState()
    {
        _queueDragTrack = null;
        _queueDragBlocked = false;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }

        return null;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void RenamePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPlaylist is null)
        {
            return;
        }

        var dialog = new PlaylistRenameDialog(_viewModel.SelectedPlaylist.Name)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.RenameSelectedPlaylistAsync(dialog.PlaylistName);
        }
    }


    private void Next_Click(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(_viewModel.NextCommand);
        e.Handled = true;
    }

    // Universal v1.5: the normal left-click opens the native WPF context menu.
    // Existing theme selection, persistence and visualizer handling are unchanged.
    private void QuickThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowKeyboardShortcuts();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog(_viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void MetadataWorkshop_Click(object sender, RoutedEventArgs e)
    {
        var track = _viewModel.SelectedQueueTrack ?? _viewModel.CurrentTrack;
        if (track is null)
        {
            MessageBox.Show(this, "Select a queued track or begin playing a track first.", "Metadata Workshop", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var enrichment = _viewModel.LastEnrichment?.TrackPath.Equals(track.FilePath, StringComparison.OrdinalIgnoreCase) == true
            ? _viewModel.LastEnrichment
            : null;
        var dialog = new MetadataWorkshopDialog(
            track,
            enrichment,
            (forceRefresh, cancellationToken) => _viewModel.GetMetadataWorkshopProposalAsync(track, forceRefresh, cancellationToken),
            (currentEnrichment, candidate, forceRefresh, cancellationToken) =>
                _viewModel.SelectMetadataWorkshopReleaseAsync(currentEnrichment, candidate, forceRefresh, cancellationToken))
        {
            Owner = this
        };
        dialog.ShowDialog();
        if (dialog.TagsChanged)
        {
            MessageBox.Show(this, "Tags changed successfully. Rescan the library or reload the track to refresh all displayed metadata.", "Metadata Workshop", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ShowKeyboardShortcuts()
    {
        var dialog = new KeyboardShortcutsDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.F1 || ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.Oem2))
        {
            ShowKeyboardShortcuts();
            e.Handled = true;
            return;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            ExecuteCommand(_viewModel.ExportSelectedPlaylistCommand);
            e.Handled = true;
            return;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.V)
        {
            OpenOrActivateWindowVisualizer();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    ExecuteCommand(_viewModel.OpenAudioFilesCommand);
                    e.Handled = true;
                    return;
                case Key.L:
                    NavigateTo("Library");
                    e.Handled = true;
                    return;
                case Key.P:
                    NavigateTo("Playlists");
                    e.Handled = true;
                    return;
                case Key.Q:
                    NavigateTo("Queue");
                    e.Handled = true;
                    return;
                case Key.OemComma:
                    NavigateTo("Settings");
                    e.Handled = true;
                    return;
                case Key.F:
                    FocusCurrentSearch();
                    e.Handled = true;
                    return;
                case Key.E:
                    ExecuteCommand(_viewModel.ToggleNowPlayingModeCommand);
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateResponsivePanelWidths();
                        AnimateNowPlayingModeSwitch();
                    }));
                    e.Handled = true;
                    return;
                case Key.S:
                    ExecuteCommand(_viewModel.SaveQueueAsPlaylistCommand);
                    e.Handled = true;
                    return;
                case Key.Left:
                    ExecuteCommand(_viewModel.PreviousCommand);
                    e.Handled = true;
                    return;
                case Key.Right:
                    ExecuteCommand(_viewModel.NextCommand);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.Escape && IsTextEntryFocused())
        {
            Keyboard.ClearFocus();
            Focus();
            e.Handled = true;
            return;
        }

        if (modifiers != ModifierKeys.None || IsInteractiveControlFocused())
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                ExecuteCommand(_viewModel.TogglePlaybackCommand);
                e.Handled = true;
                break;
            case Key.Left:
                if (_viewModel.CanSeek)
                {
                    _viewModel.PositionSeconds -= 5;
                    e.Handled = true;
                }
                break;
            case Key.Right:
                if (_viewModel.CanSeek)
                {
                    _viewModel.PositionSeconds += 5;
                    e.Handled = true;
                }
                break;
            case Key.Up:
                _viewModel.Volume += 0.05;
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.Volume -= 0.05;
                e.Handled = true;
                break;
            case Key.M:
                ExecuteCommand(_viewModel.ToggleMuteCommand);
                e.Handled = true;
                break;
            case Key.S:
                ExecuteCommand(_viewModel.ToggleShuffleCommand);
                e.Handled = true;
                break;
            case Key.R:
                ExecuteCommand(_viewModel.CycleRepeatCommand);
                e.Handled = true;
                break;
        }
    }

    private void NavigateTo(string section)
    {
        ExecuteCommand(_viewModel.NavigateCommand, section);
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateResponsivePanelWidths();
            Focus();
        }));
    }

    private void FocusCurrentSearch()
    {
        TextBox? target = null;
        if (_viewModel.IsLibraryActive)
        {
            target = LibrarySearchTextBox;
        }
        else if (_viewModel.IsPlaylistsActive)
        {
            target = PlaylistSearchTextBox;
        }
        else if (_viewModel.IsQueueActive)
        {
            target = QueueSearchTextBox;
        }

        if (target is null || target.Visibility != Visibility.Visible)
        {
            return;
        }

        target.Focus();
        target.SelectAll();
    }

    private static void ExecuteCommand(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static bool IsTextEntryFocused() => Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;

    private static bool IsInteractiveControlFocused() => Keyboard.FocusedElement is
        TextBoxBase or PasswordBox or ComboBox or ButtonBase or Selector or Slider;

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotKey)
        {
            handled = HandleMediaHotKey(wParam.ToInt32());
            return IntPtr.Zero;
        }

        if (message == WmGetMinMaxInfo)
        {
            ApplyMonitorWorkArea(windowHandle, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void RegisterMediaHotKeys()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        TryRegisterMediaHotKey(MediaHotKeyPrevious, VkMediaPreviousTrack);
        TryRegisterMediaHotKey(MediaHotKeyPlayPause, VkMediaPlayPause);
        TryRegisterMediaHotKey(MediaHotKeyStop, VkMediaStop);
        TryRegisterMediaHotKey(MediaHotKeyNext, VkMediaNextTrack);
    }

    private void TryRegisterMediaHotKey(int id, uint virtualKey)
    {
        // Registration can legitimately fail when another application has already
        // claimed a particular media key. Leave that key untouched rather than
        // treating the conflict as an application failure.
        if (RegisterHotKey(_windowHandle, id, ModNoRepeat, virtualKey))
        {
            _registeredMediaHotKeyIds.Add(id);
        }
    }

    private void UnregisterMediaHotKeys()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _registeredMediaHotKeyIds.Clear();
            return;
        }

        foreach (var id in _registeredMediaHotKeyIds)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _registeredMediaHotKeyIds.Clear();
    }

    private bool HandleMediaHotKey(int id)
    {
        if (!_registeredMediaHotKeyIds.Contains(id))
        {
            return false;
        }

        switch (id)
        {
            case MediaHotKeyPrevious:
                ExecuteCommand(_viewModel.PreviousCommand);
                break;
            case MediaHotKeyPlayPause:
                ExecuteCommand(_viewModel.TogglePlaybackCommand);
                break;
            case MediaHotKeyStop:
                ExecuteCommand(_viewModel.StopPlaybackCommand);
                break;
            case MediaHotKeyNext:
                ExecuteCommand(_viewModel.NextCommand);
                break;
            default:
                return false;
        }

        return true;
    }

    private static void ApplyMonitorWorkArea(IntPtr windowHandle, IntPtr minMaxInfoPointer)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        var workArea = monitorInfo.WorkArea;
        var monitorArea = monitorInfo.MonitorArea;

        minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;

        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
    }

    private void ClearGlobalSearch_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LibrarySearchText = string.Empty;
        _globalSearchResults.Clear();
        GlobalSearchPopup.IsOpen = false;
        GlobalSearchTextBox.Focus();
    }

    private void GlobalSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _globalSearchResults.Clear();
        foreach (var result in _viewModel.SearchGlobal(GlobalSearchTextBox.Text))
        {
            _globalSearchResults.Add(result);
        }

        GlobalSearchPopup.IsOpen = GlobalSearchTextBox.IsKeyboardFocusWithin && GlobalSearchTextBox.Text.Trim().Length >= 2;
    }

    private void GlobalSearchTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        GlobalSearchTextBox_TextChanged(sender, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    }

    private void GlobalSearchResult_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GlobalSearchResult result)
        {
            return;
        }

        _viewModel.PlayLibraryTrackCommand.Execute(result.Track);
        GlobalSearchPopup.IsOpen = false;
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void GlobalSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && GlobalSearchPopup.IsOpen && GlobalSearchResultsList.Items.Count > 0)
        {
            GlobalSearchResultsList.SelectedIndex = Math.Max(0, GlobalSearchResultsList.SelectedIndex);
            GlobalSearchResultsList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_viewModel.LibrarySearchText))
        {
            _viewModel.LibrarySearchText = string.Empty;
            _globalSearchResults.Clear();
            GlobalSearchPopup.IsOpen = false;
        }
        else
        {
            Keyboard.ClearFocus();
        }

        e.Handled = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rectangle MonitorArea;
        public Rectangle WorkArea;
        public uint Flags;
    }
}
