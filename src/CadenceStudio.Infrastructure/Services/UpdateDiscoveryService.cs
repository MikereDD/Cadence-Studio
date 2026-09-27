using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
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

    public static Uri ManifestSignatureEndpoint { get; } = new(
        $"https://raw.githubusercontent.com/MikereDD/Cadence-Studio/main/updates/{ProductInfo.UpdateChannel}/release-manifest.json.sig");

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var architecture = GetCurrentArchitecture();
        if (architecture is null)
        {
            return new UpdateCheckResult(
                UpdateCheckState.UnsupportedArchitecture,
                $"Update discovery does not support process architecture {RuntimeInformation.ProcessArchitecture}.");
        }

        if (!IsApprovedManifestEndpoint(ManifestEndpoint, "release-manifest.json") ||
            !IsApprovedManifestEndpoint(ManifestSignatureEndpoint, "release-manifest.json.sig"))
        {
            return new UpdateCheckResult(
                UpdateCheckState.Failed,
                "The configured update manifest endpoints are not approved Cadence Studio HTTPS origins.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestEndpoint);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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

            var manifestBytes = await ReadBoundedBytesAsync(
                response.Content,
                MaximumManifestBytes,
                "Manifest",
                cancellationToken);

            using var signatureRequest = new HttpRequestMessage(HttpMethod.Get, ManifestSignatureEndpoint);
            signatureRequest.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            signatureRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var signatureResponse = await HttpClient.SendAsync(
                signatureRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (signatureResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.InvalidManifest,
                    "A release manifest was published without its required detached manifest signature.");
            }

            if (!signatureResponse.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.Failed,
                    $"Manifest signature endpoint returned HTTP {(int)signatureResponse.StatusCode} ({signatureResponse.ReasonPhrase}).");
            }

            var signatureBytes = await ReadBoundedBytesAsync(
                signatureResponse.Content,
                ReleaseManifestAuthentication.MaximumSignatureBytes,
                "Manifest signature",
                cancellationToken);

            // The exact published manifest bytes are authenticated before any remote
            // security-sensitive field is deserialized or acted upon.
            var authorization = ReleaseManifestAuthentication.VerifyExact(manifestBytes, signatureBytes);

            ReleaseManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonOptions);
            }
            catch (JsonException exception)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.InvalidManifest,
                    $"Authenticated update manifest JSON was rejected: {exception.Message}");
            }

            if (manifest is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckState.InvalidManifest,
                    "Authenticated update manifest was empty.");
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
                    $"Authenticated update manifest was rejected: {validationError}");
            }

            var manifestHash = ReleaseManifestAuthentication.ComputeSha256(manifestBytes);
            var replayStore = new ManifestReplayStateStore();
            replayStore.Observe(manifest.ManifestSequence, manifestHash, manifest.PublishedAt);

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
                    ? $"Cadence Studio {ProductInfo.DisplayVersion} is current on the {ProductInfo.UpdateChannel} channel. Signed manifest sequence {manifest.ManifestSequence} is trusted."
                    : $"This build is newer than the published {ProductInfo.UpdateChannel} manifest ({manifest.Version}). Signed manifest sequence {manifest.ManifestSequence} is trusted.";

                return new UpdateCheckResult(
                    UpdateCheckState.Current,
                    message,
                    manifest.Version,
                    manifest.Mandatory,
                    manifest.ReleaseNotesUrl);
            }

            return new UpdateCheckResult(
                UpdateCheckState.UpdateAvailable,
                $"Update v{manifest.Version} is available. Exact manifest bytes were authorized by {authorization.KeyId}; manifest sequence {manifest.ManifestSequence}, replay checks, and compatibility validation passed.",
                manifest.Version,
                manifest.Mandatory,
                manifest.ReleaseNotesUrl,
                manifest);
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
                $"Update check could not reach the approved manifest endpoints: {exception.Message}");
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
        return client;
    }

    private static string? GetCurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };

    private static bool IsApprovedManifestEndpoint(Uri uri, string fileName)
    {
        var expectedPath =
            $"/MikereDD/Cadence-Studio/main/updates/{ProductInfo.UpdateChannel}/{fileName}";

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal) &&
               string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment);
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(
        HttpContent content,
        int maximumBytes,
        string label,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentEncoding.Count != 0)
            throw new InvalidDataException($"{label} response must not use content encoding.");
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
            throw new InvalidDataException($"{label} exceeds the {maximumBytes} byte size limit.");

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumBytes)
                throw new InvalidDataException($"{label} exceeds the {maximumBytes} byte size limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (buffer.Length == 0)
            throw new InvalidDataException($"{label} response is empty.");
        return buffer.ToArray();
    }
}
