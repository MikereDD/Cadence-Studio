using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CadenceStudio.App.Mvvm;
using CadenceStudio.Core;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Enums;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackService _playbackService;
    private readonly IMetadataService _metadataService;
    private readonly ILibraryService _libraryService;
    private readonly ISessionService _sessionService;
    private readonly IFolderPickerService _folderPicker;
    private readonly IFilePickerService _filePicker;
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly Random _random = new();
    private readonly List<Track> _libraryTracks = [];


    private string? _selectedLibraryRoot;
    private Track? _selectedQueueTrack;
    private string _activeSection;
    private string _sectionSubtitle;
    private string _statusText;
    private bool _isShuffleEnabled;
    private RepeatMode _repeatMode;
    private VisualizerPalette _visualizerPalette;
    private double _volume;
    private bool _isMuted;
    private double _positionSeconds;
    private bool _isUpdatingPosition;
    private bool _disposed;
    private PlaybackState _lastObservedPlaybackState = PlaybackState.Stopped;
    private CancellationTokenSource? _libraryScanCancellation;
    private string _librarySearchText = string.Empty;
    private bool _isLibraryScanning;
    private string _libraryScanStatus = "Library index not loaded";
    private int _libraryScanFilesDiscovered;
    private int _libraryScanFilesProcessed;
    private int _libraryScanTracksIndexed;
    private int _libraryScanErrors;
    private DateTimeOffset? _libraryLastScanUtc;
    private readonly string? _sessionTrackPath;
    private readonly double _sessionPositionSeconds;
    private int _sessionMissingQueueFiles;

    public MainWindowViewModel(
        IPlaybackService playbackService,
        IMetadataService metadataService,
        ITrackEnrichmentService enrichmentService,
        ILibraryService libraryService,
        IPlaylistService playlistService,
        ISessionService sessionService,
        IFolderPickerService folderPicker,
        IFilePickerService filePicker,
        IAppLogger logger,
        AppSettings settings)
    {
        _playbackService = playbackService;
        _metadataService = metadataService;
        _enrichmentService = enrichmentService;
        _libraryService = libraryService;
        _playlistService = playlistService;
        _sessionService = sessionService;
        _folderPicker = folderPicker;
        _filePicker = filePicker;
        _logger = logger;
        _settings = settings;

        LibraryRoots = new ObservableCollection<string>(settings.LibraryRoots);
        LibraryFolders = [];
        Queue = [];
        InitializeQueueAndPlaylistState();
        InitializeEqualizerState(settings.Equalizer);
        InitializeVisualizerState(settings.Visualizer);
        InitializeAppearanceState(settings.Theme, settings.Typography, settings.TextSize, settings.ReduceMotion, settings.IsNowPlayingExpanded, settings.CloseToTray, settings.MinimizeToTray);
        InitializeSystemIntegrationState(settings.StartWithWindows);
        InitializeEnrichmentState(settings.SelectedNowPlayingTab);

        _activeSection = NormalizeSection(settings.SelectedSection);
        _sectionSubtitle = GetSectionSubtitle(_activeSection);
        _isShuffleEnabled = settings.ShuffleEnabled;
        _repeatMode = settings.RepeatMode;
        _visualizerPalette = settings.VisualizerPalette;
        RefreshVisualizerBrushes();
        _volume = settings.Volume;
        _isMuted = settings.IsMuted;
        _sessionTrackPath = settings.CurrentTrackPath;
        _sessionPositionSeconds = settings.PositionSeconds;

        RestoreQueue(settings.QueuePaths, settings.SelectedQueueTrackPath ?? settings.CurrentTrackPath);
        _statusText = Queue.Count > 0
            ? $"Playback engine online • Restored {QueueCountText.ToLowerInvariant()}{MissingQueueStatusSuffix()}"
            : _sessionMissingQueueFiles > 0
                ? $"Playback engine online • Skipped {_sessionMissingQueueFiles} missing queue {(_sessionMissingQueueFiles == 1 ? "file" : "files")}"
                : "Playback engine online • Open music to begin";

        NavigateCommand = new RelayCommand<string>(Navigate);
        AddLibraryRootCommand = new RelayCommand(AddLibraryRoot);
        RemoveLibraryRootCommand = new RelayCommand(RemoveLibraryRoot);
        ScanLibraryCommand = new RelayCommand(() => _ = ScanLibraryAsync());
        CancelLibraryScanCommand = new RelayCommand(CancelLibraryScan);
        AddLibraryTrackCommand = new RelayCommand<Track>(track => _ = AddLibraryTracksAsync(track is null ? [] : [track], false));
        PlayLibraryTrackCommand = new RelayCommand<Track>(track => _ = AddLibraryTracksAsync(track is null ? [] : [track], true));
        AddLibraryFolderCommand = new RelayCommand<LibraryFolderNode>(folder =>
            _ = AddLibraryTracksAsync(folder?.DescendantTracks() ?? [], false));
        PlayLibraryFolderCommand = new RelayCommand<LibraryFolderNode>(folder =>
            _ = AddLibraryTracksAsync(folder?.DescendantTracks() ?? [], true, replaceQueue: true));
        OpenAudioFilesCommand = new RelayCommand(OpenAudioFiles);
        TogglePlaybackCommand = new RelayCommand(TogglePlayback);
        StopPlaybackCommand = new RelayCommand(StopPlayback);
        PreviousCommand = new RelayCommand(PreviousTrack);
        NextCommand = new RelayCommand(NextTrack);
        PlaySelectedCommand = new RelayCommand(PlaySelectedTrack);
        PlayTrackCommand = new RelayCommand<Track>(PlayTrack);
        RemoveTrackCommand = new RelayCommand<Track>(RemoveTrack);
        ToggleShuffleCommand = new RelayCommand(ToggleShuffle);
        CycleRepeatCommand = new RelayCommand(CycleRepeat);
        CycleVisualizerCommand = new RelayCommand(CycleVisualizer);
        ImportPlaylistCommand = new RelayCommand(() => _ = ImportPlaylistAsync());
        ExportQueueCommand = new RelayCommand(() => _ = ExportQueueAsync());
        ClearQueueCommand = new RelayCommand(ClearQueue);
        ToggleMuteCommand = new RelayCommand(ToggleMute);
        InitializeQueueAndPlaylistCommands();
        InitializeEqualizerCommands();
        InitializeVisualizerCommands();
        InitializeAppearanceCommands();
        InitializeSystemIntegrationCommands();
        InitializeEnrichmentCommands();
        InitializeQueueArtworkCommands();

        _playbackService.SetEqualizer(BuildEqualizerSettings());
        _playbackService.SetVisualizer(BuildVisualizerSettings());
        _playbackService.SnapshotChanged += OnPlaybackSnapshotChanged;
        _playbackService.PlaybackEnded += OnPlaybackEnded;
        _playbackService.VisualizerFrameReady += OnVisualizerFrameReady;
        _playbackService.SetVolume(_isMuted ? 0 : _volume);
    }

    public string AppName => ProductInfo.Name;
    public string VersionText => ProductInfo.DisplayVersion;

    public ObservableCollection<string> LibraryRoots { get; }
    public ObservableCollection<LibraryFolderNode> LibraryFolders { get; }
    public ObservableCollection<Track> Queue { get; }

    public RelayCommand<string> NavigateCommand { get; }
    public RelayCommand AddLibraryRootCommand { get; }
    public RelayCommand RemoveLibraryRootCommand { get; }
    public RelayCommand ScanLibraryCommand { get; }
    public RelayCommand CancelLibraryScanCommand { get; }
    public RelayCommand<Track> AddLibraryTrackCommand { get; }
    public RelayCommand<Track> PlayLibraryTrackCommand { get; }
    public RelayCommand<LibraryFolderNode> AddLibraryFolderCommand { get; }
    public RelayCommand<LibraryFolderNode> PlayLibraryFolderCommand { get; }
    public RelayCommand OpenAudioFilesCommand { get; }
    public RelayCommand TogglePlaybackCommand { get; }
    public RelayCommand StopPlaybackCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PlaySelectedCommand { get; }
    public RelayCommand<Track> PlayTrackCommand { get; }
    public RelayCommand<Track> RemoveTrackCommand { get; }
    public RelayCommand ToggleShuffleCommand { get; }
    public RelayCommand CycleRepeatCommand { get; }
    public RelayCommand CycleVisualizerCommand { get; }
    public RelayCommand ImportPlaylistCommand { get; }
    public RelayCommand ExportQueueCommand { get; }
    public RelayCommand ClearQueueCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }

    public string? SelectedLibraryRoot
    {
        get => _selectedLibraryRoot;
        set => SetProperty(ref _selectedLibraryRoot, value);
    }


    public string LibrarySearchText
    {
        get => _librarySearchText;
        set
        {
            if (SetProperty(ref _librarySearchText, value ?? string.Empty))
            {
                RebuildLibraryBrowser();
            }
        }
    }


    public IReadOnlyList<GlobalSearchResult> SearchGlobal(string? query, int maxResults = 12)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length < 2)
        {
            return [];
        }

        static int MatchRank(string? value, string queryText)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return int.MaxValue;
            }

            if (value.Equals(queryText, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (value.StartsWith(queryText, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return value.Contains(queryText, StringComparison.OrdinalIgnoreCase) ? 2 : int.MaxValue;
        }

        static string MatchReason(string field, int rank) => rank switch
        {
            0 => $"Exact {field} match",
            1 => $"{field} starts with search",
            _ => $"Matching {field}"
        };

        var results = new List<(int Rank, int KindOrder, string SortKey, GlobalSearchResult Result)>();

        foreach (var artistGroup in _libraryTracks
                     .Where(track => !string.IsNullOrWhiteSpace(track.Artist))
                     .GroupBy(track => track.Artist, StringComparer.OrdinalIgnoreCase))
        {
            var rank = MatchRank(artistGroup.Key, text);
            if (rank == int.MaxValue)
            {
                continue;
            }

            var track = artistGroup.First();
            var subtitle = $"{MatchReason("artist", rank)} • {artistGroup.Count()} tracks";
            results.Add((rank, 0, artistGroup.Key, new GlobalSearchResult("Artist", artistGroup.Key, subtitle, track)));
        }

        foreach (var albumGroup in _libraryTracks
                     .Where(track => !string.IsNullOrWhiteSpace(track.Album))
                     .GroupBy(track => $"{track.Artist}{track.Album}", StringComparer.OrdinalIgnoreCase))
        {
            var track = albumGroup.First();
            var rank = MatchRank(track.Album, text);
            if (rank == int.MaxValue)
            {
                continue;
            }

            var owner = string.IsNullOrWhiteSpace(track.Artist) ? "Unknown artist" : track.Artist;
            var subtitle = $"{MatchReason("album", rank)} • {owner} • {albumGroup.Count()} tracks";
            results.Add((rank, 1, track.Album, new GlobalSearchResult("Album", track.Album, subtitle, track)));
        }

        foreach (var track in _libraryTracks)
        {
            var rank = MatchRank(track.Title, text);
            if (rank == int.MaxValue)
            {
                continue;
            }

            var context = string.Join(" • ", new[] { track.Artist, track.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var subtitle = string.IsNullOrWhiteSpace(context)
                ? MatchReason("track", rank)
                : $"{MatchReason("track", rank)} • {context}";
            results.Add((rank, 2, track.Title, new GlobalSearchResult("Track", track.Title, subtitle, track)));
        }

        foreach (var genreGroup in _libraryTracks
                     .Where(track => !string.IsNullOrWhiteSpace(track.Genre))
                     .GroupBy(track => track.Genre, StringComparer.OrdinalIgnoreCase))
        {
            var rank = MatchRank(genreGroup.Key, text);
            if (rank == int.MaxValue)
            {
                continue;
            }

            var track = genreGroup.First();
            var subtitle = $"{MatchReason("genre", rank)} • {genreGroup.Count()} tracks";
            results.Add((rank, 3, genreGroup.Key, new GlobalSearchResult("Genre", genreGroup.Key, subtitle, track)));
        }

        return results
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.KindOrder)
            .ThenBy(item => item.SortKey, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Result)
            .Take(maxResults)
            .ToArray();
    }

    public bool IsLibraryScanning
    {
        get => _isLibraryScanning;
        private set
        {
            if (SetProperty(ref _isLibraryScanning, value))
            {
                OnPropertyChanged(nameof(IsLibraryIdle));
                OnPropertyChanged(nameof(LibraryScanProgressIsIndeterminate));
            }
        }
    }

    public bool IsLibraryIdle => !IsLibraryScanning;

    public string LibraryScanStatus
    {
        get => _libraryScanStatus;
        private set => SetProperty(ref _libraryScanStatus, value);
    }

    public int LibraryScanFilesDiscovered
    {
        get => _libraryScanFilesDiscovered;
        private set
        {
            if (SetProperty(ref _libraryScanFilesDiscovered, value))
            {
                OnPropertyChanged(nameof(LibraryScanProgressMaximum));
                OnPropertyChanged(nameof(LibraryScanProgressIsIndeterminate));
            }
        }
    }

    public int LibraryScanFilesProcessed
    {
        get => _libraryScanFilesProcessed;
        private set => SetProperty(ref _libraryScanFilesProcessed, value);
    }

    public int LibraryScanTracksIndexed
    {
        get => _libraryScanTracksIndexed;
        private set => SetProperty(ref _libraryScanTracksIndexed, value);
    }

    public int LibraryScanErrors
    {
        get => _libraryScanErrors;
        private set => SetProperty(ref _libraryScanErrors, value);
    }

    public double LibraryScanProgressMaximum => Math.Max(1, LibraryScanFilesDiscovered);
    public bool LibraryScanProgressIsIndeterminate => IsLibraryScanning && LibraryScanFilesDiscovered == 0;
    public string IndexedTrackCountText => _libraryTracks.Count == 1 ? "1 indexed track" : $"{_libraryTracks.Count} indexed tracks";
    public string LibraryFolderCountText => LibraryFolders.Count == 1
        ? "1 top folder"
        : $"{LibraryFolders.Count} top folders";
    public string LibraryLastScanText => _libraryLastScanUtc is null
        ? "Not scanned yet"
        : $"Last scan {_libraryLastScanUtc.Value.ToLocalTime():MMM d, yyyy h:mm tt}";

    public Track? SelectedQueueTrack
    {
        get => _selectedQueueTrack;
        set => SetProperty(ref _selectedQueueTrack, value);
    }

    public string ActiveSection
    {
        get => _activeSection;
        private set
        {
            if (SetProperty(ref _activeSection, value))
            {
                SectionSubtitle = GetSectionSubtitle(value);
                OnPropertyChanged(nameof(IsLibraryActive));
                OnPropertyChanged(nameof(IsPlaylistsActive));
                OnPropertyChanged(nameof(IsQueueActive));
                OnPropertyChanged(nameof(IsSettingsActive));
            }
        }
    }

    public bool IsLibraryActive => ActiveSection == "Library";
    public bool IsPlaylistsActive => ActiveSection == "Playlists";
    public bool IsQueueActive => ActiveSection == "Queue";
    public bool IsSettingsActive => ActiveSection == "Settings";

    public string SectionSubtitle
    {
        get => _sectionSubtitle;
        private set => SetProperty(ref _sectionSubtitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        private set
        {
            if (SetProperty(ref _isShuffleEnabled, value))
            {
                OnPropertyChanged(nameof(ShuffleLabel));
            }
        }
    }

    public string ShuffleLabel => IsShuffleEnabled ? "Shuffle on" : "Shuffle off";

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        private set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                OnPropertyChanged(nameof(RepeatLabel));
            }
        }
    }

    public string RepeatLabel => RepeatMode switch
    {
        RepeatMode.All => "Repeat all",
        RepeatMode.One => "Repeat one",
        _ => "Repeat off"
    };

    public VisualizerPalette VisualizerPalette
    {
        get => _visualizerPalette;
        private set
        {
            if (SetProperty(ref _visualizerPalette, value))
            {
                OnPropertyChanged(nameof(VisualizerLabel));
                RefreshVisualizerBrushes();
            }
        }
    }

    public string VisualizerLabel => $"{VisualizerPalette} palette";

    public double Volume
    {
        get => _volume;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (SetProperty(ref _volume, normalized))
            {
                if (!_isMuted)
                {
                    _playbackService.SetVolume(normalized);
                }

                OnPropertyChanged(nameof(VolumePercentText));
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public string VolumePercentText => $"{Math.Round(Volume * 100):0}%";

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public string VolumeGlyph => IsMuted || Volume <= 0.001 ? "×" : "♪";
    public string LibraryCountText => LibraryRoots.Count == 1 ? "1 saved root" : $"{LibraryRoots.Count} saved roots";
    public string QueueCountText => Queue.Count == 1 ? "1 track" : $"{Queue.Count} tracks";

    public Track? CurrentTrack => _playbackService.Snapshot.CurrentTrack;
    public string NowPlayingTitle => CurrentTrack?.Title ?? "Nothing playing";
    public string NowPlayingArtist => CurrentTrack?.Artist ?? "Open music to begin";
    public string NowPlayingAlbum => CurrentTrack?.Album ?? "No album loaded";
    public string NowPlayingFormat => _playbackService.Snapshot.FormatDescription
                                      ?? CurrentTrack?.FormatDescription
                                      ?? "Audio details appear after opening a track";
    public string NowPlayingMetadata => CurrentTrack?.MetadataDescription ?? "Embedded tags appear here";
    public byte[]? NowPlayingArtwork
    {
        get
        {
            var current = CurrentTrack;
            return current?.ArtworkBytes
                ?? (PathsEqual(current?.FilePath, _onlineArtworkTrackPath) ? _onlineArtworkBytes : null);
        }
    }

    public string NowPlayingArtworkSource
    {
        get
        {
            var current = CurrentTrack;
            if (current?.HasArtwork == true)
            {
                return current.ArtworkSource;
            }

            return PathsEqual(current?.FilePath, _onlineArtworkTrackPath) && _onlineArtworkBytes is { Length: > 0 }
                ? _onlineArtworkSource
                : "Cadence fallback";
        }
    }
    public string PlaybackStatusText => _playbackService.Snapshot.State switch
    {
        PlaybackState.Playing => "NAudio playback active",
        PlaybackState.Paused => "Playback paused",
        PlaybackState.Faulted => "Playback needs attention",
        _ when _playbackService.Snapshot.CurrentTrack is not null => "Track ready",
        _ => "NAudio playback online"
    };

    public bool IsPlaybackPlaying => _playbackService.Snapshot.State == PlaybackState.Playing;
    public string PlayPauseGlyph => IsPlaybackPlaying ? "Pause" : "Play";
    public string PositionText => FormatTime(_playbackService.Snapshot.Position);
    public string DurationText => FormatTime(_playbackService.Snapshot.Duration);
    public double DurationSeconds => Math.Max(0, _playbackService.Snapshot.Duration.TotalSeconds);
    public bool CanSeek => _playbackService.Snapshot.CurrentTrack is not null && DurationSeconds > 0;

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            var normalized = Math.Clamp(value, 0, Math.Max(0, DurationSeconds));
            if (SetProperty(ref _positionSeconds, normalized) && !_isUpdatingPosition && CanSeek)
            {
                _playbackService.Seek(TimeSpan.FromSeconds(normalized));
            }
        }
    }

    public async Task SaveSessionAsync()
    {
        CaptureSessionState();
        await _sessionService.SaveAsync(_settings).ConfigureAwait(false);
    }

    public double? SavedWindowLeft => _settings.WindowLeft;
    public double? SavedWindowTop => _settings.WindowTop;
    public double SavedWindowWidth => _settings.WindowWidth;
    public double SavedWindowHeight => _settings.WindowHeight;
    public bool SavedWindowMaximized => _settings.WindowMaximized;
    public bool HasSavedWindowPlacement => SavedWindowLeft.HasValue && SavedWindowTop.HasValue;

    public void CaptureWindowPlacement(double left, double top, double width, double height, bool maximized)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.WindowMaximized = maximized;
    }

    public async Task InitializeSessionPlaybackAsync()
    {
        if (string.IsNullOrWhiteSpace(_sessionTrackPath))
        {
            return;
        }

        if (!File.Exists(_sessionTrackPath))
        {
            _logger.Warning($"The saved session track is missing and was skipped: {_sessionTrackPath}");
            StatusText = $"Session restored • Saved track is missing{MissingQueueStatusSuffix()}";
            return;
        }

        var track = FindQueueTrack(_sessionTrackPath);
        if (track is null)
        {
            track = ReadTrackMetadata(_sessionTrackPath);
            Queue.Add(track);
            OnQueueChanged();
        }

        try
        {
            await _playbackService.OpenAsync(track).ConfigureAwait(false);
            if (_playbackService.Snapshot.State == PlaybackState.Faulted)
            {
                RunOnUiThread(() => StatusText = $"Session restored • Could not reopen {track.Title}");
                return;
            }

            var durationSeconds = Math.Max(0, _playbackService.Snapshot.Duration.TotalSeconds);
            var restoredSeconds = Math.Clamp(_sessionPositionSeconds, 0, durationSeconds);
            _playbackService.PreparePaused(TimeSpan.FromSeconds(restoredSeconds));

            RunOnUiThread(() =>
            {
                SelectedQueueTrack = FindQueueTrack(track.FilePath) ?? track;
                StatusText = $"Session restored • {track.Title} paused at {FormatTime(TimeSpan.FromSeconds(restoredSeconds))}{MissingQueueStatusSuffix()}";
            });
        }
        catch (Exception exception)
        {
            _logger.Error($"Session playback restore failed: {track.FilePath}", exception);
            RunOnUiThread(() => StatusText = $"Session restored • Could not reopen {track.Title}{MissingQueueStatusSuffix()}");
        }
    }

    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        CancelLibraryScan();

        // Capture the current position before disposing the playback graph. Audio
        // is then stopped immediately so a slow session write can never leave
        // Cadence Studio playing after the window has been asked to close.
        CaptureSessionState();
        Dispose();

        try
        {
            _sessionService.SaveAsync(_settings).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error("Shutdown persistence failed.", exception);
        }
    }

    private void CaptureSessionState()
    {
        _settings.Volume = Volume;
        _settings.IsMuted = IsMuted;
        _settings.ShuffleEnabled = IsShuffleEnabled;
        _settings.RepeatMode = RepeatMode;
        _settings.VisualizerPalette = VisualizerPalette;
        _settings.Theme = CurrentTheme;
        _settings.Typography = CurrentTypography;
        _settings.TextSize = CurrentTextSize;
        _settings.ReduceMotion = ReduceMotion;
        _settings.IsNowPlayingExpanded = IsNowPlayingExpanded;
        _settings.Visualizer = BuildVisualizerSettings();
        _settings.Equalizer = BuildEqualizerSettings();
        _settings.SelectedSection = ActiveSection;
        _settings.SelectedNowPlayingTab = SelectedNowPlayingTab;
        _settings.SelectedPlaylistId = _playlistsInitialized ? SelectedPlaylist?.Id : _settings.SelectedPlaylistId;
        _settings.LibraryRoots = LibraryRoots.ToList();
        _settings.QueuePaths = Queue.Select(track => track.FilePath).ToList();
        _settings.CurrentTrackPath = _playbackService.Snapshot.CurrentTrack?.FilePath;
        _settings.SelectedQueueTrackPath = SelectedQueueTrack?.FilePath;
        _settings.PositionSeconds = _playbackService.Snapshot.Position.TotalSeconds;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackService.SnapshotChanged -= OnPlaybackSnapshotChanged;
        _playbackService.PlaybackEnded -= OnPlaybackEnded;
        _playbackService.VisualizerFrameReady -= OnVisualizerFrameReady;
        DisposeEnrichment();
        DisposeQueueArtwork();
        _playbackService.Dispose();
    }

    public async Task InitializeLibraryAsync()
    {
        try
        {
            LibraryScanStatus = "Loading saved library index…";
            var startupTimer = Stopwatch.StartNew();
            var snapshot = await _libraryService.LoadAsync();
            var indexLoadMilliseconds = startupTimer.ElapsedMilliseconds;

            // Build the 40k-track folder hierarchy away from the WPF dispatcher.
            // The completed plain view-model tree is attached in one short UI update.
            var roots = LibraryRoots.ToArray();
            var searchText = LibrarySearchText;
            startupTimer.Restart();
            var preparedFolders = await Task.Run(() =>
                BuildLibraryBrowserNodes(snapshot.Tracks, roots, searchText));
            var treeBuildMilliseconds = startupTimer.ElapsedMilliseconds;

            RunOnUiThread(() =>
            {
                ApplyLibrarySnapshot(snapshot, preparedFolders);
                LibraryScanStatus = snapshot.Tracks.Count == 0
                    ? "No indexed tracks yet"
                    : $"Loaded {IndexedTrackCountText}";
                StatusText = snapshot.Tracks.Count == 0
                    ? "Library ready • Scan a saved root to begin"
                    : $"Library ready • {IndexedTrackCountText}";
            });

            _logger.Info(
                $"Library startup ready. Tracks={snapshot.Tracks.Count}, IndexLoadMs={indexLoadMilliseconds}, TreeBuildMs={treeBuildMilliseconds}.");

            if (snapshot.Tracks.Count == 0 && LibraryRoots.Count > 0)
            {
                RunOnUiThread(() => _ = ScanLibraryAsync());
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Library index initialization failed.", exception);
            RunOnUiThread(() =>
            {
                LibraryScanStatus = "Library index could not be loaded";
                StatusText = "Library index could not be loaded • Rescan to rebuild it";
            });
        }
    }

    private async Task ScanLibraryAsync(bool allowEmptyRoots = false)
    {
        if (IsLibraryScanning)
        {
            StatusText = "A library scan is already running.";
            return;
        }

        if (LibraryRoots.Count == 0 && !allowEmptyRoots)
        {
            LibraryScanStatus = "Add a library root before scanning";
            StatusText = "Add a music folder before scanning the library.";
            return;
        }

        _libraryScanCancellation?.Dispose();
        _libraryScanCancellation = new CancellationTokenSource();
        var cancellationToken = _libraryScanCancellation.Token;

        IsLibraryScanning = true;
        LibraryScanFilesDiscovered = 0;
        LibraryScanFilesProcessed = 0;
        LibraryScanTracksIndexed = 0;
        LibraryScanErrors = 0;
        LibraryScanStatus = "Discovering music files…";
        StatusText = "Library scan running in the background • Playback remains available";

        var progress = new Progress<LibraryScanProgress>(ApplyLibraryScanProgress);

        try
        {
            var result = await _libraryService
                .ScanAsync(LibraryRoots.ToArray(), progress, cancellationToken);

            ApplyLibrarySnapshot(result.Snapshot);
            LibraryScanStatus = result.Failed == 0
                ? $"Scan complete • {result.FilesDiscovered} files"
                : $"Scan complete • {result.Failed} items need attention";

            StatusText = $"Library updated • +{result.Added} new • {result.Updated} changed • {result.Removed} removed" +
                         (result.Failed > 0 ? $" • {result.Failed} skipped" : string.Empty);
        }
        catch (OperationCanceledException)
        {
            LibraryScanStatus = "Scan cancelled";
            StatusText = "Library scan cancelled • Existing index preserved";
        }
        catch (Exception exception)
        {
            LibraryScanStatus = "Scan failed • Existing index preserved";
            StatusText = $"Library scan failed: {exception.Message}";
            _logger.Error("Library scan failed.", exception);
        }
        finally
        {
            IsLibraryScanning = false;
            _libraryScanCancellation?.Dispose();
            _libraryScanCancellation = null;
        }
    }

    private void CancelLibraryScan()
    {
        if (_libraryScanCancellation is null)
        {
            return;
        }

        LibraryScanStatus = "Cancelling scan…";
        _libraryScanCancellation.Cancel();
    }

    private void ApplyLibraryScanProgress(LibraryScanProgress progress)
    {
        LibraryScanFilesDiscovered = progress.FilesDiscovered;
        LibraryScanFilesProcessed = progress.FilesProcessed;
        LibraryScanTracksIndexed = progress.TracksIndexed;
        LibraryScanErrors = progress.Errors;
        LibraryScanStatus = progress.CurrentPath is null
            ? progress.Stage
            : $"{progress.Stage} • {Path.GetFileName(progress.CurrentPath)}";
    }

    private void ApplyLibrarySnapshot(
        LibrarySnapshot snapshot,
        IReadOnlyList<LibraryFolderNode>? preparedFolders = null)
    {
        _libraryTracks.Clear();
        _libraryTracks.AddRange(snapshot.Tracks);
        _libraryLastScanUtc = snapshot.LastScanUtc;

        if (preparedFolders is null)
        {
            RebuildLibraryBrowser();
        }
        else
        {
            ReplaceLibraryFolders(preparedFolders);
        }

        if (SelectedPlaylist is not null)
        {
            RebuildSelectedPlaylistTracks();
        }
        OnPropertyChanged(nameof(IndexedTrackCountText));
        OnPropertyChanged(nameof(LibraryLastScanText));
    }

    private void RebuildLibraryBrowser()
    {
        var nodes = BuildLibraryBrowserNodes(
            _libraryTracks,
            LibraryRoots.ToArray(),
            LibrarySearchText);
        ReplaceLibraryFolders(nodes);
    }

    private void ReplaceLibraryFolders(IReadOnlyList<LibraryFolderNode> nodes)
    {
        LibraryFolders.Clear();
        foreach (var node in nodes)
        {
            LibraryFolders.Add(node);
        }

        OnPropertyChanged(nameof(LibraryFolderCountText));
    }

    private static IReadOnlyList<LibraryFolderNode> BuildLibraryBrowserNodes(
        IEnumerable<Track> tracks,
        IEnumerable<string> libraryRoots,
        string? searchText)
    {
        var query = searchText?.Trim() ?? string.Empty;
        var trackSource = query.Length == 0
            ? tracks
            : tracks.Where(track => LibraryTrackMatches(track, query));

        // The persisted snapshot is already file-path sorted. Search results are
        // re-sorted because filtering can be called from an arbitrary source.
        var filteredTracks = query.Length == 0
            ? trackSource.ToArray()
            : trackSource.OrderBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase).ToArray();

        var roots = libraryRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(NormalizeDirectoryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(root => root.Length)
            .ToArray();

        var topLevel = new Dictionary<string, FolderBuilder>(StringComparer.OrdinalIgnoreCase);
        var showRootContainers = roots.Length > 1;

        foreach (var track in filteredTracks)
        {
            var root = roots.FirstOrDefault(candidate => IsPathInsideRoot(track.FilePath, candidate));
            if (root is null)
            {
                AddUnrootedTrack(topLevel, track);
                continue;
            }

            var relativePath = Path.GetRelativePath(root, track.FilePath);
            var relativeDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;
            var segments = relativeDirectory
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            FolderBuilder current;
            var segmentIndex = 0;

            if (showRootContainers)
            {
                var rootName = GetFolderDisplayName(root);
                current = GetOrAddFolder(topLevel, rootName, root);
            }
            else if (segments.Length > 0)
            {
                var firstPath = Path.Combine(root, segments[0]);
                current = GetOrAddFolder(topLevel, segments[0], firstPath);
                segmentIndex = 1;
            }
            else
            {
                var rootName = GetFolderDisplayName(root);
                current = GetOrAddFolder(topLevel, rootName, root);
            }

            for (; segmentIndex < segments.Length; segmentIndex++)
            {
                current = current.GetOrAddFolder(segments[segmentIndex]);
            }

            current.Tracks.Add(track);
        }

        return topLevel.Values
            .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(builder => builder.ToNode())
            .ToArray();
    }

    private static FolderBuilder GetOrAddFolder(
        IDictionary<string, FolderBuilder> folders,
        string name,
        string fullPath)
    {
        if (!folders.TryGetValue(fullPath, out var folder))
        {
            folder = new FolderBuilder(name, fullPath);
            folders[fullPath] = folder;
        }

        return folder;
    }

    private static void AddUnrootedTrack(IDictionary<string, FolderBuilder> folders, Track track)
    {
        var directory = Path.GetDirectoryName(track.FilePath) ?? "Unrooted files";
        var folder = GetOrAddFolder(folders, GetFolderDisplayName(directory), directory);
        folder.Tracks.Add(track);
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsPathInsideRoot(string filePath, string root)
    {
        var normalizedFile = Path.GetFullPath(filePath);
        if (string.Equals(normalizedFile, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return normalizedFile.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFolderDisplayName(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private sealed class FolderBuilder
    {
        private readonly Dictionary<string, FolderBuilder> _folders = new(StringComparer.OrdinalIgnoreCase);

        public FolderBuilder(string name, string fullPath)
        {
            Name = name;
            FullPath = fullPath;
        }

        public string Name { get; }
        public string FullPath { get; }
        public List<Track> Tracks { get; } = [];

        public FolderBuilder GetOrAddFolder(string name)
        {
            var childPath = Path.Combine(FullPath, name);
            if (!_folders.TryGetValue(childPath, out var child))
            {
                child = new FolderBuilder(name, childPath);
                _folders[childPath] = child;
            }

            return child;
        }

        public LibraryFolderNode ToNode()
        {
            var childNodes = _folders.Values
                .OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(folder => folder.ToNode())
                .ToArray();

            var orderedTracks = Tracks
                .OrderBy(track => track.DiscNumber ?? 0)
                .ThenBy(track => track.TrackNumber ?? int.MaxValue)
                .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var node = new LibraryFolderNode
            {
                Name = Name,
                FullPath = FullPath,
                FolderCount = childNodes.Length,
                TrackCount = orderedTracks.Length + childNodes.Sum(child => child.TrackCount)
            };

            foreach (var child in childNodes)
            {
                node.Children.Add(child);
            }

            foreach (var track in orderedTracks)
            {
                node.Children.Add(track);
            }

            return node;
        }
    }

    private async Task AddLibraryTracksAsync(IEnumerable<Track> tracks, bool playFirst, bool replaceQueue = false)
    {
        var indexedTracks = tracks
            .Where(track => track is not null && File.Exists(track.FilePath))
            .GroupBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (indexedTracks.Length == 0)
        {
            StatusText = "No available library tracks were selected.";
            return;
        }

        StatusText = indexedTracks.Length == 1
            ? $"Loading {indexedTracks[0].Title}…"
            : $"Loading {indexedTracks.Length} library tracks…";

        var hydratedTracks = new List<Track>(indexedTracks.Length);
        foreach (var indexedTrack in indexedTracks)
        {
            try
            {
                hydratedTracks.Add(await _metadataService.ReadTrackAsync(indexedTrack.FilePath));
            }
            catch (Exception exception)
            {
                _logger.Error($"Could not hydrate indexed track: {indexedTrack.FilePath}", exception);
                hydratedTracks.Add(indexedTrack);
            }
        }

        if (replaceQueue)
        {
            _playbackService.Stop();
            Queue.Clear();
            SelectedQueueTrack = null;
        }

        Track? first = null;
        var added = 0;
        foreach (var track in hydratedTracks)
        {
            var existing = FindQueueTrack(track.FilePath);
            if (existing is not null)
            {
                first ??= existing;
                continue;
            }

            Queue.Add(track);
            first ??= track;
            added++;
        }

        OnQueueChanged();
        SelectedQueueTrack = first ?? SelectedQueueTrack;

        if (playFirst && first is not null)
        {
            PlayTrack(first);
            return;
        }

        StatusText = added == 0
            ? "Those library tracks are already in the queue."
            : $"Added {added} {(added == 1 ? "track" : "tracks")} from the library.";
    }

    private static bool LibraryTrackMatches(Track track, string query) =>
        ContainsIgnoreCase(track.Title, query) ||
        ContainsIgnoreCase(track.Artist, query) ||
        ContainsIgnoreCase(track.AlbumArtist, query) ||
        ContainsIgnoreCase(track.Album, query) ||
        ContainsIgnoreCase(track.Genre, query) ||
        ContainsIgnoreCase(track.FilePath, query);

    private static bool ContainsIgnoreCase(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private void Navigate(string? section)
    {
        ActiveSection = NormalizeSection(section);
        StatusText = $"{ActiveSection} workspace selected";
    }

    private void AddLibraryRoot()
    {
        var folder = _folderPicker.PickFolder("Add a music library root");
        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusText = "No library folder was added.";
            return;
        }

        if (LibraryRoots.Any(existing => string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedLibraryRoot = LibraryRoots.First(existing =>
                string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase));
            StatusText = "That library root is already saved.";
            return;
        }

        LibraryRoots.Add(folder);
        SelectedLibraryRoot = folder;
        OnPropertyChanged(nameof(LibraryCountText));
        StatusText = "Library root saved • Starting background scan";
        _logger.Info($"Library root added: {folder}");
        _ = ScanLibraryAsync();
    }

    private void RemoveLibraryRoot()
    {
        if (SelectedLibraryRoot is null)
        {
            StatusText = "Select a saved library root before removing it.";
            return;
        }

        var removed = SelectedLibraryRoot;
        LibraryRoots.Remove(removed);
        SelectedLibraryRoot = null;
        OnPropertyChanged(nameof(LibraryCountText));
        StatusText = "Library root removed • Refreshing index";
        _logger.Info($"Library root removed: {removed}");
        _ = ScanLibraryAsync(allowEmptyRoots: true);
    }

    private void OpenAudioFiles()
    {
        var files = _filePicker.PickAudioFiles();
        if (files.Count == 0)
        {
            StatusText = "No music files were selected.";
            return;
        }

        Track? firstSelected = null;
        var addedCount = 0;

        foreach (var file in files.Where(File.Exists))
        {
            var existing = FindQueueTrack(file);
            if (existing is not null)
            {
                firstSelected ??= existing;
                continue;
            }

            var track = ReadTrackMetadata(file);
            Queue.Add(track);
            firstSelected ??= track;
            addedCount++;
        }

        OnQueueChanged();

        if (firstSelected is null)
        {
            StatusText = "The selected files could not be added.";
            return;
        }

        SelectedQueueTrack = firstSelected;
        PlayTrack(firstSelected);

        if (addedCount > 1)
        {
            StatusText = $"Added {addedCount} tracks • Playing {firstSelected.Title}";
        }
    }

    private void TogglePlayback()
    {
        var snapshot = _playbackService.Snapshot;

        if (snapshot.CurrentTrack is null)
        {
            var track = SelectedQueueTrack ?? Queue.FirstOrDefault();
            if (track is null)
            {
                OpenAudioFiles();
                return;
            }

            PlayTrack(track);
            return;
        }

        if (snapshot.State == PlaybackState.Playing)
        {
            _playbackService.Pause();
        }
        else
        {
            _playbackService.Play();
        }
    }

    private void StopPlayback()
    {
        if (_playbackService.Snapshot.CurrentTrack is null)
        {
            StatusText = "Nothing is loaded.";
            return;
        }

        _playbackService.Stop();
        StatusText = "Playback stopped.";
    }

    private void PlaySelectedTrack()
    {
        var track = SelectedQueueTrack ?? Queue.FirstOrDefault();
        if (track is null)
        {
            StatusText = "Select a queue track or open music first.";
            return;
        }

        PlayTrack(track);
    }

    private void PlayTrack(Track? track)
    {
        if (track is null)
        {
            return;
        }

        try
        {
            _playbackService.OpenAsync(track).GetAwaiter().GetResult();
            if (_playbackService.Snapshot.State == PlaybackState.Faulted)
            {
                return;
            }

            SelectedQueueTrack = FindQueueTrack(track.FilePath) ?? track;
            _playbackService.Play();
            StatusText = $"Playing {track.Title}";
        }
        catch (Exception exception)
        {
            StatusText = $"Could not play {track.FileName}: {exception.Message}";
            _logger.Error($"Playback command failed: {track.FilePath}", exception);
        }
    }

    private void RemoveTrack(Track? track)
    {
        if (track is null)
        {
            return;
        }

        var queued = FindQueueTrack(track.FilePath);
        if (queued is null)
        {
            return;
        }

        var isCurrent = PathsEqual(_playbackService.Snapshot.CurrentTrack?.FilePath, queued.FilePath);
        if (isCurrent)
        {
            _playbackService.Stop();
        }

        Queue.Remove(queued);
        if (ReferenceEquals(SelectedQueueTrack, queued) || PathsEqual(SelectedQueueTrack?.FilePath, queued.FilePath))
        {
            SelectedQueueTrack = Queue.FirstOrDefault();
        }

        OnQueueChanged();
        StatusText = $"Removed {queued.Title} from the queue.";
    }

    private void PreviousTrack()
    {
        var snapshot = _playbackService.Snapshot;
        if (snapshot.CurrentTrack is not null && snapshot.Position > TimeSpan.FromSeconds(3))
        {
            _playbackService.Seek(TimeSpan.Zero);
            StatusText = "Restarted current track.";
            return;
        }

        var currentIndex = FindCurrentQueueIndex();
        if (currentIndex > 0)
        {
            PlayTrack(Queue[currentIndex - 1]);
            return;
        }

        if (RepeatMode == RepeatMode.All && Queue.Count > 0)
        {
            PlayTrack(Queue[^1]);
            return;
        }

        if (snapshot.CurrentTrack is not null)
        {
            _playbackService.Seek(TimeSpan.Zero);
        }

        StatusText = "Already at the beginning of the queue.";
    }

    private void NextTrack()
    {
        if (Queue.Count == 0)
        {
            StatusText = "The queue is empty.";
            return;
        }

        var snapshot = _playbackService.Snapshot;
        var currentIndex = FindQueueIndex(snapshot.CurrentTrack?.FilePath);
        if (currentIndex < 0)
        {
            currentIndex = FindQueueIndex(SelectedQueueTrack?.FilePath);
        }

        Track? next;
        if (IsShuffleEnabled && Queue.Count > 1)
        {
            var candidates = Enumerable.Range(0, Queue.Count)
                .Where(index => index != currentIndex)
                .ToArray();
            next = Queue[candidates[_random.Next(candidates.Length)]];
        }
        else
        {
            var nextIndex = currentIndex < 0 ? 0 : currentIndex + 1;
            if (nextIndex >= Queue.Count)
            {
                if (RepeatMode != RepeatMode.All)
                {
                    StatusText = "Already at the end of the queue.";
                    return;
                }

                nextIndex = 0;
            }

            next = Queue[nextIndex];

            // A queue can contain the same file more than once. Manual skip should still
            // visibly advance instead of reopening the exact same path and appearing inert.
            if (snapshot.CurrentTrack is not null && PathsEqual(next.FilePath, snapshot.CurrentTrack.FilePath))
            {
                var scanLimit = RepeatMode == RepeatMode.All ? Queue.Count : Queue.Count - nextIndex;
                for (var offset = 1; offset < scanLimit; offset++)
                {
                    var candidateIndex = nextIndex + offset;
                    if (candidateIndex >= Queue.Count)
                    {
                        candidateIndex %= Queue.Count;
                    }

                    var candidate = Queue[candidateIndex];
                    if (!PathsEqual(candidate.FilePath, snapshot.CurrentTrack.FilePath))
                    {
                        next = candidate;
                        break;
                    }
                }
            }
        }

        _logger.Info($"Manual next requested. CurrentIndex={currentIndex}, Next='{next.FilePath}'.");
        SelectedQueueTrack = next;
        PlayTrack(next);
    }

    private void ToggleShuffle()
    {
        IsShuffleEnabled = !IsShuffleEnabled;
        StatusText = ShuffleLabel;
    }

    private void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
        StatusText = RepeatLabel;
    }

    private void CycleVisualizer()
    {
        VisualizerPalette = VisualizerPalette switch
        {
            VisualizerPalette.Monochrome => VisualizerPalette.Spectrum,
            VisualizerPalette.Spectrum => VisualizerPalette.Indigo,
            _ => VisualizerPalette.Monochrome
        };
        StatusText = VisualizerLabel;
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        _playbackService.SetVolume(IsMuted ? 0 : Volume);
        StatusText = IsMuted ? "Muted" : $"Volume restored to {VolumePercentText}";
    }

    private void ClearQueue()
    {
        if (Queue.Count == 0)
        {
            StatusText = "Queue is already empty.";
            return;
        }

        _playbackService.Stop();
        Queue.Clear();
        SelectedQueueTrack = null;
        OnQueueChanged();
        StatusText = "Queue cleared.";
    }

    private void OnPlaybackSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        RunOnUiThread(() => ApplyPlaybackSnapshot(snapshot));
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        if (snapshot.CurrentTrack is not null)
        {
            var index = FindQueueIndex(snapshot.CurrentTrack.FilePath);
            if (index >= 0 && Queue[index] != snapshot.CurrentTrack)
            {
                var queuedTrack = Queue[index];
                var preserveHydratedArtwork = queuedTrack.HasArtwork && !snapshot.CurrentTrack.HasArtwork;
                if (!preserveHydratedArtwork)
                {
                    Queue[index] = snapshot.CurrentTrack;
                    SelectedQueueTrack = snapshot.CurrentTrack;
                }
            }
        }

        _isUpdatingPosition = true;
        PositionSeconds = snapshot.Position.TotalSeconds;
        _isUpdatingPosition = false;

        OnPropertyChanged(nameof(CurrentTrack));
        OnPropertyChanged(nameof(NowPlayingTitle));
        OnPropertyChanged(nameof(NowPlayingArtist));
        OnPropertyChanged(nameof(NowPlayingAlbum));
        OnPropertyChanged(nameof(NowPlayingFormat));
        OnPropertyChanged(nameof(NowPlayingMetadata));
        OnPropertyChanged(nameof(NowPlayingArtwork));
        OnPropertyChanged(nameof(NowPlayingArtworkSource));
        OnPropertyChanged(nameof(PlaybackStatusText));
        OnPropertyChanged(nameof(IsPlaybackPlaying));
        OnPropertyChanged(nameof(PlayPauseGlyph));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(CanSeek));
        EnsureTrackEnrichment(snapshot.CurrentTrack);

        if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            StatusText = snapshot.ErrorMessage;
        }
        else if (snapshot.State != _lastObservedPlaybackState)
        {
            StatusText = snapshot.State switch
            {
                PlaybackState.Playing => $"Playing {snapshot.CurrentTrack?.Title}",
                PlaybackState.Paused => "Playback paused.",
                PlaybackState.Stopped when snapshot.CurrentTrack is not null => "Track ready.",
                _ => StatusText
            };
        }

        if (snapshot.State != PlaybackState.Playing)
        {
            ResetVisualizerBars();
        }

        _lastObservedPlaybackState = snapshot.State;
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        RunOnUiThread(AdvanceAfterTrackEnd);
    }

    private void AdvanceAfterTrackEnd()
    {
        if (_disposed)
        {
            return;
        }

        var current = _playbackService.Snapshot.CurrentTrack;
        if (current is null)
        {
            return;
        }

        if (RepeatMode == RepeatMode.One)
        {
            PlayTrack(FindQueueTrack(current.FilePath) ?? current);
            return;
        }

        var next = ChooseNextTrack(allowWrap: RepeatMode == RepeatMode.All);
        if (next is null)
        {
            StatusText = "Queue complete.";
            return;
        }

        PlayTrack(next);
    }

    private Track? ChooseNextTrack(bool allowWrap)
    {
        if (Queue.Count == 0)
        {
            return null;
        }

        var currentIndex = FindCurrentQueueIndex();
        if (IsShuffleEnabled && Queue.Count > 1)
        {
            var candidates = Enumerable.Range(0, Queue.Count)
                .Where(index => index != currentIndex)
                .ToArray();
            return Queue[candidates[_random.Next(candidates.Length)]];
        }

        if (currentIndex < 0)
        {
            return SelectedQueueTrack ?? Queue[0];
        }

        if (currentIndex + 1 < Queue.Count)
        {
            return Queue[currentIndex + 1];
        }

        return allowWrap ? Queue[0] : null;
    }

    private int FindCurrentQueueIndex() => FindQueueIndex(_playbackService.Snapshot.CurrentTrack?.FilePath);

    private int FindQueueIndex(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return -1;
        }

        for (var index = 0; index < Queue.Count; index++)
        {
            if (PathsEqual(Queue[index].FilePath, filePath))
            {
                return index;
            }
        }

        return -1;
    }

    private Track? FindQueueTrack(string? filePath)
    {
        var index = FindQueueIndex(filePath);
        return index >= 0 ? Queue[index] : null;
    }


    private Track ReadTrackMetadata(string filePath)
    {
        try
        {
            return _metadataService.ReadTrackAsync(filePath).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error($"Metadata hydration failed; using filename fallback: {filePath}", exception);
            return Track.FromPath(filePath);
        }
    }

    private void RestoreQueue(IEnumerable<string> paths, string? selectedPath)
    {
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                _sessionMissingQueueFiles++;
                _logger.Warning($"A missing queue file was skipped during session restore: {path}");
                continue;
            }

            if (FindQueueIndex(path) < 0)
            {
                Queue.Add(ReadTrackMetadata(path));
            }
        }

        SelectedQueueTrack = FindQueueTrack(selectedPath) ?? Queue.FirstOrDefault();
    }

    private string MissingQueueStatusSuffix() => _sessionMissingQueueFiles <= 0
        ? string.Empty
        : $" • Skipped {_sessionMissingQueueFiles} missing {(_sessionMissingQueueFiles == 1 ? "file" : "files")}";

    private void OnQueueChanged()
    {
        OnPropertyChanged(nameof(QueueCountText));
        OnPropertyChanged(nameof(QueueArtworkMissingCountText));
        RefreshQueueView();

        if (Queue.Count == 0)
        {
            _queueArtworkCancellation?.Cancel();
            QueueArtworkStatus = "Queue is empty";
            return;
        }

        if (IsQueueArtworkLoading)
        {
            _queueArtworkPendingPass = true;
            return;
        }

        ScheduleQueueArtworkEnrichment();
    }

    private void ReportFutureAction(string action, string version)
    {
        StatusText = $"{action} arrives in {version}.";
    }

    private void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSection(string? section) => section switch
    {
        "Playlists" => "Playlists",
        "Queue" => "Queue",
        "Settings" => "Settings",
        _ => "Library"
    };

    private static string GetSectionSubtitle(string section) => section switch
    {
        "Playlists" => "Create, import, organize, and export reusable listening paths",
        "Queue" => "Search, reorder, remove, save, and control the active playback path",
        "Settings" => "Playback, appearance, library, and restore preferences",
        _ => "Browse the indexed library by its real folder hierarchy"
    };

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}

public sealed record GlobalSearchResult(string Kind, string Title, string Subtitle, Track Track);
