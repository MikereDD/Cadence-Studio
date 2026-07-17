using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface ITrackEnrichmentService
{
    Task<TrackEnrichment> EnrichAsync(
        Track track,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<TrackEnrichment> EnrichArtworkAsync(
        Track track,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    Task<TrackEnrichment> SelectReleaseAsync(
        TrackEnrichment enrichment,
        MusicBrainzReleaseCandidate candidate,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}
