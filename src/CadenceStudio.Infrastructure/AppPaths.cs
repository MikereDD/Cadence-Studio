namespace CadenceStudio.Infrastructure;

public sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CadenceStudio");

        Cache = Path.Combine(Root, "cache");
        Logs = Path.Combine(Root, "logs");
        EnrichmentCache = Path.Combine(Cache, "enrichment");
        ArtworkCache = Path.Combine(Cache, "artwork");
        SessionFile = Path.Combine(Root, "session.json");
        LibraryIndexFile = Path.Combine(Cache, "library-index.json");
        PlaylistsFile = Path.Combine(Root, "playlists.json");
        LogFile = Path.Combine(Logs, "cadence-studio.log");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(EnrichmentCache);
        Directory.CreateDirectory(ArtworkCache);
    }

    public string Root { get; }
    public string Cache { get; }
    public string Logs { get; }
    public string EnrichmentCache { get; }
    public string ArtworkCache { get; }
    public string SessionFile { get; }
    public string LibraryIndexFile { get; }
    public string PlaylistsFile { get; }
    public string LogFile { get; }

    public int CleanupStaleTemporaryFiles(TimeSpan? minimumAge = null)
    {
        var cutoff = DateTime.UtcNow - (minimumAge ?? TimeSpan.FromHours(1));
        var candidates = new List<string>();
        foreach (var directory in new[] { Root, Cache, EnrichmentCache, ArtworkCache })
        {
            try
            {
                // Cache folders are intentionally scanned only at their top level so
                // temp recovery never delays large-library startup.
                candidates.AddRange(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
            }
            catch
            {
                // One inaccessible support folder must not block the remaining cleanup.
            }
        }

        var removed = 0;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(candidate) > cutoff)
                {
                    continue;
                }

                File.Delete(candidate);
                removed++;
            }
            catch
            {
                // Best-effort cleanup. A locked or inaccessible temp file is harmless.
            }
        }

        return removed;
    }
}
