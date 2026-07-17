using System.Runtime.CompilerServices;
using System.Text.Json;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;

namespace CadenceStudio.Infrastructure.Services;

public sealed class JsonLibraryService : ILibraryService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".wma", ".ogg", ".opus", ".aiff", ".aif"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // A compact index materially reduces parse and disk I/O time for large libraries.
        WriteIndented = false,
        DefaultBufferSize = 64 * 1024
    };

    private readonly AppPaths _paths;
    private readonly IMetadataService _metadataService;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, LibraryIndexEntryDocument>? _entries;
    private DateTimeOffset? _lastScanUtc;
    private bool _needsCompactRewrite;
    private int _compactRewriteScheduled;

    public JsonLibraryService(
        AppPaths paths,
        IMetadataService metadataService,
        IAppLogger logger)
    {
        _paths = paths;
        _metadataService = metadataService;
        _logger = logger;
    }

    public async Task<LibrarySnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        LibrarySnapshot snapshot;
        var scheduleCompactRewrite = false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);

            // Do not probe every indexed path during startup. A 40k-track library
            // previously caused tens of thousands of synchronous File.Exists calls
            // before the browser could appear. Missing paths are handled when played
            // and are removed by the next incremental scan.
            snapshot = CreateSnapshot();
            scheduleCompactRewrite = _needsCompactRewrite;
            _needsCompactRewrite = false;
        }
        finally
        {
            _gate.Release();
        }

        if (scheduleCompactRewrite)
        {
            ScheduleCompactRewrite();
        }

        return snapshot;
    }

    public async Task<LibraryScanResult> ScanAsync(
        IEnumerable<string> roots,
        IProgress<LibraryScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedCoreAsync(cancellationToken).ConfigureAwait(false);

            var normalizedRoots = roots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            progress?.Report(new LibraryScanProgress(
                "Discovering music files", 0, 0, 0, 0, normalizedRoots.FirstOrDefault()));

            var discoveredPaths = new List<string>();
            var discoveryErrors = 0;

            foreach (var root in normalizedRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var filePath in EnumerateAudioFilesSafe(root, errorPath =>
                         {
                             discoveryErrors++;
                             _logger.Info($"Library scan skipped inaccessible path: {errorPath}");
                         }, cancellationToken))
                {
                    discoveredPaths.Add(filePath);
                    if (discoveredPaths.Count % 25 == 0)
                    {
                        progress?.Report(new LibraryScanProgress(
                            "Discovering music files",
                            discoveredPaths.Count,
                            0,
                            0,
                            discoveryErrors,
                            filePath));
                    }
                }
            }

            discoveredPaths = discoveredPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var previousEntries = _entries!;
            var nextEntries = new Dictionary<string, LibraryIndexEntryDocument>(StringComparer.OrdinalIgnoreCase);
            var inMemoryTracks = new List<Track>(discoveredPaths.Count);
            var added = 0;
            var updated = 0;
            var failed = discoveryErrors;
            var processed = 0;

            foreach (var filePath in discoveredPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processed++;

                try
                {
                    var info = new FileInfo(filePath);
                    var signatureMatches = previousEntries.TryGetValue(filePath, out var previous) &&
                                           previous.Length == info.Length &&
                                           previous.LastWriteTimeUtc == info.LastWriteTimeUtc;

                    Track track;
                    LibraryIndexEntryDocument document;

                    if (signatureMatches && previous is not null)
                    {
                        track = previous.Track.ToTrack();
                        document = previous;
                    }
                    else
                    {
                        track = await _metadataService
                            .ReadTrackAsync(filePath, cancellationToken)
                            .ConfigureAwait(false);

                        document = LibraryIndexEntryDocument.FromTrack(info, track);
                        if (previous is null)
                        {
                            added++;
                        }
                        else
                        {
                            updated++;
                        }
                    }

                    nextEntries[filePath] = document;
                    inMemoryTracks.Add(track);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failed++;
                    _logger.Error($"Library indexing failed for: {filePath}", exception);

                    try
                    {
                        var info = new FileInfo(filePath);
                        var fallback = Track.FromPath(filePath);
                        nextEntries[filePath] = LibraryIndexEntryDocument.FromTrack(info, fallback);
                        inMemoryTracks.Add(fallback);
                    }
                    catch (Exception fallbackException)
                    {
                        _logger.Error($"Library fallback indexing also failed for: {filePath}", fallbackException);
                    }
                }

                progress?.Report(new LibraryScanProgress(
                    "Reading tags and updating index",
                    discoveredPaths.Count,
                    processed,
                    inMemoryTracks.Count,
                    failed,
                    filePath));
            }

            var removed = previousEntries.Keys.Count(path => !nextEntries.ContainsKey(path));
            var completedUtc = DateTimeOffset.UtcNow;

            // Persist the complete replacement index before publishing it in memory.
            // A cancelled or failed write therefore leaves the previous known-good
            // index intact both on disk and for the remainder of this app session.
            await SaveCoreAsync(nextEntries, completedUtc, cancellationToken).ConfigureAwait(false);
            _entries = nextEntries;
            _lastScanUtc = completedUtc;

            // discoveredPaths is already ordinal-path sorted, so avoid a second
            // metadata sort that the folder browser immediately discards.
            var orderedTracks = inMemoryTracks.ToArray();

            var snapshot = new LibrarySnapshot(orderedTracks, _lastScanUtc);
            progress?.Report(new LibraryScanProgress(
                "Library scan complete",
                discoveredPaths.Count,
                processed,
                orderedTracks.Length,
                failed,
                null));

            _logger.Info(
                $"Library scan complete. Files={discoveredPaths.Count}, Added={added}, Updated={updated}, Removed={removed}, Failed={failed}.");

            return new LibraryScanResult(
                snapshot,
                discoveredPaths.Count,
                added,
                updated,
                removed,
                failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<Track> SearchAsync(
        string query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var normalizedQuery = query?.Trim() ?? string.Empty;

        foreach (var track in snapshot.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (normalizedQuery.Length == 0 || Matches(track, normalizedQuery))
            {
                yield return track;
            }
        }
    }

    private async Task EnsureLoadedCoreAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null)
        {
            return;
        }

        _entries = new Dictionary<string, LibraryIndexEntryDocument>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_paths.LibraryIndexFile))
        {
            return;
        }

        try
        {
            await using var stream = new FileStream(
                _paths.LibraryIndexFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer
                .DeserializeAsync<LibraryIndexDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (document?.Entries is null)
            {
                return;
            }

            foreach (var entry in document.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FilePath) || entry.Track is null)
                {
                    continue;
                }

                var normalized = NormalizePath(entry.FilePath);
                entry.FilePath = normalized;
                entry.Track.FilePath = normalized;
                _entries[normalized] = entry;
            }

            _lastScanUtc = document.LastScanUtc;
            _needsCompactRewrite = document.SchemaVersion < 2;
            _logger.Info($"Loaded library index with {_entries.Count} entries.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Error("The library index could not be loaded; a new scan can rebuild it.", exception);
            PreserveCorruptIndex();
            _entries.Clear();
            _lastScanUtc = null;
        }
    }

    private async Task SaveCoreAsync(
        IReadOnlyDictionary<string, LibraryIndexEntryDocument> entries,
        DateTimeOffset lastScanUtc,
        CancellationToken cancellationToken)
    {
        var document = new LibraryIndexDocument
        {
            SchemaVersion = 2,
            LastScanUtc = lastScanUtc,
            Entries = entries.Values
                .OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        var tempPath = _paths.LibraryIndexFile + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _paths.LibraryIndexFile, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // Temporary-file cleanup is best effort.
            }

            throw;
        }
    }

    private LibrarySnapshot CreateSnapshot()
    {
        var tracks = _entries!.Values
            .OrderBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Track.ToTrack())
            .ToArray();

        return new LibrarySnapshot(tracks, _lastScanUtc);
    }

    private void ScheduleCompactRewrite()
    {
        if (Interlocked.Exchange(ref _compactRewriteScheduled, 1) != 0)
        {
            return;
        }

        _ = Task.Run(RewriteCompactIndexAsync);
    }

    private async Task RewriteCompactIndexAsync()
    {
        try
        {
            // Let startup and the first library render finish before using disk and CPU
            // for the one-time schema migration.
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries is null)
                {
                    return;
                }

                await SaveCoreAsync(
                        _entries,
                        _lastScanUtc ?? DateTimeOffset.UtcNow,
                        CancellationToken.None)
                    .ConfigureAwait(false);

                _logger.Info("Migrated the library index to the compact startup format.");
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception exception)
        {
            _logger.Error("The library index could not be compacted in the background.", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _compactRewriteScheduled, 0);
        }
    }

    private static IEnumerable<string> EnumerateAudioFilesSafe(
        string root,
        Action<string> onError,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch
            {
                onError(directory);
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (SupportedExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return NormalizePath(file);
                }
            }

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(directory);
            }
            catch
            {
                onError(directory);
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(subdirectory);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    pending.Push(subdirectory);
                }
                catch
                {
                    onError(subdirectory);
                }
            }
        }
    }

    private static bool Matches(Track track, string query) =>
        Contains(track.Title, query) ||
        Contains(track.Artist, query) ||
        Contains(track.AlbumArtist, query) ||
        Contains(track.Album, query) ||
        Contains(track.Genre, query) ||
        Contains(track.FilePath, query);

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string GetPrimaryArtist(Track track) =>
        string.IsNullOrWhiteSpace(track.AlbumArtist) ? track.Artist : track.AlbumArtist;

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private void PreserveCorruptIndex()
    {
        try
        {
            if (!File.Exists(_paths.LibraryIndexFile))
            {
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var preservedPath = Path.Combine(_paths.Cache, $"library-index.corrupt-{timestamp}.json");
            File.Move(_paths.LibraryIndexFile, preservedPath, overwrite: true);
        }
        catch
        {
            // Recovery is best effort. A rescan can always rebuild the index.
        }
    }

    private sealed class LibraryIndexDocument
    {
        public LibraryIndexDocument() { }

        public int SchemaVersion { get; set; } = 2;
        public DateTimeOffset? LastScanUtc { get; set; }
        public List<LibraryIndexEntryDocument> Entries { get; set; } = [];
    }

    private sealed class LibraryIndexEntryDocument
    {
        public LibraryIndexEntryDocument() { }

        public string FilePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public TrackDocument Track { get; set; } = new();

        public static LibraryIndexEntryDocument FromTrack(FileInfo info, Track track) => new()
        {
            FilePath = info.FullName,
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
            Track = TrackDocument.FromTrack(track)
        };
    }

    private sealed class TrackDocument
    {
        public TrackDocument() { }

        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = "Unknown artist";
        public string AlbumArtist { get; set; } = string.Empty;
        public string Album { get; set; } = "Unknown album";
        public string Genre { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int? TrackNumber { get; set; }
        public int? TrackCount { get; set; }
        public int? DiscNumber { get; set; }
        public int? DiscCount { get; set; }
        public int? Year { get; set; }
        public string Codec { get; set; } = string.Empty;
        public int SampleRateHz { get; set; }
        public int AudioBitrateKbps { get; set; }
        public int BitsPerSample { get; set; }
        public int Channels { get; set; }

        public static TrackDocument FromTrack(Track track) => new()
        {
            FilePath = track.FilePath,
            Title = track.Title,
            Artist = track.Artist,
            AlbumArtist = track.AlbumArtist,
            Album = track.Album,
            Genre = track.Genre,
            Duration = track.Duration,
            TrackNumber = track.TrackNumber,
            TrackCount = track.TrackCount,
            DiscNumber = track.DiscNumber,
            DiscCount = track.DiscCount,
            Year = track.Year,
            Codec = track.Codec,
            SampleRateHz = track.SampleRateHz,
            AudioBitrateKbps = track.AudioBitrateKbps,
            BitsPerSample = track.BitsPerSample,
            Channels = track.Channels
        };

        public Track ToTrack() => new()
        {
            FilePath = FilePath,
            Title = string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(FilePath) : Title,
            Artist = string.IsNullOrWhiteSpace(Artist) ? "Unknown artist" : Artist,
            AlbumArtist = AlbumArtist,
            Album = string.IsNullOrWhiteSpace(Album) ? "Unknown album" : Album,
            Genre = Genre,
            Duration = Duration,
            TrackNumber = TrackNumber,
            TrackCount = TrackCount,
            DiscNumber = DiscNumber,
            DiscCount = DiscCount,
            Year = Year,
            Codec = Codec,
            SampleRateHz = SampleRateHz,
            AudioBitrateKbps = AudioBitrateKbps,
            BitsPerSample = BitsPerSample,
            Channels = Channels,
            ArtworkBytes = null,
            ArtworkSource = "Indexed metadata"
        };
    }
}
