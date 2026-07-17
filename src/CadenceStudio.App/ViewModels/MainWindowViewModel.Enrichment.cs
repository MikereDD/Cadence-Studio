using System.Text;
using CadenceStudio.App.Mvvm;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ITrackEnrichmentService _enrichmentService;
    private CancellationTokenSource? _enrichmentCancellation;
    private string? _enrichmentTrackPath;
    private string _selectedNowPlayingTab = "Details";
    private bool _isEnrichmentLoading;
    private string _enrichmentStatus = "Play a track to load MusicBrainz, cover art, lyrics, and artist information";
    private string _musicBrainzStatus = "Not matched";
    private string _nowPlayingDetailsText = "Audio and metadata details appear here.";
    private string _lyricsText = "No lyrics loaded.";
    private string _lyricsSourceText = "Local lyrics are checked before online lookup.";
    private string _artistBioText = "No artist biography loaded.";
    private string _bioSourceText = "MusicBrainz and Wikipedia attribution appears here.";
    private string? _onlineArtworkTrackPath;
    private byte[]? _onlineArtworkBytes;
    private string _onlineArtworkSource = "Cadence fallback";
    private TrackEnrichment? _lastEnrichment;

    public RelayCommand<string> SelectNowPlayingTabCommand { get; private set; } = null!;
    public RelayCommand RefreshEnrichmentCommand { get; private set; } = null!;

    public string SelectedNowPlayingTab
    {
        get => _selectedNowPlayingTab;
        private set
        {
            if (SetProperty(ref _selectedNowPlayingTab, value))
            {
                OnPropertyChanged(nameof(IsDetailsTabActive));
                OnPropertyChanged(nameof(IsLyricsTabActive));
                OnPropertyChanged(nameof(IsBioTabActive));
            }
        }
    }

    public bool IsDetailsTabActive => SelectedNowPlayingTab == "Details";
    public bool IsLyricsTabActive => SelectedNowPlayingTab == "Lyrics";
    public bool IsBioTabActive => SelectedNowPlayingTab == "Bio";

    public bool IsEnrichmentLoading
    {
        get => _isEnrichmentLoading;
        private set => SetProperty(ref _isEnrichmentLoading, value);
    }

    public string EnrichmentStatus
    {
        get => _enrichmentStatus;
        private set => SetProperty(ref _enrichmentStatus, value);
    }

    public string MusicBrainzStatus
    {
        get => _musicBrainzStatus;
        private set => SetProperty(ref _musicBrainzStatus, value);
    }

    public string NowPlayingDetailsText
    {
        get => _nowPlayingDetailsText;
        private set => SetProperty(ref _nowPlayingDetailsText, value);
    }

    public string LyricsText
    {
        get => _lyricsText;
        private set => SetProperty(ref _lyricsText, value);
    }

    public string LyricsSourceText
    {
        get => _lyricsSourceText;
        private set => SetProperty(ref _lyricsSourceText, value);
    }

    public string ArtistBioText
    {
        get => _artistBioText;
        private set => SetProperty(ref _artistBioText, value);
    }

    public TrackEnrichment? LastEnrichment => _lastEnrichment;

    public Task<TrackEnrichment> GetMetadataWorkshopProposalAsync(
        Track track,
        bool forceRefresh = true,
        CancellationToken cancellationToken = default) =>
        _enrichmentService.EnrichAsync(track, forceRefresh, cancellationToken);

    public Task<TrackEnrichment> SelectMetadataWorkshopReleaseAsync(
        TrackEnrichment enrichment,
        MusicBrainzReleaseCandidate candidate,
        bool forceRefresh = true,
        CancellationToken cancellationToken = default) =>
        _enrichmentService.SelectReleaseAsync(enrichment, candidate, forceRefresh, cancellationToken);

    public string BioSourceText
    {
        get => _bioSourceText;
        private set => SetProperty(ref _bioSourceText, value);
    }

    private void InitializeEnrichmentState(string selectedTab)
    {
        SelectedNowPlayingTab = selectedTab is "Lyrics" or "Bio" ? selectedTab : "Details";
    }

    private void InitializeEnrichmentCommands()
    {
        SelectNowPlayingTabCommand = new RelayCommand<string>(tab =>
        {
            SelectedNowPlayingTab = tab is "Lyrics" or "Bio" ? tab : "Details";
        });
        RefreshEnrichmentCommand = new RelayCommand(() =>
        {
            var track = CurrentTrack;
            if (track is not null)
            {
                _ = LoadTrackEnrichmentAsync(track, forceRefresh: true);
            }
        });
    }

    private void EnsureTrackEnrichment(Track? track)
    {
        if (track is null)
        {
            _enrichmentTrackPath = null;
            _enrichmentCancellation?.Cancel();
            ClearOnlineArtwork();
            ResetEnrichmentDisplay();
            return;
        }

        if (string.Equals(_enrichmentTrackPath, track.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _enrichmentTrackPath = track.FilePath;
        if (!PathsEqual(_onlineArtworkTrackPath, track.FilePath))
        {
            ClearOnlineArtwork();
        }
        _ = LoadTrackEnrichmentAsync(track, forceRefresh: false);
    }

    private async Task LoadTrackEnrichmentAsync(Track track, bool forceRefresh)
    {
        _enrichmentCancellation?.Cancel();
        _enrichmentCancellation?.Dispose();
        _enrichmentCancellation = new CancellationTokenSource();
        var token = _enrichmentCancellation.Token;
        var requestedPath = track.FilePath;

        RunOnUiThread(() =>
        {
            IsEnrichmentLoading = true;
            EnrichmentStatus = forceRefresh
                ? "Refreshing MusicBrainz, cover art, lyrics, and artist information…"
                : "Loading MusicBrainz, cover art, lyrics, and artist information…";
            MusicBrainzStatus = "Looking up MusicBrainz…";
            NowPlayingDetailsText = BuildLocalDetails(track);
            LyricsText = "Looking for embedded, local, and online lyrics…";
            LyricsSourceText = "Lyrics lookup in progress";
            ArtistBioText = "Looking up artist information…";
            BioSourceText = "Artist lookup in progress";
        });

        try
        {
            var enrichment = await _enrichmentService.EnrichAsync(track, forceRefresh, token).ConfigureAwait(false);
            if (token.IsCancellationRequested ||
                !string.Equals(_enrichmentTrackPath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RunOnUiThread(() => ApplyEnrichment(track, enrichment));
        }
        catch (OperationCanceledException)
        {
            // A new track or refresh superseded this request.
        }
        catch (Exception exception)
        {
            _logger.Error($"Track enrichment failed: {track.FilePath}", exception);
            RunOnUiThread(() =>
            {
                IsEnrichmentLoading = false;
                EnrichmentStatus = "Online enrichment failed; local metadata remains available";
                MusicBrainzStatus = "MusicBrainz unavailable";
                NowPlayingDetailsText = BuildLocalDetails(track);
                LyricsText = "Lyrics could not be loaded.";
                LyricsSourceText = "Check the Cadence Studio log for details";
                ArtistBioText = "Artist biography could not be loaded.";
                BioSourceText = "Check the Cadence Studio log for details";
            });
        }
    }

    private void ApplyEnrichment(Track track, TrackEnrichment enrichment)
    {
        _lastEnrichment = enrichment;
        OnPropertyChanged(nameof(LastEnrichment));
        IsEnrichmentLoading = false;
        MusicBrainzStatus = enrichment.MusicBrainzStatus;
        EnrichmentStatus = enrichment.FromCache
            ? "Enrichment loaded from the local cache"
            : "Enrichment lookup complete and cached locally";
        NowPlayingDetailsText = BuildEnrichedDetails(track, enrichment);
        LyricsText = enrichment.HasLyrics ? enrichment.LyricsText : "No lyrics were found for this track.";
        LyricsSourceText = enrichment.LyricsSource;
        ArtistBioText = enrichment.HasBio ? enrichment.ArtistBio : "No artist biography was found.";
        BioSourceText = enrichment.BioSource;
        ApplyOnlineArtwork(track, enrichment);
    }

    private void ResetEnrichmentDisplay()
    {
        _lastEnrichment = null;
        OnPropertyChanged(nameof(LastEnrichment));
        IsEnrichmentLoading = false;
        EnrichmentStatus = "Play a track to load MusicBrainz, cover art, lyrics, and artist information";
        MusicBrainzStatus = "Not matched";
        NowPlayingDetailsText = "Audio and metadata details appear here.";
        LyricsText = "No lyrics loaded.";
        LyricsSourceText = "Local lyrics are checked before online lookup.";
        ArtistBioText = "No artist biography loaded.";
        BioSourceText = "MusicBrainz and Wikipedia attribution appears here.";
    }

    private static string BuildLocalDetails(Track track)
    {
        var builder = new StringBuilder();
        AppendDetail(builder, "Title", track.Title);
        AppendDetail(builder, "Artist", track.Artist);
        AppendDetail(builder, "Album", track.Album);
        AppendDetail(builder, "Local tags", track.MetadataDescription);
        AppendDetail(builder, "Audio", track.FormatDescription);
        AppendDetail(builder, "File", track.FilePath);
        return builder.ToString().TrimEnd();
    }

    private static string BuildEnrichedDetails(Track track, TrackEnrichment enrichment)
    {
        var builder = new StringBuilder(BuildLocalDetails(track));
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("MUSICBRAINZ");
        AppendDetail(builder, "Match", enrichment.MusicBrainzStatus);
        AppendDetail(builder, "Canonical title", enrichment.CanonicalTitle);
        AppendDetail(builder, "Canonical artist", enrichment.CanonicalArtist);
        AppendDetail(builder, "Canonical release", enrichment.CanonicalAlbum);
        AppendDetail(builder, "First release", enrichment.FirstReleaseDate);
        AppendDetail(builder, "Recording MBID", enrichment.MusicBrainzRecordingId);
        AppendDetail(builder, "Artist MBID", enrichment.MusicBrainzArtistId);
        AppendDetail(builder, "Release MBID", enrichment.MusicBrainzReleaseId);
        AppendDetail(builder, "Release group MBID", enrichment.MusicBrainzReleaseGroupId);
        AppendDetail(builder, "Artwork", track.HasArtwork ? track.ArtworkSource : enrichment.CoverArtSource);
        return builder.ToString().TrimEnd();
    }

    private void ApplyOnlineArtwork(Track track, TrackEnrichment enrichment)
    {
        if (track.HasArtwork)
        {
            ClearOnlineArtwork();
            return;
        }

        if (!enrichment.HasCoverArt)
        {
            if (PathsEqual(_onlineArtworkTrackPath, track.FilePath))
            {
                ClearOnlineArtwork();
            }
            return;
        }

        _onlineArtworkTrackPath = track.FilePath;
        _onlineArtworkBytes = enrichment.CoverArtBytes;
        _onlineArtworkSource = enrichment.CoverArtSource;

        var index = FindQueueIndex(track.FilePath);
        if (index >= 0)
        {
            var hydratedTrack = Queue[index] with
            {
                ArtworkBytes = enrichment.CoverArtBytes,
                ArtworkSource = enrichment.CoverArtSource
            };
            Queue[index] = hydratedTrack;
            if (PathsEqual(SelectedQueueTrack?.FilePath, track.FilePath))
            {
                SelectedQueueTrack = hydratedTrack;
            }
            RefreshQueueView();
        }

        OnPropertyChanged(nameof(NowPlayingArtwork));
        OnPropertyChanged(nameof(NowPlayingArtworkSource));
    }

    private void ClearOnlineArtwork()
    {
        if (_onlineArtworkBytes is null && _onlineArtworkTrackPath is null)
        {
            return;
        }

        _onlineArtworkTrackPath = null;
        _onlineArtworkBytes = null;
        _onlineArtworkSource = "Cadence fallback";
        OnPropertyChanged(nameof(NowPlayingArtwork));
        OnPropertyChanged(nameof(NowPlayingArtworkSource));
    }

    private static void AppendDetail(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(label).Append(": ").AppendLine(value.Trim());
        }
    }

    private void DisposeEnrichment()
    {
        _enrichmentCancellation?.Cancel();
        _enrichmentCancellation?.Dispose();
        _enrichmentCancellation = null;
    }
}
