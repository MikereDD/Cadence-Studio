using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadenceStudio.Core.Updates;

public sealed class ManifestReplayStateStore
{
    private const int StateFormatVersion = 1;
    private const int MaximumStateBytes = 8 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    private readonly string _root;

    public ManifestReplayStateStore(string? root = null)
    {
        _root = UpdateTransactionPaths.Canonical(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CadenceStudio",
            "update-trust"));
        StatePath = UpdateTransactionPaths.Canonical(Path.Combine(
            _root,
            $"manifest-{ProductInfo.UpdateChannel}.json"));
    }

    public string StatePath { get; }

    // Returns true when trusted state advanced, false when the exact same authenticated
    // manifest sequence/hash was observed again.
    public bool Observe(long manifestSequence, string manifestSha256, string publishedAt)
    {
        if (manifestSequence <= 0)
            throw new InvalidDataException("Manifest sequence must be positive.");

        var normalizedHash = NormalizeSha256(manifestSha256);
        if (!DateTimeOffset.TryParse(publishedAt, out _))
            throw new InvalidDataException("Manifest publishedAt is invalid.");

        Directory.CreateDirectory(_root);
        UpdateTransactionPaths.RejectReparsePoints(_root);
        UpdateTransactionPaths.Canonical(StatePath);
        var lockPath = UpdateTransactionPaths.Canonical(Path.Combine(_root, "manifest-state.lock"));
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        ManifestReplayState? current = null;
        if (File.Exists(StatePath)) current = ReadState();

        if (current is not null)
        {
            if (manifestSequence < current.HighestManifestSequence)
                throw new InvalidDataException("Authenticated manifest sequence rollback detected.");

            if (manifestSequence == current.HighestManifestSequence)
            {
                if (!string.Equals(normalizedHash, current.ManifestSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Authenticated manifest sequence was reused for different bytes.");
                return false;
            }
        }

        var next = new ManifestReplayState
        {
            FormatVersion = StateFormatVersion,
            AppId = ProductInfo.AppId,
            Channel = ProductInfo.UpdateChannel,
            HighestManifestSequence = manifestSequence,
            ManifestSha256 = normalizedHash,
            PublishedAt = publishedAt,
            ObservedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        WriteState(next);
        return true;
    }

    private ManifestReplayState ReadState()
    {
        UpdateTransactionPaths.Canonical(StatePath);
        using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumStateBytes)
            throw new InvalidDataException("Manifest replay state size is invalid.");

        ManifestReplayState state;
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicates(document.RootElement);
            state = document.Deserialize<ManifestReplayState>(Json) ??
                throw new InvalidDataException("Manifest replay state is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Manifest replay state JSON is invalid.", exception);
        }

        if (state.FormatVersion != StateFormatVersion ||
            !string.Equals(state.AppId, ProductInfo.AppId, StringComparison.Ordinal) ||
            !string.Equals(state.Channel, ProductInfo.UpdateChannel, StringComparison.Ordinal) ||
            state.HighestManifestSequence <= 0 ||
            NormalizeSha256(state.ManifestSha256) != state.ManifestSha256 ||
            !DateTimeOffset.TryParse(state.PublishedAt, out _) ||
            !DateTimeOffset.TryParse(state.ObservedUtc, out _))
            throw new InvalidDataException("Manifest replay state is invalid.");

        return state;
    }

    private void WriteState(ManifestReplayState state)
    {
        var temporary = UpdateTransactionPaths.Canonical(Path.Combine(
            _root,
            $"manifest-state-{Guid.NewGuid():N}.tmp"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, Json);
                stream.Flush(flushToDisk: true);
            }

            UpdateTransactionPaths.Canonical(StatePath);
            if (File.Exists(StatePath))
                File.Replace(temporary, StatePath, destinationBackupFileName: null);
            else
                File.Move(temporary, StatePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string NormalizeSha256(string value)
    {
        if (value.Length != 64)
            throw new InvalidDataException("Manifest SHA-256 is invalid.");
        try
        {
            var bytes = Convert.FromHexString(value);
            if (bytes.Length != 32) throw new InvalidDataException("Manifest SHA-256 is invalid.");
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Manifest SHA-256 is invalid.", exception);
        }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("Duplicate manifest replay-state property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
        }
    }

    private sealed class ManifestReplayState
    {
        public required int FormatVersion { get; init; }
        public required string AppId { get; init; }
        public required string Channel { get; init; }
        public required long HighestManifestSequence { get; init; }
        public required string ManifestSha256 { get; init; }
        public required string PublishedAt { get; init; }
        public required string ObservedUtc { get; init; }
    }
}
