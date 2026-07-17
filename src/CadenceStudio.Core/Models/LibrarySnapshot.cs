namespace CadenceStudio.Core.Models;

public sealed record LibrarySnapshot(
    IReadOnlyList<Track> Tracks,
    DateTimeOffset? LastScanUtc)
{
    public static LibrarySnapshot Empty { get; } = new([], null);
}

public sealed record LibraryScanResult(
    LibrarySnapshot Snapshot,
    int FilesDiscovered,
    int Added,
    int Updated,
    int Removed,
    int Failed);

public sealed record LibraryScanProgress(
    string Stage,
    int FilesDiscovered,
    int FilesProcessed,
    int TracksIndexed,
    int Errors,
    string? CurrentPath);
