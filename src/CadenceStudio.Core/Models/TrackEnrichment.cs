using System.Text.Json.Serialization;

namespace CadenceStudio.Core.Models;

public sealed class TrackEnrichment
{
    public string TrackPath { get; set; } = string.Empty;
    public DateTimeOffset RetrievedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTime FileLastWriteUtc { get; set; }
    public bool FromCache { get; set; }
    public bool IsArtworkOnly { get; set; }
    public int ArtworkMatcherVersion { get; set; }

    public string MusicBrainzStatus { get; set; } = "Not matched";
    public int? MusicBrainzScore { get; set; }
    public string MusicBrainzRecordingId { get; set; } = string.Empty;
    public string MusicBrainzArtistId { get; set; } = string.Empty;
    public string MusicBrainzReleaseId { get; set; } = string.Empty;
    public string MusicBrainzReleaseGroupId { get; set; } = string.Empty;
    public string CanonicalTitle { get; set; } = string.Empty;
    public string CanonicalArtist { get; set; } = string.Empty;
    public string CanonicalAlbum { get; set; } = string.Empty;
    public string FirstReleaseDate { get; set; } = string.Empty;
    public string SelectedReleaseDate { get; set; } = string.Empty;
    public string SelectedReleaseCountry { get; set; } = string.Empty;
    public string SelectedReleaseStatus { get; set; } = string.Empty;
    public string SelectedReleaseFormat { get; set; } = string.Empty;
    public int? SelectedReleaseTrackCount { get; set; }
    public List<MusicBrainzReleaseCandidate> ReleaseCandidates { get; set; } = [];
    public string ArtistType { get; set; } = string.Empty;
    public string ArtistArea { get; set; } = string.Empty;
    public string ArtistLifeSpan { get; set; } = string.Empty;
    public string ArtistTags { get; set; } = string.Empty;

    public string LyricsText { get; set; } = string.Empty;
    public string LyricsSource { get; set; } = "No lyrics found";
    public bool LyricsAreSynchronized { get; set; }

    public string ArtistBio { get; set; } = string.Empty;
    public string BioSource { get; set; } = "No artist bio found";
    public string BioPageTitle { get; set; } = string.Empty;

    public bool CoverArtLookupCompleted { get; set; }
    public string CoverArtSource { get; set; } = "No online cover art";
    public string CoverArtCacheFileName { get; set; } = string.Empty;

    [JsonIgnore]
    public byte[]? CoverArtBytes { get; set; }

    public bool HasMusicBrainzMatch => !string.IsNullOrWhiteSpace(MusicBrainzRecordingId);
    public bool HasLyrics => !string.IsNullOrWhiteSpace(LyricsText);
    public bool HasBio => !string.IsNullOrWhiteSpace(ArtistBio);
    public bool HasCoverArt => CoverArtBytes is { Length: > 0 };
}
