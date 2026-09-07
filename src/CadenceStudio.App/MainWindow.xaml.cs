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
        if (WindowVisualizerLaunchButton is null)
            return;
        WindowVisualizerLaunchButton.Content = isOpen ? "BRING VISUALIZER FRONT" : "WINDOW VISUALIZER";
        WindowVisualizerLaunchButton.ToolTip = isOpen
            ? "Bring the open Window Visualizer to front"
            : "Open the Window Visualizer in the last-used mode (Ctrl+Shift+V)";
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

    private void UpdateResponsivePanelWidths()
    {
        if (LibraryPanelColumn is null)
        {
            return;
        }

        if (_viewModel.IsNowPlayingExpanded)
        {
            LibraryPanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
            LibraryNowPlayingSpacerColumn.Width = new GridLength(0, GridUnitType.Pixel);
            NowPlayingQueueSpacerColumn.Width = new GridLength(0, GridUnitType.Pixel);
            QueuePanelColumn.Width = new GridLength(0, GridUnitType.Pixel);
        }
        else
        {
            var targetWidth = WindowState == WindowState.Maximized
                ? MaximizedLibraryPanelWidth
                : RestoredLibraryPanelWidth;
            LibraryPanelColumn.Width = new GridLength(targetWidth, GridUnitType.Pixel);
            LibraryNowPlayingSpacerColumn.Width = new GridLength(16, GridUnitType.Pixel);
            NowPlayingQueueSpacerColumn.Width = new GridLength(16, GridUnitType.Pixel);
            QueuePanelColumn.Width = new GridLength(300, GridUnitType.Pixel);
        }

        if (EnrichmentPanelColumn is not null)
        {
            var enrichmentWidth = WindowState == WindowState.Maximized
                ? MaximizedEnrichmentPanelWidth
                : RestoredEnrichmentPanelWidth;
            EnrichmentPanelColumn.Width = new GridLength(enrichmentWidth, GridUnitType.Pixel);
        }

        if (ExpandedEnrichmentPanelColumn is not null)
        {
            var expandedWidth = WindowState == WindowState.Maximized
                ? MaximizedExpandedEnrichmentWidth
                : RestoredExpandedEnrichmentWidth;
            ExpandedEnrichmentPanelColumn.Width = new GridLength(expandedWidth, GridUnitType.Pixel);
        }

        if (NowPlayingArtworkBorder is not null)
        {
            var artworkSize = WindowState == WindowState.Maximized
                ? MaximizedArtworkSize
                : RestoredArtworkSize;
            NowPlayingArtworkBorder.Width = artworkSize;
            NowPlayingArtworkBorder.Height = artworkSize;
        }

        if (ExpandedNowPlayingArtworkBorder is not null)
        {
            var expandedArtworkSize = WindowState == WindowState.Maximized
                ? MaximizedExpandedArtworkSize
                : RestoredExpandedArtworkSize;
            ExpandedNowPlayingArtworkBorder.Width = expandedArtworkSize;
            ExpandedNowPlayingArtworkBorder.Height = expandedArtworkSize;
        }

        // Expanded Eye Candy uses the vertical room progressively. dev.10.5.3
        // gives Up next a more substantial artwork-led stage while keeping the
        // visualizer sizing from dev.10.5.2 intact. Taller windows add modest
        // queue breathing room without crowding transport or insights.
        var heightProgress = Math.Clamp((ActualHeight - 720d) / 360d, 0d, 1d);
        if (ExpandedVisualizerBorder is not null)
        {
            ExpandedVisualizerBorder.Height = 170d + (100d * heightProgress);
        }

        if (ExpandedVisualizerBars is not null)
        {
            ExpandedVisualizerBars.Height = 122d + (100d * heightProgress);
            _viewModel.SetExpandedVisualizerHeight(ExpandedVisualizerBars.Height - 4d);
        }

        if (ExpandedQueueBorder is not null)
        {
            ExpandedQueueBorder.Height = 190d + (40d * heightProgress);
        }

        if (ExpandedHorizontalQueueListBox is not null)
        {
            ExpandedHorizontalQueueListBox.Height = 138d + (18d * heightProgress);
        }
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
