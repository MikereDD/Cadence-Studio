using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface ILibraryService
{
    Task<LibrarySnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task<LibraryScanResult> ScanAsync(
        IEnumerable<string> roots,
        IProgress<LibraryScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<Track> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);
}
