using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;

namespace CadenceStudio.Infrastructure.Services;

public sealed class JsonSessionService(AppPaths paths, IAppLogger logger) : ISessionService
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SessionFile))
        {
            logger.Info("No Cadence Studio session file found; using defaults.");
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(paths.SessionFile);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, _options, cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();

            settings.Normalize();
            logger.Info("Session restored.");
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            PreserveCorruptSession();
            logger.Error("Session could not be loaded; defaults will be used.", exception);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var tempFile = paths.SessionFile + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                             tempFile,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFile, paths.SessionFile, overwrite: true);
            logger.Info("Session saved.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Error("Session could not be saved.", exception);
            throw;
        }
        finally
        {
            TryDelete(tempFile);
            _saveGate.Release();
        }
    }

    private void PreserveCorruptSession()
    {
        try
        {
            var suffix = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var destination = paths.SessionFile + $".corrupt-{suffix}";
            File.Move(paths.SessionFile, destination, overwrite: true);
        }
        catch (Exception exception)
        {
            logger.Warning($"The corrupt session file could not be preserved: {exception.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
