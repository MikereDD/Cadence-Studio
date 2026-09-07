namespace CadenceStudio.Core.Updates;

public static class UpdateTransactionPaths
{
    public static string UpdatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CadenceStudio", "updates");

    public static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains('/') ||
            path.Length < 3 || path[1] != ':' || path[2] != '\\' ||
            path[2..].Contains(':'))
            throw new InvalidDataException("A canonical local drive path is required.");
        foreach (var segment in path[3..].Split('\\'))
        {
            var device = segment.Split('.')[0].ToUpperInvariant();
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') ||
                segment.EndsWith(' ') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                device is "CON" or "PRN" or "AUX" or "NUL" ||
                (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) ||
                 device.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(device[3])))
                throw new InvalidDataException("Unsafe path segment.");
        }
        var full = Path.GetFullPath(path);
        if (!string.Equals(full, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Noncanonical path.");
        RejectReparsePoints(full);
        return full;
    }

    public static void RejectReparsePoints(string path)
    {
        for (string? cursor = path; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse points are not permitted in update paths.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    public static bool Same(string left, string right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.OrdinalIgnoreCase);

    public static bool Within(string path, string root) =>
        Canonical(path).StartsWith(Canonical(root) + "\\", StringComparison.OrdinalIgnoreCase);

    public static void Validate(UpdateTransaction t, string transactionFile, string knownInstallRoot, bool requirePrepared = true)
    {
        if (t.FormatVersion != 1 || t.AppId != ProductInfo.AppId ||
            !Guid.TryParseExact(t.TransactionId, "N", out var id) || id == Guid.Empty ||
            t.TransactionId != id.ToString("N") || !Enum.IsDefined(t.State) ||
            (requirePrepared && t.State != UpdateTransactionState.Prepared) ||
            t.ProcessId <= 0 || t.ProcessStartUtcTicks <= 0 || t.ProcessStartUtcTicks > DateTime.UtcNow.Ticks ||
            t.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(1) || t.CreatedUtc < DateTimeOffset.UtcNow.AddDays(-7))
            throw new InvalidDataException("Invalid transaction identity, format, state, or lifetime.");
        if (t.CurrentVersion?.Length > 128 || t.TargetVersion?.Length > 128 ||
            !ReleaseVersion.TryParse(t.CurrentVersion, out var current) ||
            !ReleaseVersion.TryParse(t.TargetVersion, out var target) ||
            t.CurrentVersion != ProductInfo.InformationalVersion ||
            current!.IsDevelopment != target!.IsDevelopment ||
            (t.SyntheticTest ? target.CompareTo(current) != 0 : target.CompareTo(current) <= 0))
            throw new InvalidDataException("Invalid version transition.");
        var staging = Path.Combine(UpdatesRoot, t.TransactionId);
        if (!Same(t.InstallRoot, knownInstallRoot) ||
            !Same(t.InstalledExecutable, Path.Combine(knownInstallRoot, "CadenceStudio.exe")) ||
            !Same(t.RestartExecutable, t.InstalledExecutable) || !File.Exists(t.InstalledExecutable) ||
            !Same(t.StagingRoot, staging) || !Same(transactionFile, Path.Combine(staging, "transaction.json")) ||
            Same(t.InstallRoot, UpdatesRoot) || Within(t.InstallRoot, UpdatesRoot) || Within(UpdatesRoot, t.InstallRoot))
            throw new InvalidDataException("Installation or staging identity mismatch.");
        if (!Same(t.PayloadPath, Path.Combine(staging, "payload", "payload.zip")) ||
            !Same(t.SignaturePath, Path.Combine(staging, "payload", "payload.zip.sig")) ||
            !Same(t.ExtractionPath, Path.Combine(staging, "extracted")) ||
            !Same(t.BackupPath, Path.Combine(staging, "previous")))
            throw new InvalidDataException("Invalid reserved staging paths.");
    }
}
