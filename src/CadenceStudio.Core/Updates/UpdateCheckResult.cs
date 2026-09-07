namespace CadenceStudio.Core.Updates;

public enum UpdateCheckState
{
    Current,
    UpdateAvailable,
    ManifestUnavailable,
    InvalidManifest,
    FullInstallerRequired,
    UpdaterTooOld,
    UnsupportedArchitecture,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    string Message,
    string? CandidateVersion = null,
    bool Mandatory = false,
    string? ReleaseNotesUrl = null);
