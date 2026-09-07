using Microsoft.Win32;

namespace CadenceStudio.App.Services;

public static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Cadence Studio";

    private static RegistryView StartupRegistryView =>
        Environment.Is64BitOperatingSystem
            ? RegistryView.Registry64
            : RegistryView.Registry32;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var currentUser = RegistryKey.OpenBaseKey(
                    RegistryHive.CurrentUser,
                    StartupRegistryView);
                using var runKey = currentUser.OpenSubKey(RunKeyPath, writable: false);

                return runKey?.GetValue(
                           ValueName,
                           null,
                           RegistryValueOptions.DoNotExpandEnvironmentNames) is string command &&
                       !string.IsNullOrWhiteSpace(command);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var currentUser = RegistryKey.OpenBaseKey(
                RegistryHive.CurrentUser,
                StartupRegistryView);
            using var runKey = currentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException(
                    "Windows startup registry key could not be opened.");

            if (enabled)
            {
                var executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    throw new InvalidOperationException(
                        "Cadence Studio executable path could not be determined.");
                }

                var command = $"\"{executablePath}\"";

                runKey.SetValue(
                    ValueName,
                    command,
                    RegistryValueKind.String);
                runKey.Flush();

                var writtenValue = runKey.GetValue(
                    ValueName,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

                if (!string.Equals(writtenValue, command, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Windows startup registration could not be verified after writing it.");
                }
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
                runKey.Flush();

                if (runKey.GetValue(
                        ValueName,
                        null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) is not null)
                {
                    throw new InvalidOperationException(
                        "Windows startup registration could not be verified after removing it.");
                }
            }

            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
