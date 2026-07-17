using System.Text;
using System.Text.Json;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;

namespace CadenceStudio.Infrastructure.Services;

public sealed class JsonPlaylistService : IPlaylistService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public JsonPlaylistService(AppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SavedPlaylist>> LoadSavedAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.PlaylistsFile))
        {
            return Array.Empty<SavedPlaylist>();
        }

        try
        {
            await using var stream = File.OpenRead(_paths.PlaylistsFile);
            var playlists = await JsonSerializer.DeserializeAsync<List<SavedPlaylist>>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? [];

            foreach (var playlist in playlists)
            {
                playlist.Normalize();
            }

            return playlists
                .OrderBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.Error("Saved playlists could not be loaded.", exception);
            PreserveCorruptPlaylistFile();
            return Array.Empty<SavedPlaylist>();
        }
    }

    public async Task SaveSavedAsync(
        IEnumerable<SavedPlaylist> playlists,
        CancellationToken cancellationToken = default)
    {
        var snapshot = playlists.Select(playlist => new SavedPlaylist
        {
            Id = playlist.Id,
            Name = playlist.Name,
            TrackPaths = [.. playlist.TrackPaths],
            CreatedUtc = playlist.CreatedUtc,
            UpdatedUtc = playlist.UpdatedUtc
        }).ToList();

        foreach (var playlist in snapshot)
        {
            playlist.Normalize();
        }

        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var temporaryPath = _paths.PlaylistsFile + ".tmp";
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.PlaylistsFile, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task<SavedPlaylist> ImportAsync(
        string playlistPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistPath);

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(playlistPath)) ?? Environment.CurrentDirectory;
        var lines = await File.ReadAllLinesAsync(playlistPath, cancellationToken).ConfigureAwait(false);
        var paths = new List<string>();

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = rawLine.Trim().TrimStart('\uFEFF').Trim('"');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string resolved;
            if (Path.IsPathRooted(line))
            {
                resolved = Path.GetFullPath(line);
            }
            else if (Uri.TryCreate(line, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile)
                {
                    continue;
                }

                resolved = uri.LocalPath;
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(baseDirectory, line));
            }

            paths.Add(resolved);
        }

        var playlist = new SavedPlaylist
        {
            Name = Path.GetFileNameWithoutExtension(playlistPath),
            TrackPaths = paths,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        playlist.Normalize();
        return playlist;
    }

    public async Task ExportAsync(
        string playlistPath,
        SavedPlaylist playlist,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistPath);
        ArgumentNullException.ThrowIfNull(playlist);

        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        foreach (var trackPath in playlist.TrackPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(trackPath);
        }

        await File.WriteAllTextAsync(
            playlistPath,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
    }

    private void PreserveCorruptPlaylistFile()
    {
        try
        {
            if (!File.Exists(_paths.PlaylistsFile))
            {
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            File.Move(_paths.PlaylistsFile, _paths.PlaylistsFile + $".corrupt-{timestamp}", overwrite: true);
        }
        catch (Exception exception)
        {
            _logger.Error("Corrupt playlist file could not be preserved.", exception);
        }
    }
}
