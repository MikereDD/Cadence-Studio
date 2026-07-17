using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface IPlaylistService
{
    Task<IReadOnlyList<SavedPlaylist>> LoadSavedAsync(CancellationToken cancellationToken = default);
    Task SaveSavedAsync(IEnumerable<SavedPlaylist> playlists, CancellationToken cancellationToken = default);
    Task<SavedPlaylist> ImportAsync(string playlistPath, CancellationToken cancellationToken = default);
    Task ExportAsync(string playlistPath, SavedPlaylist playlist, CancellationToken cancellationToken = default);
}
