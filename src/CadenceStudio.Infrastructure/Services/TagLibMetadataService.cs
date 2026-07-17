using System.Collections.Concurrent;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;
using TagFile = TagLib.File;

namespace CadenceStudio.Infrastructure.Services;

public sealed class TagLibMetadataService : IMetadataService
{
    private const int MaxArtworkBytes = 25 * 1024 * 1024;

    private static readonly string[] PreferredArtworkNames =
    [
        "cover.jpg",
        "cover.jpeg",
        "cover.png",
        "folder.jpg",
        "folder.jpeg",
        "folder.png",
        "front.jpg",
        "front.jpeg",
        "front.png",
        "album.jpg",
        "album.jpeg",
        "album.png"
    ];

    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public TagLibMetadataService(IAppLogger logger)
    {
        _logger = logger;
    }

    public async Task<Track> ReadTrackAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(filePath);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The audio file could not be found.", fullPath);
        }

        var signature = new FileSignature(info.Length, info.LastWriteTimeUtc);
        if (_cache.TryGetValue(fullPath, out var cached) && cached.Signature == signature)
        {
            return cached.Track;
        }

        var track = await Task.Run(
            () => ReadTrackCore(fullPath, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        _cache[fullPath] = new CacheEntry(signature, track);
        return track;
    }

    public async Task<Stream?> OpenAlbumArtAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var track = await ReadTrackAsync(filePath, cancellationToken).ConfigureAwait(false);
        return track.ArtworkBytes is { Length: > 0 } bytes
            ? new MemoryStream(bytes, writable: false)
            : null;
    }

    private Track ReadTrackCore(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fallback = Track.FromPath(fullPath);
        var result = fallback;
        byte[]? artworkBytes = null;
        var artworkSource = "Cadence fallback";

        try
        {
            using var mediaFile = TagFile.Create(fullPath);
            var tag = mediaFile.Tag;
            var properties = mediaFile.Properties;

            artworkBytes = ReadEmbeddedArtwork(tag.Pictures);
            if (artworkBytes is { Length: > 0 })
            {
                artworkSource = "Embedded cover";
            }

            result = fallback with
            {
                Title = FirstValue(tag.Title, fallback.Title),
                Artist = FirstValue(JoinValues(tag.Performers), fallback.Artist),
                AlbumArtist = FirstValue(JoinValues(tag.AlbumArtists), string.Empty),
                Album = FirstValue(tag.Album, fallback.Album),
                Genre = FirstValue(JoinValues(tag.Genres), string.Empty),
                Duration = properties.Duration > TimeSpan.Zero ? properties.Duration : fallback.Duration,
                TrackNumber = ToNullableInt(tag.Track) ?? fallback.TrackNumber,
                TrackCount = ToNullableInt(tag.TrackCount),
                DiscNumber = ToNullableInt(tag.Disc),
                DiscCount = ToNullableInt(tag.DiscCount),
                Year = ToNullableInt(tag.Year),
                Codec = fallback.ExtensionLabel,
                SampleRateHz = Math.Max(0, properties.AudioSampleRate),
                AudioBitrateKbps = Math.Max(0, properties.AudioBitrate),
                BitsPerSample = Math.Max(0, properties.BitsPerSample),
                Channels = Math.Max(0, properties.AudioChannels)
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Error($"Metadata read failed; filename fallback will be used: {fullPath}", exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (artworkBytes is not { Length: > 0 })
        {
            var folderArtwork = ReadFolderArtwork(fullPath);
            artworkBytes = folderArtwork.Bytes;
            if (artworkBytes is { Length: > 0 })
            {
                artworkSource = folderArtwork.Source;
            }
        }

        return result with
        {
            ArtworkBytes = artworkBytes,
            ArtworkSource = artworkSource
        };
    }

    private static byte[]? ReadEmbeddedArtwork(IEnumerable<TagLib.IPicture>? pictures)
    {
        if (pictures is null)
        {
            return null;
        }

        var pictureList = pictures.Where(picture => picture is not null).ToArray();
        var selected = pictureList.FirstOrDefault(picture => picture.Type == TagLib.PictureType.FrontCover)
                       ?? pictureList.FirstOrDefault();

        if (selected?.Data is null || selected.Data.Count <= 0 || selected.Data.Count > MaxArtworkBytes)
        {
            return null;
        }

        return selected.Data.Data;
    }

    private static ArtworkResult ReadFolderArtwork(string audioFilePath)
    {
        var directory = Path.GetDirectoryName(audioFilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return ArtworkResult.Empty;
        }

        try
        {
            var filesByName = Directory.EnumerateFiles(directory)
                .ToDictionary(path => Path.GetFileName(path)!, path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var preferredName in PreferredArtworkNames)
            {
                if (!filesByName.TryGetValue(preferredName, out var candidate))
                {
                    continue;
                }

                var info = new FileInfo(candidate);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxArtworkBytes)
                {
                    continue;
                }

                return new ArtworkResult(
                    System.IO.File.ReadAllBytes(candidate),
                    $"Folder art • {Path.GetFileName(candidate)}");
            }
        }
        catch
        {
            // Folder artwork is optional. Metadata and playback should continue.
        }

        return ArtworkResult.Empty;
    }

    private static string JoinValues(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        return string.Join(", ", values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string FirstValue(string? primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();

    private static int? ToNullableInt(uint value) =>
        value is > 0 and <= int.MaxValue ? (int)value : null;

    private sealed record CacheEntry(FileSignature Signature, Track Track);
    private readonly record struct FileSignature(long Length, DateTime LastWriteUtc);
    private readonly record struct ArtworkResult(byte[]? Bytes, string Source)
    {
        public static ArtworkResult Empty { get; } = new(null, "Cadence fallback");
    }
}
