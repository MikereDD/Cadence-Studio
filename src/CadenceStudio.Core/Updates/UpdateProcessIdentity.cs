using System.Diagnostics;

namespace CadenceStudio.Core.Updates;

public static class UpdateProcessIdentity
{
    public static bool IsClosed(UpdateTransaction t)
    {
        Process process;
        try { process = Process.GetProcessById(t.ProcessId); }
        catch (ArgumentException) { return true; }
        using (process)
        {
            try
            {
                if (process.HasExited) return true;
                if (process.StartTime.ToUniversalTime().Ticks != t.ProcessStartUtcTicks ||
                    !UpdateTransactionPaths.Same(process.MainModule!.FileName!, t.InstalledExecutable))
                    throw new InvalidDataException("Process identity conflicts with the transaction.");
                return process.HasExited;
            }
            catch (InvalidOperationException) when (process.HasExited) { return true; }
            catch (System.ComponentModel.Win32Exception) when (process.HasExited) { return true; }
        }
    }
}
