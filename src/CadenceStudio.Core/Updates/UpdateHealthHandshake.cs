using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadenceStudio.Core.Updates;

public sealed record UpdateHealthRequest(string TransactionId, string TargetVersion, string MarkerPath, string Token);

public sealed record UpdateHealthReceipt
{
    [JsonPropertyName("formatVersion")]
    public required int FormatVersion { get; init; }

    [JsonPropertyName("transactionId")]
    public required string TransactionId { get; init; }

    [JsonPropertyName("targetVersion")]
    public required string TargetVersion { get; init; }

    [JsonPropertyName("processId")]
    public required int ProcessId { get; init; }

    [JsonPropertyName("processStartUtcTicks")]
    public required long ProcessStartUtcTicks { get; init; }

    [JsonPropertyName("confirmedUtc")]
    public required DateTimeOffset ConfirmedUtc { get; init; }

    [JsonPropertyName("proof")]
    public required string Proof { get; init; }
}

public static class UpdateHealthHandshake
{
    public const string TokenEnvironmentVariable = "CADENCE_UPDATE_HEALTH_TOKEN";
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static bool TryParseStartupArguments(
        IReadOnlyList<string> args,
        out UpdateHealthRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        if (args.Count == 0 || !string.Equals(args[0], "--update-health", StringComparison.Ordinal))
            return true;

        if (args.Count != 7 ||
            !string.Equals(args[1], "--transaction-id", StringComparison.Ordinal) ||
            !string.Equals(args[3], "--target-version", StringComparison.Ordinal) ||
            !string.Equals(args[5], "--health-marker", StringComparison.Ordinal))
            return Fail("Malformed update-health startup arguments.", out error);

        var transactionId = args[2];
        var targetVersion = args[4];
        var markerPath = args[6];
        var token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        Environment.SetEnvironmentVariable(TokenEnvironmentVariable, null);

        if (!Guid.TryParseExact(transactionId, "N", out var id) || id == Guid.Empty || transactionId != id.ToString("N"))
            return Fail("Invalid update-health transaction ID.", out error);
        if (!string.Equals(targetVersion, ProductInfo.InformationalVersion, StringComparison.Ordinal))
            return Fail("Update-health target version does not match this Cadence Studio build.", out error);
        if (!IsLowerHex(token, 64))
            return Fail("Missing or invalid update-health token.", out error);

        var expectedMarker = Path.Combine(UpdateTransactionPaths.UpdatesRoot, transactionId, "health.json");
        try
        {
            if (!UpdateTransactionPaths.Same(markerPath, expectedMarker))
                return Fail("Update-health marker path is not transaction scoped.", out error);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fail("Update-health marker path is invalid.", out error);
        }

        request = new UpdateHealthRequest(transactionId, targetVersion, expectedMarker, token!);
        return true;
    }

    public static void ConfirmHealthy(UpdateHealthRequest request)
    {
        if (!IsLowerHex(request.Token, 64)) throw new InvalidDataException("Invalid update-health token.");
        var expectedMarker = Path.Combine(UpdateTransactionPaths.UpdatesRoot, request.TransactionId, "health.json");
        if (!UpdateTransactionPaths.Same(request.MarkerPath, expectedMarker))
            throw new InvalidDataException("Invalid update-health marker path.");
        if (File.Exists(request.MarkerPath))
            throw new InvalidDataException("Update-health marker already exists.");

        using var process = Process.GetCurrentProcess();
        var startTicks = process.StartTime.ToUniversalTime().Ticks;
        var receipt = new UpdateHealthReceipt
        {
            FormatVersion = 1,
            TransactionId = request.TransactionId,
            TargetVersion = request.TargetVersion,
            ProcessId = process.Id,
            ProcessStartUtcTicks = startTicks,
            ConfirmedUtc = DateTimeOffset.UtcNow,
            Proof = CreateProof(request.Token, request.TransactionId, request.TargetVersion, process.Id, startTicks)
        };

        var directory = Path.GetDirectoryName(request.MarkerPath) ?? throw new InvalidDataException("Missing health marker directory.");
        UpdateTransactionPaths.Canonical(directory);
        var temporary = Path.Combine(directory, $"health-{Guid.NewGuid():N}.tmp");
        UpdateTransactionPaths.Canonical(temporary);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, receipt, Json);
                stream.Flush(true);
            }
            File.Move(temporary, request.MarkerPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static bool WaitForConfirmation(
        UpdateTransaction transaction,
        Process process,
        long expectedStartUtcTicks,
        TimeSpan timeout,
        out string error)
    {
        error = string.Empty;
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
            return Fail("Invalid startup-health timeout.", out error);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!TryValidateRunningProcess(process, transaction.RestartExecutable, expectedStartUtcTicks, out error))
                return false;

            if (File.Exists(transaction.HealthMarkerPath))
            {
                if (!TryReadAndValidateReceipt(transaction, process.Id, expectedStartUtcTicks, out error))
                    return false;
                if (!TryValidateRunningProcess(process, transaction.RestartExecutable, expectedStartUtcTicks, out error))
                    return false;
                return true;
            }

            Thread.Sleep(100);
        }

        return Fail($"Updated application did not confirm startup health within {timeout.TotalSeconds:0} seconds.", out error);
    }

    public static void StopForRollback(Process? process, string expectedExecutable, long expectedStartUtcTicks)
    {
        if (process is null) return;
        process.Refresh();
        if (process.HasExited) return;
        if (process.StartTime.ToUniversalTime().Ticks != expectedStartUtcTicks ||
            !UpdateTransactionPaths.Same(process.MainModule!.FileName!, expectedExecutable))
            throw new InvalidDataException("Refusing to terminate a process whose identity does not match the update launch.");
        process.Kill(entireProcessTree: false);
        if (!process.WaitForExit(5000))
            throw new IOException("Updated application did not exit for rollback.");
    }

    private static bool TryReadAndValidateReceipt(
        UpdateTransaction transaction,
        int expectedProcessId,
        long expectedStartUtcTicks,
        out string error)
    {
        error = string.Empty;
        try
        {
            UpdateTransactionPaths.Canonical(transaction.HealthMarkerPath);
            using var stream = new FileStream(transaction.HealthMarkerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 4096)
                return Fail("Startup-health receipt has an invalid size.", out error);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
            RejectDuplicates(document.RootElement);
            var receipt = document.Deserialize<UpdateHealthReceipt>(Json);
            if (receipt is null || receipt.FormatVersion != 1 ||
                !string.Equals(receipt.TransactionId, transaction.TransactionId, StringComparison.Ordinal) ||
                !string.Equals(receipt.TargetVersion, transaction.TargetVersion, StringComparison.Ordinal) ||
                receipt.ProcessId != expectedProcessId || receipt.ProcessStartUtcTicks != expectedStartUtcTicks ||
                receipt.ConfirmedUtc < transaction.CreatedUtc || receipt.ConfirmedUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
                !IsLowerHex(receipt.Proof, 64))
                return Fail("Startup-health receipt identity does not match this update transaction.", out error);

            var expected = Convert.FromHexString(CreateProof(
                transaction.HealthToken,
                transaction.TransactionId,
                transaction.TargetVersion,
                expectedProcessId,
                expectedStartUtcTicks));
            var actual = Convert.FromHexString(receipt.Proof);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return Fail("Startup-health receipt proof is invalid.", out error);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException or ArgumentException)
        {
            return Fail("Startup-health receipt was rejected: " + exception.Message, out error);
        }
    }

    private static bool TryValidateRunningProcess(Process process, string expectedExecutable, long expectedStartUtcTicks, out string error)
    {
        error = string.Empty;
        try
        {
            process.Refresh();
            if (process.HasExited)
                return Fail("Updated application exited before startup health was confirmed.", out error);
            if (process.StartTime.ToUniversalTime().Ticks != expectedStartUtcTicks ||
                !UpdateTransactionPaths.Same(process.MainModule!.FileName!, expectedExecutable))
                return Fail("Updated application process identity changed before startup health was confirmed.", out error);
            return true;
        }
        catch (InvalidOperationException)
        {
            return Fail("Updated application exited before startup health was confirmed.", out error);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return Fail("Could not validate updated application process identity: " + exception.Message, out error);
        }
    }

    private static string CreateProof(string token, string transactionId, string targetVersion, int processId, long startUtcTicks)
    {
        var key = Convert.FromHexString(token);
        var message = Encoding.UTF8.GetBytes($"{transactionId}\n{targetVersion}\n{processId}\n{startUtcTicks}");
        return Convert.ToHexString(HMACSHA256.HashData(key, message)).ToLowerInvariant();
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Startup-health receipt must be a JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate startup-health receipt property.");
    }

    private static bool IsLowerHex(string? value, int length)
    {
        if (value is null || value.Length != length) return false;
        foreach (var c in value)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
