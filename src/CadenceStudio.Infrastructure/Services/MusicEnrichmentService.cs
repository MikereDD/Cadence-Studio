using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;
using TagFile = TagLib.File;

namespace CadenceStudio.Infrastructure.Services;

public sealed partial class MusicEnrichmentService : ITrackEnrichmentService
{
    private const int QueueArtworkMatcherVersion = 2;
    private const int MaxCoverArtBytes = 12 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan MusicBrainzMinimumInterval = TimeSpan.FromMilliseconds(1100);
    private static readonly SemaphoreSlim MusicBrainzGate = new(1, 1);
    private static DateTimeOffset _lastMusicBrainzRequestUtc = DateTimeOffset.MinValue;

    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public MusicEnrichmentService(AppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CadenceStudio/1.0 (https://github.com/MikereDD/Cadence-Studio)");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<TrackEnrichment> SelectReleaseAsync(
        TrackEnrichment enrichment,
        MusicBrainzReleaseCandidate candidate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enrichment);
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        ApplyReleaseCandidate(enrichment, candidate);
        await EnrichCoverArtAsync(enrichment, forceRefresh, cancellationToken).ConfigureAwait(false);
        return enrichment;
    }

    public async Task<TrackEnrichment> EnrichAsync(
        Track track,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(track.FilePath);
        var fileInfo = new FileInfo(fullPath);
        var fileLastWriteUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;
        var localLyrics = ReadLocalLyrics(fullPath);

        if (!forceRefresh)
        {
            var cached = await LoadCacheAsync(fullPath, fileLastWriteUtc, cancellationToken).ConfigureAwait(false);
            if (cached is not null && !cached.IsArtworkOnly)
            {
                if (localLyrics.Text.Length > 0)
                {
                    cached.LyricsText = localLyrics.Text;
                    cached.LyricsSource = localLyrics.Source;
                    cached.LyricsAreSynchronized = localLyrics.IsSynchronized;
                }

                if (!track.HasArtwork)
                {
                    await HydrateCachedCoverArtAsync(cached, cancellationToken).ConfigureAwait(false);
                    if (!cached.HasCoverArt && !cached.CoverArtLookupCompleted && cached.HasMusicBrainzMatch)
                    {
                        try
                        {
                            await EnrichCoverArtAsync(cached, forceRefresh: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                            await SaveCacheAsync(cached, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            cached.CoverArtSource = "Cover Art Archive unavailable";
                            _logger.Error($"Cover Art Archive lookup failed for {track.FilePath}", exception);
                        }
                    }
                }

                cached.FromCache = true;
                return cached;
            }
        }

        var result = new TrackEnrichment
        {
            IsArtworkOnly = false,
            TrackPath = fullPath,
            RetrievedUtc = DateTimeOffset.UtcNow,
            FileLastWriteUtc = fileLastWriteUtc,
            LyricsText = localLyrics.Text,
            LyricsSource = localLyrics.Source,
            LyricsAreSynchronized = localLyrics.IsSynchronized
        };

        try
        {
            await EnrichFromMusicBrainzAsync(
                track,
                result,
                includeArtistDetails: true,
                minimumScore: 65,
                albumHint: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result.MusicBrainzStatus = "MusicBrainz unavailable";
            _logger.Error($"MusicBrainz enrichment failed for {track.FilePath}", exception);
        }

        if (!track.HasArtwork && result.HasMusicBrainzMatch)
        {
            try
            {
                await EnrichCoverArtAsync(result, forceRefresh, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.CoverArtSource = "Cover Art Archive unavailable";
                _logger.Error($"Cover Art Archive lookup failed for {track.FilePath}", exception);
            }
        }
        else if (track.HasArtwork)
        {
            result.CoverArtLookupCompleted = false;
            result.CoverArtSource = track.ArtworkSource;
        }

        if (!result.HasLyrics)
        {
            try
            {
                await EnrichLyricsFromLrcLibAsync(track, result, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.LyricsSource = "Lyrics lookup unavailable";
                _logger.Error($"LRCLIB lyrics lookup failed for {track.FilePath}", exception);
            }
        }

        try
        {
            await EnrichArtistBioAsync(track, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result.BioSource = "Artist bio lookup unavailable";
            _logger.Error($"Wikipedia artist bio lookup failed for {track.Artist}", exception);
        }

        await SaveCacheAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<TrackEnrichment> EnrichArtworkAsync(
        Track track,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(track.FilePath);
        var fileInfo = new FileInfo(fullPath);
        var fileLastWriteUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue;

        if (track.HasArtwork)
        {
            return new TrackEnrichment
            {
                TrackPath = fullPath,
                RetrievedUtc = DateTimeOffset.UtcNow,
                FileLastWriteUtc = fileLastWriteUtc,
                MusicBrainzStatus = "Local artwork already available",
                CoverArtSource = track.ArtworkSource,
                CoverArtLookupCompleted = false,
                IsArtworkOnly = true
            };
        }

        if (!forceRefresh)
        {
            var cached = await LoadCacheAsync(fullPath, fileLastWriteUtc, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                await HydrateCachedCoverArtAsync(cached, cancellationToken).ConfigureAwait(false);
                if (cached.HasCoverArt ||
                    (cached.CoverArtLookupCompleted && cached.ArtworkMatcherVersion >= QueueArtworkMatcherVersion))
                {
                    cached.FromCache = true;
                    return cached;
                }

                if (cached.HasMusicBrainzMatch)
                {
                    try
                    {
                        await EnrichCoverArtAsync(cached, forceRefresh: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                        await SaveCacheAsync(cached, cancellationToken).ConfigureAwait(false);
                        cached.FromCache = true;
                        return cached;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        cached.CoverArtSource = "Cover Art Archive unavailable";
                        _logger.Error($"Queue artwork lookup failed for {track.FilePath}", exception);
                        return cached;
                    }
                }
            }
        }

        var result = new TrackEnrichment
        {
            TrackPath = fullPath,
            RetrievedUtc = DateTimeOffset.UtcNow,
            FileLastWriteUtc = fileLastWriteUtc,
            IsArtworkOnly = true,
            ArtworkMatcherVersion = QueueArtworkMatcherVersion
        };

        try
        {
            await EnrichFromMusicBrainzAsync(
                track,
                result,
                includeArtistDetails: false,
                minimumScore: 65,
                albumHint: InferAlbumHint(track),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result.MusicBrainzStatus = "MusicBrainz unavailable";
            _logger.Error($"Queue artwork MusicBrainz lookup failed for {track.FilePath}", exception);
        }

        if (result.HasMusicBrainzMatch)
        {
            try
            {
                await EnrichCoverArtAsync(result, forceRefresh, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.CoverArtSource = "Cover Art Archive unavailable";
                _logger.Error($"Queue Cover Art Archive lookup failed for {track.FilePath}", exception);
            }
        }
        else
        {
            result.CoverArtLookupCompleted = true;
            result.CoverArtSource = "No high-confidence MusicBrainz artwork match";
        }

        await SaveCacheAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static string InferAlbumHint(Track track)
    {
        if (!IsUnknown(track.Album))
        {
            return track.Album;
        }

        var directory = Path.GetDirectoryName(track.FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var folder = new DirectoryInfo(directory).Name.Trim();
        return IsGenericMusicFolder(folder) ? string.Empty : folder;
    }

    private static bool IsGenericMusicFolder(string value) =>
        value.Equals("Music", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Singles", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Tracks", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Downloads", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Misc", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Various", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    private async Task EnrichFromMusicBrainzAsync(
        Track track,
        TrackEnrichment result,
        bool includeArtistDetails,
        int minimumScore,
        string? albumHint,
        CancellationToken cancellationToken)
    {
        if (IsUnknown(track.Title) || IsUnknown(track.Artist))
        {
            result.MusicBrainzStatus = "Needs title and artist tags";
            return;
        }

        var terms = new List<string>
        {
            $"recording:\"{EscapeLucene(track.Title)}\"",
            $"artist:\"{EscapeLucene(track.Artist)}\""
        };

        if (!IsUnknown(track.Album))
        {
            terms.Add($"release:\"{EscapeLucene(track.Album)}\"");
        }

        var effectiveAlbum = !IsUnknown(track.Album)
            ? track.Album
            : albumHint ?? string.Empty;

        var query = Uri.EscapeDataString(string.Join(" AND ", terms));
        var searchUrl = $"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=5";
        using var document = await GetMusicBrainzJsonAsync(searchUrl, cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("recordings", out var recordings) ||
            recordings.ValueKind != JsonValueKind.Array ||
            recordings.GetArrayLength() == 0)
        {
            result.MusicBrainzStatus = "No MusicBrainz match";
            return;
        }

        var releaseCandidates = new List<MusicBrainzReleaseCandidate>();
        foreach (var recordingCandidate in recordings.EnumerateArray())
        {
            var recordingScore = GetInt(recordingCandidate, "score");
            if (recordingScore < minimumScore)
            {
                continue;
            }

            var candidateArtistId = string.Empty;
            var candidateArtistName = string.Empty;
            if (recordingCandidate.TryGetProperty("artist-credit", out var candidateArtistCredit) &&
                candidateArtistCredit.ValueKind == JsonValueKind.Array)
            {
                var firstCredit = candidateArtistCredit.EnumerateArray().FirstOrDefault();
                if (firstCredit.ValueKind == JsonValueKind.Object &&
                    firstCredit.TryGetProperty("artist", out var candidateArtist))
                {
                    candidateArtistId = GetString(candidateArtist, "id");
                    candidateArtistName = GetString(candidateArtist, "name");
                }
            }

            if (!recordingCandidate.TryGetProperty("releases", out var candidateReleases) ||
                candidateReleases.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var release in candidateReleases.EnumerateArray())
            {
                var rankingScore = ScoreReleaseCandidate(release, track, effectiveAlbum);
                var status = GetString(release, "status");
                var title = GetString(release, "title");
                var format = GetReleaseFormat(release);

                if (IsClearlyNonAlbumRelease(title, status, format, effectiveAlbum))
                {
                    rankingScore -= 900;
                }

                releaseCandidates.Add(new MusicBrainzReleaseCandidate
                {
                    RecordingId = GetString(recordingCandidate, "id"),
                    RecordingTitle = GetString(recordingCandidate, "title"),
                    ArtistId = candidateArtistId,
                    ArtistName = candidateArtistName,
                    RecordingScore = recordingScore,
                    ReleaseId = GetString(release, "id"),
                    ReleaseGroupId = release.TryGetProperty("release-group", out var releaseGroup)
                        ? GetString(releaseGroup, "id")
                        : string.Empty,
                    Title = title,
                    Date = GetString(release, "date"),
                    Country = GetString(release, "country"),
                    Status = status,
                    Format = format,
                    TrackCount = GetReleaseTrackCount(release),
                    HasFrontCover = HasFrontCover(release),
                    RankingScore = rankingScore + recordingScore
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(effectiveAlbum))
        {
            var bestRecording = recordings.EnumerateArray()
                .OrderByDescending(item => GetInt(item, "score"))
                .FirstOrDefault();
            var bestRecordingId = bestRecording.ValueKind == JsonValueKind.Object ? GetString(bestRecording, "id") : string.Empty;
            var bestRecordingTitle = bestRecording.ValueKind == JsonValueKind.Object ? GetString(bestRecording, "title") : track.Title;
            var bestRecordingScore = bestRecording.ValueKind == JsonValueKind.Object ? GetInt(bestRecording, "score") : minimumScore;
            var bestArtistId = string.Empty;
            var bestArtistName = track.Artist;
            if (bestRecording.ValueKind == JsonValueKind.Object &&
                bestRecording.TryGetProperty("artist-credit", out var bestArtistCredit) &&
                bestArtistCredit.ValueKind == JsonValueKind.Array)
            {
                var credit = bestArtistCredit.EnumerateArray().FirstOrDefault();
                if (credit.ValueKind == JsonValueKind.Object && credit.TryGetProperty("artist", out var artist))
                {
                    bestArtistId = GetString(artist, "id");
                    bestArtistName = GetString(artist, "name");
                }
            }

            try
            {
                var releaseTerms = new[]
                {
                    $"release:\"{EscapeLucene(effectiveAlbum)}\"",
                    $"artist:\"{EscapeLucene(track.Artist)}\""
                };
                var releaseQuery = Uri.EscapeDataString(string.Join(" AND ", releaseTerms));
                var releaseUrl = $"https://musicbrainz.org/ws/2/release/?query={releaseQuery}&fmt=json&limit=25";
                using var releaseDocument = await GetMusicBrainzJsonAsync(releaseUrl, cancellationToken).ConfigureAwait(false);
                if (releaseDocument.RootElement.TryGetProperty("releases", out var albumReleases) &&
                    albumReleases.ValueKind == JsonValueKind.Array)
                {
                    foreach (var release in albumReleases.EnumerateArray())
                    {
                        var title = GetString(release, "title");
                        var status = GetString(release, "status");
                        var format = GetReleaseFormat(release);
                        var rankingScore = ScoreReleaseCandidate(release, track, effectiveAlbum) + bestRecordingScore;
                        if (IsClearlyNonAlbumRelease(title, status, format, effectiveAlbum)) rankingScore -= 900;

                        releaseCandidates.Add(new MusicBrainzReleaseCandidate
                        {
                            RecordingId = bestRecordingId,
                            RecordingTitle = bestRecordingTitle,
                            ArtistId = bestArtistId,
                            ArtistName = bestArtistName,
                            RecordingScore = bestRecordingScore,
                            ReleaseId = GetString(release, "id"),
                            ReleaseGroupId = release.TryGetProperty("release-group", out var releaseGroup)
                                ? GetString(releaseGroup, "id")
                                : string.Empty,
                            Title = title,
                            Date = GetString(release, "date"),
                            Country = GetString(release, "country"),
                            Status = status,
                            Format = format,
                            TrackCount = GetReleaseTrackCount(release),
                            HasFrontCover = HasFrontCover(release),
                            RankingScore = rankingScore
                        });
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Warning($"MusicBrainz release candidate search failed for {track.FilePath}: {exception.Message}");
            }
        }

        result.ReleaseCandidates = releaseCandidates
            .GroupBy(candidate => candidate.ReleaseId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.RankingScore).First())
            .OrderByDescending(candidate => candidate.RankingScore)
            .ThenByDescending(candidate => candidate.HasFrontCover)
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        var selectedCandidate = result.ReleaseCandidates.FirstOrDefault();
        if (selectedCandidate is null)
        {
            result.MusicBrainzStatus = "No suitable MusicBrainz release match";
            return;
        }

        ApplyReleaseCandidate(result, selectedCandidate);
        result.MusicBrainzScore = selectedCandidate.RecordingScore;
        result.MusicBrainzStatus = $"Matched with MusicBrainz • {selectedCandidate.RecordingScore}%";

        var selectedRecording = recordings.EnumerateArray()
            .FirstOrDefault(item => string.Equals(GetString(item, "id"), selectedCandidate.RecordingId, StringComparison.OrdinalIgnoreCase));
        if (selectedRecording.ValueKind == JsonValueKind.Object &&
            selectedRecording.TryGetProperty("first-release-date", out var firstReleaseDate))
        {
            result.FirstReleaseDate = firstReleaseDate.GetString() ?? string.Empty;
        }

        if (!includeArtistDetails || string.IsNullOrWhiteSpace(result.MusicBrainzArtistId))
        {
            return;
        }

        var artistUrl = $"https://musicbrainz.org/ws/2/artist/{result.MusicBrainzArtistId}?inc=url-rels+tags&fmt=json";
        using var artistDocument = await GetMusicBrainzJsonAsync(artistUrl, cancellationToken).ConfigureAwait(false);
        var root = artistDocument.RootElement;
        result.ArtistType = GetString(root, "type");
        if (root.TryGetProperty("area", out var area))
        {
            result.ArtistArea = GetString(area, "name");
        }

        if (root.TryGetProperty("life-span", out var lifeSpan))
        {
            var begin = GetString(lifeSpan, "begin");
            var end = GetString(lifeSpan, "end");
            result.ArtistLifeSpan = begin.Length == 0 && end.Length == 0
                ? string.Empty
                : end.Length == 0 ? $"{begin}–present" : $"{begin}–{end}";
        }

        if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
            result.ArtistTags = string.Join(", ", tags.EnumerateArray()
                .OrderByDescending(tag => GetInt(tag, "count"))
                .Select(tag => GetString(tag, "name"))
                .Where(name => name.Length > 0)
                .Take(6));
        }

        if (root.TryGetProperty("relations", out var relations) && relations.ValueKind == JsonValueKind.Array)
        {
            foreach (var relation in relations.EnumerateArray())
            {
                if (!relation.TryGetProperty("url", out var urlObject))
                {
                    continue;
                }

                var resource = GetString(urlObject, "resource");
                if (resource.Contains("wikipedia.org/wiki/", StringComparison.OrdinalIgnoreCase))
                {
                    result.BioPageTitle = ExtractWikipediaTitle(resource);
                    break;
                }
            }
        }
    }

    private static void ApplyReleaseCandidate(TrackEnrichment result, MusicBrainzReleaseCandidate candidate)
    {
        result.MusicBrainzRecordingId = candidate.RecordingId;
        result.MusicBrainzArtistId = candidate.ArtistId;
        result.MusicBrainzReleaseId = candidate.ReleaseId;
        result.MusicBrainzReleaseGroupId = candidate.ReleaseGroupId;
        result.CanonicalTitle = candidate.RecordingTitle;
        result.CanonicalArtist = candidate.ArtistName;
        result.CanonicalAlbum = candidate.Title;
        result.SelectedReleaseDate = candidate.Date;
        result.SelectedReleaseCountry = candidate.Country;
        result.SelectedReleaseStatus = candidate.Status;
        result.SelectedReleaseFormat = candidate.Format;
        result.SelectedReleaseTrackCount = candidate.TrackCount;
    }

    private static bool IsClearlyNonAlbumRelease(string title, string status, string format, string effectiveAlbum)
    {
        if (string.IsNullOrWhiteSpace(effectiveAlbum)) return false;

        var normalizedTitle = NormalizeReleaseTitle(title);
        var normalizedAlbum = NormalizeReleaseTitle(effectiveAlbum);
        var exactAlbum = normalizedTitle.Equals(normalizedAlbum, StringComparison.OrdinalIgnoreCase);
        if (exactAlbum) return false;

        var lowerTitle = title.ToLowerInvariant();
        var nonAlbumWords = new[] { "demo", "bootleg", "promo", "single", "ep", "live", "remix", "compilation", "sampler" };
        if (nonAlbumWords.Any(word => lowerTitle.Contains(word, StringComparison.Ordinal))) return true;
        if (string.Equals(status, "Bootleg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Promotion", StringComparison.OrdinalIgnoreCase)) return true;
        if (format.Contains("Cassette", StringComparison.OrdinalIgnoreCase) && !effectiveAlbum.Contains("cassette", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int ScoreReleaseCandidate(JsonElement release, Track track, string effectiveAlbum)
    {
        var score = 0;
        var title = GetString(release, "title");
        var status = GetString(release, "status");
        var date = GetString(release, "date");
        var trackCount = GetReleaseTrackCount(release);

        if (!string.IsNullOrWhiteSpace(effectiveAlbum))
        {
            if (string.Equals(title, effectiveAlbum, StringComparison.OrdinalIgnoreCase))
            {
                score += 500;
            }
            else if (NormalizeReleaseTitle(title).Equals(NormalizeReleaseTitle(effectiveAlbum), StringComparison.OrdinalIgnoreCase))
            {
                score += 420;
            }
            else if (title.Contains(effectiveAlbum, StringComparison.OrdinalIgnoreCase) ||
                     effectiveAlbum.Contains(title, StringComparison.OrdinalIgnoreCase))
            {
                score += 140;
            }
            else
            {
                score -= 180;
            }
        }

        if (track.Year is > 0 && date.Length >= 4 && int.TryParse(date[..4], out var releaseYear))
        {
            var yearDelta = Math.Abs(track.Year.Value - releaseYear);
            score += yearDelta switch
            {
                0 => 180,
                1 => 80,
                <= 3 => 20,
                _ => -Math.Min(140, yearDelta * 12)
            };
        }

        if (track.TrackCount is > 0 && trackCount is > 0)
        {
            score += track.TrackCount.Value == trackCount.Value
                ? 180
                : -Math.Min(120, Math.Abs(track.TrackCount.Value - trackCount.Value) * 20);
        }

        if (string.Equals(status, "Official", StringComparison.OrdinalIgnoreCase)) score += 180;
        else if (string.Equals(status, "Bootleg", StringComparison.OrdinalIgnoreCase)) score -= 500;
        else if (string.Equals(status, "Promotion", StringComparison.OrdinalIgnoreCase)) score -= 240;
        if (HasFrontCover(release)) score += 65;
        if (GetReleaseFormat(release).Contains("CD", StringComparison.OrdinalIgnoreCase)) score += 35;
        return score;
    }

    private static string NormalizeReleaseTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private static int? GetReleaseTrackCount(JsonElement release)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var total = 0;
        foreach (var medium in media.EnumerateArray())
        {
            var count = GetInt(medium, "track-count");
            if (count > 0) total += count;
        }
        return total > 0 ? total : null;
    }

    private static string GetReleaseFormat(JsonElement release)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(" + ", media.EnumerateArray()
            .Select(item => GetString(item, "format"))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task EnrichCoverArtAsync(
        TrackEnrichment result,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        result.CoverArtLookupCompleted = false;
        result.CoverArtBytes = null;

        var candidates = new List<(string Kind, string Mbid)>();
        if (Guid.TryParse(result.MusicBrainzReleaseId, out _))
        {
            candidates.Add(("release", result.MusicBrainzReleaseId));
        }
        if (Guid.TryParse(result.MusicBrainzReleaseGroupId, out _))
        {
            candidates.Add(("release-group", result.MusicBrainzReleaseGroupId));
        }

        foreach (var candidate in candidates)
        {
            var cacheFileName = $"{candidate.Kind}-{candidate.Mbid}-500.jpg";
            var cachePath = Path.Combine(_paths.ArtworkCache, cacheFileName);

            if (!forceRefresh && await TryLoadCoverArtFileAsync(cachePath, result, cancellationToken).ConfigureAwait(false))
            {
                result.CoverArtCacheFileName = cacheFileName;
                result.CoverArtSource = candidate.Kind == "release"
                    ? "Cover Art Archive • MusicBrainz release"
                    : "Cover Art Archive • MusicBrainz release group";
                result.CoverArtLookupCompleted = true;
                return;
            }

            var url = candidate.Kind == "release"
                ? $"https://coverartarchive.org/release/{candidate.Mbid}/front-500"
                : $"https://coverartarchive.org/release-group/{candidate.Mbid}/front-500";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning($"Cover Art Archive returned non-image content for {candidate.Kind} {candidate.Mbid}.");
                continue;
            }

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue &&
                (declaredLength.Value <= 0 || declaredLength.Value > MaxCoverArtBytes))
            {
                _logger.Warning($"Cover Art Archive image size was rejected for {candidate.Kind} {candidate.Mbid}.");
                continue;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length <= 0 || bytes.Length > MaxCoverArtBytes)
            {
                _logger.Warning($"Cover Art Archive image data was rejected for {candidate.Kind} {candidate.Mbid}.");
                continue;
            }

            await SaveCoverArtFileAsync(cachePath, bytes, cancellationToken).ConfigureAwait(false);
            result.CoverArtBytes = bytes;
            result.CoverArtCacheFileName = cacheFileName;
            result.CoverArtSource = candidate.Kind == "release"
                ? "Cover Art Archive • MusicBrainz release"
                : "Cover Art Archive • MusicBrainz release group";
            result.CoverArtLookupCompleted = true;
            return;
        }

        result.CoverArtLookupCompleted = true;
        result.CoverArtSource = "No Cover Art Archive image found";
        result.CoverArtCacheFileName = string.Empty;
    }

    private async Task HydrateCachedCoverArtAsync(
        TrackEnrichment result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.CoverArtCacheFileName))
        {
            return;
        }

        var safeName = Path.GetFileName(result.CoverArtCacheFileName);
        if (!string.Equals(safeName, result.CoverArtCacheFileName, StringComparison.Ordinal))
        {
            result.CoverArtCacheFileName = string.Empty;
            return;
        }

        var cachePath = Path.Combine(_paths.ArtworkCache, safeName);
        await TryLoadCoverArtFileAsync(cachePath, result, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> TryLoadCoverArtFileAsync(
        string cachePath,
        TrackEnrichment result,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(cachePath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxCoverArtBytes)
            {
                return false;
            }

            result.CoverArtBytes = await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            return result.HasCoverArt;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SaveCoverArtFileAsync(
        string cachePath,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, cachePath, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning($"Cover art cache could not be saved: {cachePath} • {exception.Message}");
        }
    }

    private async Task EnrichLyricsFromLrcLibAsync(
        Track track,
        TrackEnrichment result,
        CancellationToken cancellationToken)
    {
        if (IsUnknown(track.Title) || IsUnknown(track.Artist))
        {
            result.LyricsSource = "Needs title and artist tags";
            return;
        }

        var query = new Dictionary<string, string>
        {
            ["track_name"] = track.Title,
            ["artist_name"] = track.Artist
        };
        if (!IsUnknown(track.Album))
        {
            query["album_name"] = track.Album;
        }

        var url = "https://lrclib.net/api/get?" + string.Join("&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            result.LyricsSource = "No lyrics found";
            return;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True)
        {
            result.LyricsText = "Instrumental track";
            result.LyricsSource = "LRCLIB • instrumental";
            return;
        }

        var plain = GetString(root, "plainLyrics");
        var synced = GetString(root, "syncedLyrics");
        if (plain.Length > 0)
        {
            result.LyricsText = NormalizeText(plain);
            result.LyricsSource = "LRCLIB • plain lyrics";
            return;
        }

        if (synced.Length > 0)
        {
            result.LyricsText = StripLrcTimestamps(synced);
            result.LyricsSource = "LRCLIB • synchronized lyrics";
            result.LyricsAreSynchronized = true;
            return;
        }

        result.LyricsSource = "No lyrics found";
    }

    private async Task EnrichArtistBioAsync(
        Track track,
        TrackEnrichment result,
        CancellationToken cancellationToken)
    {
        if (IsUnknown(track.Artist))
        {
            result.BioSource = "Needs an artist tag";
            return;
        }

        var pageTitle = result.BioPageTitle;
        if (string.IsNullOrWhiteSpace(pageTitle))
        {
            pageTitle = string.IsNullOrWhiteSpace(result.CanonicalArtist)
                ? track.Artist
                : result.CanonicalArtist;
        }

        var extract = await GetWikipediaExtractAsync(pageTitle, cancellationToken).ConfigureAwait(false);
        if (extract.Text.Length == 0 && !pageTitle.Contains("(band)", StringComparison.OrdinalIgnoreCase))
        {
            extract = await SearchWikipediaExtractAsync($"{pageTitle} musician", cancellationToken).ConfigureAwait(false);
        }

        var musicBrainzFacts = BuildMusicBrainzArtistFacts(result);
        if (extract.Text.Length > 0)
        {
            result.ArtistBio = musicBrainzFacts.Length == 0
                ? extract.Text
                : $"{musicBrainzFacts}\n\n{extract.Text}";
            result.BioSource = $"Wikipedia • {extract.Title} • CC BY-SA";
            result.BioPageTitle = extract.Title;
            return;
        }

        if (musicBrainzFacts.Length > 0)
        {
            result.ArtistBio = musicBrainzFacts;
            result.BioSource = "MusicBrainz artist metadata";
            return;
        }

        result.BioSource = "No artist bio found";
    }

    private async Task<(string Title, string Text)> GetWikipediaExtractAsync(
        string pageTitle,
        CancellationToken cancellationToken)
    {
        var url = "https://en.wikipedia.org/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1" +
                  "&redirects=1&format=json&formatversion=2&titles=" + Uri.EscapeDataString(pageTitle);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (string.Empty, string.Empty);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("pages", out var pages) ||
            pages.ValueKind != JsonValueKind.Array)
        {
            return (string.Empty, string.Empty);
        }

        var page = pages.EnumerateArray().FirstOrDefault();
        if (page.ValueKind != JsonValueKind.Object || page.TryGetProperty("missing", out _))
        {
            return (string.Empty, string.Empty);
        }

        return (GetString(page, "title"), NormalizeText(GetString(page, "extract")));
    }

    private async Task<(string Title, string Text)> SearchWikipediaExtractAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        var url = "https://en.wikipedia.org/w/api.php?action=query&list=search&srlimit=1&format=json&formatversion=2&srsearch=" +
                  Uri.EscapeDataString(searchTerm);
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (string.Empty, string.Empty);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("search", out var search) ||
            search.ValueKind != JsonValueKind.Array)
        {
            return (string.Empty, string.Empty);
        }

        var first = search.EnumerateArray().FirstOrDefault();
        var title = first.ValueKind == JsonValueKind.Object ? GetString(first, "title") : string.Empty;
        return title.Length == 0
            ? (string.Empty, string.Empty)
            : await GetWikipediaExtractAsync(title, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> GetMusicBrainzJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        await MusicBrainzGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastMusicBrainzRequestUtc;
            if (elapsed < MusicBrainzMinimumInterval)
            {
                await Task.Delay(MusicBrainzMinimumInterval - elapsed, cancellationToken).ConfigureAwait(false);
            }

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            _lastMusicBrainzRequestUtc = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            MusicBrainzGate.Release();
        }
    }

    private LocalLyricsResult ReadLocalLyrics(string audioPath)
    {
        try
        {
            using var mediaFile = TagFile.Create(audioPath);
            var embedded = NormalizeText(mediaFile.Tag.Lyrics ?? string.Empty);
            if (embedded.Length > 0)
            {
                return new LocalLyricsResult(embedded, "Embedded lyrics tag", false);
            }
        }
        catch (Exception exception)
        {
            _logger.Warning($"Embedded lyrics could not be read: {audioPath} • {exception.Message}");
        }

        var directory = Path.GetDirectoryName(audioPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return LocalLyricsResult.Empty;
        }

        var stem = Path.GetFileNameWithoutExtension(audioPath);
        var candidates = new[]
        {
            Path.Combine(directory, stem + ".lrc"),
            Path.Combine(directory, stem + ".txt"),
            Path.Combine(directory, "lyrics.lrc"),
            Path.Combine(directory, "lyrics.txt")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var info = new FileInfo(candidate);
                if (!info.Exists || info.Length <= 0 || info.Length > 2 * 1024 * 1024)
                {
                    continue;
                }

                var text = File.ReadAllText(candidate);
                var isSynchronized = candidate.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase);
                return new LocalLyricsResult(
                    isSynchronized ? StripLrcTimestamps(text) : NormalizeText(text),
                    $"Local {Path.GetExtension(candidate).TrimStart('.').ToUpperInvariant()} • {Path.GetFileName(candidate)}",
                    isSynchronized);
            }
            catch (Exception exception)
            {
                _logger.Warning($"Local lyrics file could not be read: {candidate} • {exception.Message}");
            }
        }

        return LocalLyricsResult.Empty;
    }

    private async Task<TrackEnrichment?> LoadCacheAsync(
        string trackPath,
        DateTime fileLastWriteUtc,
        CancellationToken cancellationToken)
    {
        var cacheFile = GetCacheFile(trackPath);
        if (!File.Exists(cacheFile))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(cacheFile);
            var cached = await JsonSerializer.DeserializeAsync<TrackEnrichment>(
                stream,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (cached is null ||
                cached.FileLastWriteUtc != fileLastWriteUtc ||
                DateTimeOffset.UtcNow - cached.RetrievedUtc > CacheLifetime)
            {
                return null;
            }

            return cached;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning($"Enrichment cache could not be read: {cacheFile} • {exception.Message}");
            return null;
        }
    }

    private async Task SaveCacheAsync(TrackEnrichment result, CancellationToken cancellationToken)
    {
        try
        {
            var cacheFile = GetCacheFile(result.TrackPath);
            var tempFile = cacheFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, result, _jsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFile, cacheFile, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Warning($"Enrichment cache could not be saved for {result.TrackPath} • {exception.Message}");
        }
    }

    private string GetCacheFile(string trackPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(trackPath.ToUpperInvariant()));
        return Path.Combine(_paths.EnrichmentCache, Convert.ToHexString(bytes).ToLowerInvariant() + ".json");
    }

    private static string BuildMusicBrainzArtistFacts(TrackEnrichment result)
    {
        var parts = new List<string>();
        if (result.ArtistType.Length > 0)
        {
            parts.Add($"Type: {result.ArtistType}");
        }
        if (result.ArtistArea.Length > 0)
        {
            parts.Add($"Area: {result.ArtistArea}");
        }
        if (result.ArtistLifeSpan.Length > 0)
        {
            parts.Add($"Active: {result.ArtistLifeSpan}");
        }
        if (result.ArtistTags.Length > 0)
        {
            parts.Add($"Tags: {result.ArtistTags}");
        }
        return string.Join(" • ", parts);
    }

    private static string ExtractWikipediaTitle(string resource)
    {
        var marker = "/wiki/";
        var index = resource.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? string.Empty
            : Uri.UnescapeDataString(resource[(index + marker.Length)..]).Replace('_', ' ');
    }

    private static string EscapeLucene(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("Unknown ", StringComparison.OrdinalIgnoreCase);

    private static bool HasFrontCover(JsonElement release)
    {
        if (!release.TryGetProperty("cover-art-archive", out var coverArt) ||
            coverArt.ValueKind != JsonValueKind.Object ||
            !coverArt.TryGetProperty("front", out var front))
        {
            return false;
        }

        return front.ValueKind == JsonValueKind.True;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }
        return property.GetString()?.Trim() ?? string.Empty;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        return 0;
    }

    private static string NormalizeText(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
             .Replace('\r', '\n')
             .Trim();

    private static string StripLrcTimestamps(string value)
    {
        var lines = NormalizeText(value).Split('\n');
        return string.Join("\n", lines
            .Select(line => LrcTimestampRegex().Replace(line, string.Empty).Trim())
            .Where(line => line.Length > 0));
    }

    [GeneratedRegex(@"^(?:\[[0-9]{1,3}:[0-9]{2}(?:\.[0-9]{1,3})?\])+\s*")]
    private static partial Regex LrcTimestampRegex();

    private sealed record LocalLyricsResult(string Text, string Source, bool IsSynchronized)
    {
        public static LocalLyricsResult Empty { get; } = new(string.Empty, "No local lyrics found", false);
    }
}
