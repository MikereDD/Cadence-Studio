using System.IO;
using System.Windows;
using System.Windows.Threading;
using CadenceStudio.Core.Updates;

namespace CadenceStudio.App;

public partial class App
{
    private void App_StartupHealth(object sender, StartupEventArgs e)
    {
        if (!UpdateHealthHandshake.TryParseStartupArguments(e.Args, out var request, out var error))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                _logger?.Error("Updater startup-health handoff was rejected: " + error);
                RequestExit();
            }));
            return;
        }

        if (request is null) return;

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                if (MainWindow is null || !MainWindow.IsLoaded)
                    throw new InvalidDataException("Main window did not complete initialization.");
                UpdateHealthHandshake.ConfirmHealthy(request);
                _logger?.Info($"Confirmed updater startup health for transaction {request.TransactionId} and version {request.TargetVersion}.");
            }
            catch (Exception exception)
            {
                // Do not create a success marker on any failure. The updater owns
                // the bounded timeout and rollback decision for this transaction.
                _logger?.Error("Could not confirm updater startup health.", exception);
            }
        }));
    }
}
