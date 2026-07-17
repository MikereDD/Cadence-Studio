using System.Globalization;

namespace CadenceStudio.Core.Models;

public sealed record Track
{
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public string Artist { get; init; } = "Unknown artist";
    public string AlbumArtist { get; init; } = string.Empty;
    public string Album { get; init; } = "Unknown album";
    public string Genre { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public int? TrackNumber { get; init; }
    public int? TrackCount { get; init; }
    public int? DiscNumber { get; init; }
    public int? DiscCount { get; init; }
    public int? Year { get; init; }
    public string Codec { get; init; } = string.Empty;
    public int SampleRateHz { get; init; }
    public int AudioBitrateKbps { get; init; }
    public int BitsPerSample { get; init; }
    public int Channels { get; init; }
    public byte[]? ArtworkBytes { get; init; }
    public string ArtworkSource { get; init; } = "Cadence fallback";

    public string FileName => Path.GetFileName(FilePath);
    public string ExtensionLabel => Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();
    public string DurationText => FormatTime(Duration);
    public bool HasArtwork => ArtworkBytes is { Length: > 0 };
    public string DisplayCodec => string.IsNullOrWhiteSpace(Codec) ? ExtensionLabel : Codec;

    public string FormatDescription
    {
        get
        {
            var parts = new List<string>();
            AddIfPresent(parts, DisplayCodec);

            if (SampleRateHz > 0)
            {
                parts.Add(SampleRateHz >= 1000
                    ? $"{SampleRateHz / 1000d:0.#} kHz"
                    : $"{SampleRateHz} Hz");
            }

            if (BitsPerSample > 0)
            {
                parts.Add($"{BitsPerSample} bit");
            }

            if (Channels > 0)
            {
                parts.Add(Channels switch
                {
                    1 => "mono",
                    2 => "stereo",
                    _ => $"{Channels} ch"
                });
            }

            if (AudioBitrateKbps > 0)
            {
                parts.Add($"{AudioBitrateKbps} kbps");
            }

            return parts.Count == 0 ? "Audio details unavailable" : string.Join(" • ", parts);
        }
    }

    public string MetadataDescription
    {
        get
        {
            var parts = new List<string>();
            AddIfPresent(parts, Genre);

            if (Year is > 0)
            {
                parts.Add(Year.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (TrackNumber is > 0)
            {
                parts.Add(TrackCount is > 0
                    ? $"Track {TrackNumber}/{TrackCount}"
                    : $"Track {TrackNumber}");
            }

            if (DiscNumber is > 0)
            {
                parts.Add(DiscCount is > 0
                    ? $"Disc {DiscNumber}/{DiscCount}"
                    : $"Disc {DiscNumber}");
            }

            return parts.Count == 0 ? "No additional tags" : string.Join(" • ", parts);
        }
    }

    public static Track FromPath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var fileTitle = Path.GetFileNameWithoutExtension(fullPath);
        var parts = fileTitle.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var title = fileTitle;
        var artist = "Unknown artist";
        var directory = Path.GetDirectoryName(fullPath);
        var album = string.IsNullOrWhiteSpace(directory)
            ? "Unknown album"
            : new DirectoryInfo(directory).Name;
        int? trackNumber = null;

        if (parts.Length >= 4 && TryParseTrackNumber(parts[0], out var parsedTrack))
        {
            trackNumber = parsedTrack;
            artist = parts[1];
            album = parts[2];
            title = string.Join(" - ", parts.Skip(3));
        }
        else if (parts.Length >= 3)
        {
            artist = parts[0];
            album = parts[1];
            title = string.Join(" - ", parts.Skip(2));
        }
        else if (parts.Length == 2)
        {
            artist = parts[0];
            title = parts[1];
        }

        return new Track
        {
            FilePath = fullPath,
            Title = string.IsNullOrWhiteSpace(title) ? fileTitle : title,
            Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist,
            Album = string.IsNullOrWhiteSpace(album) ? "Unknown album" : album,
            TrackNumber = trackNumber,
            Codec = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant()
        };
    }

    private static void AddIfPresent(ICollection<string> target, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add(value.Trim());
        }
    }

    private static bool TryParseTrackNumber(string value, out int trackNumber)
    {
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out trackNumber);
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
