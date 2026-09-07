using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadenceStudio.Core;
using CadenceStudio.Core.Updates;

namespace CadenceStudio.Infrastructure.Services;

public sealed class UpdateDiscoveryService
{
    private const int MaximumManifestBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static Uri ManifestEndpoint { get; } = new(
        $"https://raw.githubusercontent.com/MikereDD/Cadence-Studio/main/updates/{ProductInfo.UpdateChannel}/release-manifest.json");

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var architecture = GetCurrentArchitecture();
        if (architecture is null)
        {
            return new UpdateCheckResult(
                UpdateCheckState.UnsupportedArchitecture,
                $"Update discovery does not support process architecture {RuntimeInformation.ProcessArchitecture}.");
        }

        if (!IsApprovedManifestEndpoint(ManifestEndpoint))
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                "The configured update manifest endpoint is not an approved Cadence Studio HTTPS origin.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestEndpoint);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.ManifestUnavailable,
                    $"No {ProductInfo.UpdateChannel} update manifest is published at the approved endpoint yet.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.Failed,
                    $"Update manifest endpoint returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var json = await ReadManifestTextAsync(response.Content, cancellationToken);
            ReleaseManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, JsonOptions);
            }
            catch (JsonException exception)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.InvalidManifest,
                    $"Update manifest JSON was rejected: {exception.Message}");
            }

            if (manifest is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.InvalidManifest,
                    "Update manifest was empty.");
            }

            if (!ReleaseManifestValidator.TryValidateForCadence(
                    manifest,
                    architecture,
                    out var candidateVersion,
                    out var minimumVersion,
                    out var validationError))
            {
                return new UpdateCheckResult(
                    UpdateCheckState.InvalidManifest,
                    $"Update manifest was rejected: {validationError}");
            }

            if (ProductInfo.UpdaterProtocolVersion < manifest.MinimumUpdaterProtocolVersion)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.UpdaterTooOld,
                    $"Update {manifest.Version} requires updater protocol {manifest.MinimumUpdaterProtocolVersion} or newer.",
                    manifest.Version,
                    manifest.Mandatory,
                    manifest.ReleaseNotesUrl);
            }

            if (!ReleaseVersion.TryParse(ProductInfo.InformationalVersion, out var installedVersion))
            {
                return new UpdateCheckResult(
                    UpdateCheckState.Failed,
                    $"Installed Cadence Studio version {ProductInfo.InformationalVersion} is malformed.");
            }

            if (installedVersion!.CompareTo(minimumVersion) < 0)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.FullInstallerRequired,
                    $"Update {manifest.Version} cannot be applied directly from {ProductInfo.InformationalVersion}; a full installer path is required.",
                    manifest.Version,
                    manifest.Mandatory,
                    manifest.ReleaseNotesUrl);
            }

            var comparison = candidateVersion!.CompareTo(installedVersion);
            if (comparison <= 0)
            {
                var message = comparison == 0
                    ? $"Cadence Studio {ProductInfo.DisplayVersion} is current on the {ProductInfo.UpdateChannel} channel."
                    : $"This build is newer than the published {ProductInfo.UpdateChannel} manifest ({manifest.Version}).";

                return new UpdateCheckResult(
                    UpdateCheckState.Current,
                    message,
                    manifest.Version,
                    manifest.Mandatory,
                    manifest.ReleaseNotesUrl);
            }

            return new UpdateCheckResult(
                UpdateCheckState.UpdateAvailable,
                $"Update v{manifest.Version} is available. Manifest discovery and compatibility validation passed; download and installation are intentionally disabled in v1.1-dev.3.",
                manifest.Version,
                manifest.Mandatory,
                manifest.ReleaseNotesUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                "Update check timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                $"Update check could not reach the approved manifest endpoint: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            return new UpdateCheckResult(
                UpdateCheckState.InvalidManifest,
                $"Update manifest was rejected: {exception.Message}");
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                $"Update check failed safely: {exception.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"CadenceStudio/{ProductInfo.InformationalVersion}");
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    private static string? GetCurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };

    private static bool IsApprovedManifestEndpoint(Uri uri)
    {
        var expectedPath =
            $"/MikereDD/Cadence-Studio/main/updates/{ProductInfo.UpdateChannel}/release-manifest.json";

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment);
    }

    private static async Task<string> ReadManifestTextAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"Manifest exceeds the {MaximumManifestBytes / 1024} KiB size limit.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumManifestBytes)
            {
                throw new InvalidDataException(
                    $"Manifest exceeds the {MaximumManifestBytes / 1024} KiB size limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
