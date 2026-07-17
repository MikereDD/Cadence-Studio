using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface IMetadataService
{
    Task<Track> ReadTrackAsync(string filePath, CancellationToken cancellationToken = default);
    Task<Stream?> OpenAlbumArtAsync(string filePath, CancellationToken cancellationToken = default);
}
