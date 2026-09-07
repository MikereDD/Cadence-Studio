namespace CadenceStudio.Core.Updates;

public enum UpdateTransactionState { Prepared, Validated, ProcessRunning, ProcessClosed, DryRunCompleted, Failed }

// This local format is deliberately separate from the release-manifest protocol.
// No state in this foundation authorizes installation or represents verified bytes.
public sealed record UpdateTransaction
{
    public required int FormatVersion { get; init; }
    public required string AppId { get; init; }
    public required string TransactionId { get; init; }
    public required string CurrentVersion { get; init; }
    public required string TargetVersion { get; init; }
    public required bool SyntheticTest { get; init; }
    public required string InstallRoot { get; init; }
    public required string InstalledExecutable { get; init; }
    public required string RestartExecutable { get; init; }
    public required string StagingRoot { get; init; }
    public required string PayloadPath { get; init; }
    public required string SignaturePath { get; init; }
    public required string ExtractionPath { get; init; }
    public required string BackupPath { get; init; }
    public required int ProcessId { get; init; }
    public required long ProcessStartUtcTicks { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required UpdateTransactionState State { get; init; }
}
