using System.IO;
using CadenceStudio.App.Mvvm;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CancellationTokenSource? _queueArtworkCancellation;
    private CancellationTokenSource? _queueArtworkDebounceCancellation;
    private bool _queueArtworkAutoSuppressed;
    private bool _queueArtworkPendingPass;
    private bool _isQueueArtworkLoading;
    private int _queueArtworkTracksProcessed;
    private int _queueArtworkTracksTotal;
    private int _queueArtworkTracksUpdated;
    private int _queueArtworkTracksUnmatched;
    private string _queueArtworkStatus = "Missing queue artwork is fetched automatically";

    public RelayCommand FetchQueueArtworkCommand { get; private set; } = null!;
    public RelayCommand CancelQueueArtworkCommand { get; private set; } = null!;

    public bool IsQueueArtworkLoading
    {
        get => _isQueueArtworkLoading;
        private set => SetProperty(ref _isQueueArtworkLoading, value);
    }

    public int QueueArtworkTracksProcessed
    {
        get => _queueArtworkTracksProcessed;
        private set
        {
            if (SetProperty(ref _queueArtworkTracksProcessed, value))
            {
                OnPropertyChanged(nameof(QueueArtworkProgressText));
            }
        }
    }

    public int QueueArtworkTracksTotal
    {
        get => _queueArtworkTracksTotal;
        private set
        {
            if (SetProperty(ref _queueArtworkTracksTotal, value))
            {
                OnPropertyChanged(nameof(QueueArtworkProgressMaximum));
                OnPropertyChanged(nameof(QueueArtworkProgressText));
            }
        }
    }

    public int QueueArtworkTracksUpdated
    {
        get => _queueArtworkTracksUpdated;
        private set
        {
            if (SetProperty(ref _queueArtworkTracksUpdated, value))
            {
                OnPropertyChanged(nameof(QueueArtworkProgressText));
            }
        }
    }

    public string QueueArtworkStatus
    {
        get => _queueArtworkStatus;
        private set
        {
            if (SetProperty(ref _queueArtworkStatus, value))
            {
                OnPropertyChanged(nameof(QueueArtworkProgressText));
            }
        }
    }

    public double QueueArtworkProgressMaximum => Math.Max(1, QueueArtworkTracksTotal);

    public string QueueArtworkProgressText => QueueArtworkTracksTotal <= 0
        ? QueueArtworkStatus
        : $"Artwork {QueueArtworkTracksProcessed}/{QueueArtworkTracksTotal} • {QueueArtworkTracksUpdated} added";

    public string QueueArtworkMissingCountText
    {
        get
        {
            var count = Queue.Count(track => !track.HasArtwork);
            return count == 1 ? "1 track needs artwork" : $"{count} tracks need artwork";
        }
    }

    private void InitializeQueueArtworkCommands()
    {
        FetchQueueArtworkCommand = new RelayCommand(() =>
        {
            _queueArtworkAutoSuppressed = false;
            _ = EnrichQueueArtworkAsync(automatic: false);
        });
        CancelQueueArtworkCommand = new RelayCommand(CancelQueueArtwork);
        ScheduleQueueArtworkEnrichment();
    }

    private void ScheduleQueueArtworkEnrichment()
    {
        if (_disposed || _queueArtworkAutoSuppressed || IsQueueArtworkLoading || Queue.All(track => track.HasArtwork))
        {
            return;
        }

        _queueArtworkDebounceCancellation?.Cancel();
        _queueArtworkDebounceCancellation?.Dispose();
        _queueArtworkDebounceCancellation = new CancellationTokenSource();
        var token = _queueArtworkDebounceCancellation.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1200), token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    RunOnUiThread(() => _ = EnrichQueueArtworkAsync(automatic: true));
                }
            }
            catch (OperationCanceledException)
            {
                // A later queue change replaced this debounce request.
            }
        });
    }

    private async Task EnrichQueueArtworkAsync(bool automatic)
    {
        if (_disposed || IsQueueArtworkLoading)
        {
            return;
        }

        var reusedArtwork = ReuseExistingQueueArtwork();
        var candidates = Queue
            .Where(track => !track.HasArtwork && File.Exists(track.FilePath))
            .GroupBy(track => track.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (candidates.Length == 0)
        {
            QueueArtworkTracksTotal = 0;
            QueueArtworkTracksProcessed = 0;
            QueueArtworkTracksUpdated = reusedArtwork;
            QueueArtworkStatus = reusedArtwork > 0
                ? $"Queue artwork complete • {reusedArtwork} reused from matching queued albums"
                : "Every queued track already has artwork";
            OnPropertyChanged(nameof(QueueArtworkMissingCountText));
            return;
        }

        _queueArtworkCancellation?.Cancel();
        _queueArtworkCancellation?.Dispose();
        _queueArtworkCancellation = new CancellationTokenSource();
        var token = _queueArtworkCancellation.Token;

        IsQueueArtworkLoading = true;
        QueueArtworkTracksProcessed = 0;
        QueueArtworkTracksTotal = candidates.Length;
        QueueArtworkTracksUpdated = reusedArtwork;
        _queueArtworkTracksUnmatched = 0;
        QueueArtworkStatus = automatic
            ? "Fetching missing queue artwork in the background…"
            : "Fetching missing queue artwork…";
        _logger.Info($"Queue artwork enrichment started. MissingTracks={candidates.Length}, ReusedLocal={reusedArtwork}, Automatic={automatic}.");

        var groups = candidates
            .GroupBy(GetQueueArtworkGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToArray())
            .ToArray();

        try
        {
            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();
                var representative = group
                    .OrderByDescending(GetArtworkRepresentativeScore)
                    .First();

                RunOnUiThread(() => QueueArtworkStatus = $"Looking up {GetArtworkLookupLabel(representative)}…");
                var enrichment = await _enrichmentService
                    .EnrichArtworkAsync(representative, forceRefresh: !automatic, cancellationToken: token)
                    .ConfigureAwait(false);

                var paths = group.Select(track => track.FilePath).ToArray();
                if (enrichment.HasCoverArt)
                {
                    RunOnUiThread(() =>
                    {
                        var changed = ApplyArtworkToQueuedTracks(paths, enrichment);
                        QueueArtworkTracksUpdated += changed;
                    });
                }
                else
                {
                    _queueArtworkTracksUnmatched += group.Length;
                }

                RunOnUiThread(() => QueueArtworkTracksProcessed += group.Length);
            }

            RunOnUiThread(() =>
            {
                QueueArtworkStatus = QueueArtworkTracksUpdated > 0
                    ? $"Queue artwork complete • {QueueArtworkTracksUpdated} added" +
                      (_queueArtworkTracksUnmatched > 0 ? $" • {_queueArtworkTracksUnmatched} unmatched" : string.Empty)
                    : $"No high-confidence artwork matches found • {_queueArtworkTracksUnmatched} unmatched";
                StatusText = QueueArtworkStatus;
                _logger.Info($"Queue artwork enrichment completed. Added={QueueArtworkTracksUpdated}, Unmatched={_queueArtworkTracksUnmatched}.");
            });
        }
        catch (OperationCanceledException)
        {
            RunOnUiThread(() =>
            {
                QueueArtworkStatus = $"Queue artwork canceled • {QueueArtworkTracksUpdated} added";
                StatusText = QueueArtworkStatus;
                _logger.Info($"Queue artwork enrichment canceled. Added={QueueArtworkTracksUpdated}.");
            });
        }
        catch (Exception exception)
        {
            _logger.Error("Queue artwork enrichment failed.", exception);
            RunOnUiThread(() =>
            {
                QueueArtworkStatus = "Queue artwork lookup failed • Playback remains available";
                StatusText = QueueArtworkStatus;
            });
        }
        finally
        {
            RunOnUiThread(() =>
            {
                IsQueueArtworkLoading = false;
                OnPropertyChanged(nameof(QueueArtworkMissingCountText));
                if (_queueArtworkPendingPass && !_queueArtworkAutoSuppressed)
                {
                    _queueArtworkPendingPass = false;
                    ScheduleQueueArtworkEnrichment();
                }
            });
        }
    }

    private int ReuseExistingQueueArtwork()
    {
        var changed = 0;
        var groups = Queue
            .GroupBy(GetQueueArtworkGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.ToArray())
            .ToArray();

        foreach (var group in groups)
        {
            var source = group.FirstOrDefault(track => track.HasArtwork);
            if (source?.ArtworkBytes is not { Length: > 0 })
            {
                continue;
            }

            var missingPaths = group
                .Where(track => !track.HasArtwork)
                .Select(track => track.FilePath)
                .ToArray();
            if (missingPaths.Length == 0)
            {
                continue;
            }

            var enrichment = new TrackEnrichment
            {
                CoverArtBytes = source.ArtworkBytes,
                CoverArtSource = source.ArtworkSource
            };
            changed += ApplyArtworkToQueuedTracks(missingPaths, enrichment);
        }

        return changed;
    }

    private int ApplyArtworkToQueuedTracks(IEnumerable<string> paths, TrackEnrichment enrichment)
    {
        if (!enrichment.HasCoverArt)
        {
            return 0;
        }

        var pathSet = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        for (var index = 0; index < Queue.Count; index++)
        {
            var track = Queue[index];
            if (track.HasArtwork || !pathSet.Contains(track.FilePath))
            {
                continue;
            }

            var hydrated = track with
            {
                ArtworkBytes = enrichment.CoverArtBytes,
                ArtworkSource = enrichment.CoverArtSource
            };
            Queue[index] = hydrated;
            changed++;

            if (PathsEqual(SelectedQueueTrack?.FilePath, hydrated.FilePath))
            {
                SelectedQueueTrack = hydrated;
            }
        }

        var current = CurrentTrack;
        if (current is not null && pathSet.Contains(current.FilePath))
        {
            _onlineArtworkTrackPath = current.FilePath;
            _onlineArtworkBytes = enrichment.CoverArtBytes;
            _onlineArtworkSource = enrichment.CoverArtSource;
            OnPropertyChanged(nameof(NowPlayingArtwork));
            OnPropertyChanged(nameof(NowPlayingArtworkSource));
        }

        RefreshQueueView();
        OnPropertyChanged(nameof(QueueArtworkMissingCountText));
        return changed;
    }

    private void CancelQueueArtwork()
    {
        _queueArtworkAutoSuppressed = true;
        _queueArtworkDebounceCancellation?.Cancel();
        _queueArtworkCancellation?.Cancel();
        QueueArtworkStatus = IsQueueArtworkLoading
            ? "Canceling queue artwork lookup…"
            : "Automatic queue artwork lookup paused";
    }

    private void DisposeQueueArtwork()
    {
        _queueArtworkDebounceCancellation?.Cancel();
        _queueArtworkDebounceCancellation?.Dispose();
        _queueArtworkDebounceCancellation = null;
        _queueArtworkCancellation?.Cancel();
        _queueArtworkCancellation?.Dispose();
        _queueArtworkCancellation = null;
    }

    private string GetQueueArtworkGroupKey(Track track)
    {
        if (!IsUnknownMetadata(track.Album))
        {
            var albumArtist = !string.IsNullOrWhiteSpace(track.AlbumArtist)
                ? track.AlbumArtist
                : track.Artist;
            return $"album|{NormalizeArtworkKey(albumArtist)}|{NormalizeArtworkKey(track.Album)}";
        }

        var directory = Path.GetDirectoryName(track.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            var folderName = new DirectoryInfo(fullDirectory).Name;
            var isLibraryRoot = LibraryRoots.Any(root => PathsEqual(root, fullDirectory));
            if (!isLibraryRoot && !IsGenericArtworkFolder(folderName))
            {
                return $"folder|{fullDirectory}";
            }
        }

        return $"track|{NormalizeArtworkKey(track.Artist)}|{NormalizeArtworkKey(track.Title)}";
    }

    private static bool IsGenericArtworkFolder(string value) =>
        value.Equals("Music", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Singles", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Tracks", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Downloads", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Misc", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Various", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private static int GetArtworkRepresentativeScore(Track track)
    {
        var score = 0;
        if (!IsUnknownMetadata(track.Artist)) score += 4;
        if (!IsUnknownMetadata(track.Title)) score += 4;
        if (!IsUnknownMetadata(track.Album)) score += 3;
        if (track.Year is > 0) score += 1;
        if (track.Duration > TimeSpan.Zero) score += 1;
        return score;
    }

    private static string GetArtworkLookupLabel(Track track)
    {
        if (!IsUnknownMetadata(track.Album))
        {
            return $"{track.Artist} • {track.Album}";
        }

        var folder = Path.GetDirectoryName(track.FilePath);
        return string.IsNullOrWhiteSpace(folder)
            ? $"{track.Artist} • {track.Title}"
            : new DirectoryInfo(folder).Name;
    }

    private static bool IsUnknownMetadata(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("Unknown ", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeArtworkKey(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
