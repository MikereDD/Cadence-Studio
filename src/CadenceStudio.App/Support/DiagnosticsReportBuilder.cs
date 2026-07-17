using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Core;
using CadenceStudio.Infrastructure;

namespace CadenceStudio.App.Support;

public static class DiagnosticsReportBuilder
{
    public static string Build(MainWindowViewModel viewModel, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(paths);

        using var process = Process.GetCurrentProcess();
        var builder = new StringBuilder();
        builder.AppendLine($"{ProductInfo.Name} diagnostics");
        builder.AppendLine($"Version: {ProductInfo.DisplayVersion}");
        builder.AppendLine($"Generated: {DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine("Runtime");
        builder.AppendLine($"  OS: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"  .NET: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"  Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"  64-bit process: {Environment.Is64BitProcess}");
        builder.AppendLine($"  Processor count: {Environment.ProcessorCount}");
        builder.AppendLine($"  Working set: {FormatBytes(process.WorkingSet64)}");
        builder.AppendLine($"  Network available: {NetworkInterface.GetIsNetworkAvailable()}");
        builder.AppendLine();
        builder.AppendLine("Cadence Studio state");
        builder.AppendLine($"  Workspace: {viewModel.ActiveSection}");
        builder.AppendLine($"  Theme: {viewModel.ThemeName}");
        builder.AppendLine($"  Typography: {viewModel.TypographyName} ({viewModel.TypographyFamilyName})");
        builder.AppendLine($"  Text size: {viewModel.TextSizeName}");
        builder.AppendLine($"  Now Playing mode: {(viewModel.IsNowPlayingExpanded ? "Expanded" : "Compact")}");
        builder.AppendLine($"  Queue tracks: {viewModel.Queue.Count}");
        builder.AppendLine($"  Current track: {viewModel.CurrentTrack?.FilePath ?? "None"}");
        builder.AppendLine($"  Library: {viewModel.IndexedTrackCountText}");
        builder.AppendLine($"  Library roots: {viewModel.LibraryRoots.Count}");
        builder.AppendLine();
        builder.AppendLine("Data paths");
        AppendPath(builder, "Data root", paths.Root, isDirectory: true);
        AppendPath(builder, "Logs", paths.Logs, isDirectory: true);
        AppendPath(builder, "Cache", paths.Cache, isDirectory: true);
        AppendPath(builder, "Session", paths.SessionFile, isDirectory: false);
        AppendPath(builder, "Library index", paths.LibraryIndexFile, isDirectory: false);
        AppendPath(builder, "Playlists", paths.PlaylistsFile, isDirectory: false);
        AppendPath(builder, "Log file", paths.LogFile, isDirectory: false);
        builder.AppendLine();
        builder.AppendLine("Cadence Studio is a separate modern desktop application from Cadence Classic.");
        return builder.ToString();
    }

    private static void AppendPath(StringBuilder builder, string label, string path, bool isDirectory)
    {
        if (isDirectory)
        {
            builder.AppendLine($"  {label}: {path} [{(Directory.Exists(path) ? "present" : "missing") }]");
            return;
        }

        if (!File.Exists(path))
        {
            builder.AppendLine($"  {label}: {path} [missing]");
            return;
        }

        try
        {
            builder.AppendLine($"  {label}: {path} [{FormatBytes(new FileInfo(path).Length)}]");
        }
        catch
        {
            builder.AppendLine($"  {label}: {path} [present]");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, bytes);
        var unitIndex = 0;
        var display = (double)value;
        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return $"{display:0.##} {units[unitIndex]}";
    }
}
