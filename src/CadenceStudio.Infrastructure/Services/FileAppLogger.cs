using System.Globalization;
using System.Text;
using CadenceStudio.Core.Contracts;

namespace CadenceStudio.Infrastructure.Services;

public sealed class FileAppLogger : IAppLogger
{
    private const long MaximumLogBytes = 4L * 1024L * 1024L;
    private readonly AppPaths _paths;
    private readonly object _sync = new();

    public FileAppLogger(AppPaths paths)
    {
        _paths = paths;
        TryRotateLog();
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
            {
                line.AppendLine().Append(exception);
            }

            lock (_sync)
            {
                TryRotateLog();
                File.AppendAllText(_paths.LogFile, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the player.
        }
    }

    private void TryRotateLog()
    {
        try
        {
            if (!File.Exists(_paths.LogFile) || new FileInfo(_paths.LogFile).Length < MaximumLogBytes)
            {
                return;
            }

            var archivedLog = Path.Combine(
                _paths.Logs,
                $"cadence-studio-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.Move(_paths.LogFile, archivedLog, overwrite: true);

            var oldArchives = Directory
                .EnumerateFiles(_paths.Logs, "cadence-studio-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(4)
                .ToArray();

            foreach (var oldArchive in oldArchives)
            {
                try
                {
                    File.Delete(oldArchive);
                }
                catch
                {
                    // Best-effort retention cleanup.
                }
            }
        }
        catch
        {
            // Rotation failure must never prevent logging or startup.
        }
    }
}
