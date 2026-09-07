using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CadenceStudio.App.Support;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Core;
using CadenceStudio.Infrastructure;
using CadenceStudio.Infrastructure.Services;

namespace CadenceStudio.App;

public partial class AboutDialog : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AppPaths _paths;
    private readonly UpdateDiscoveryService _updateDiscoveryService = new();
    private CancellationTokenSource? _updateCheckCancellation;
    private CadenceStudio.Core.Updates.UpdateCheckResult? _eligibleUpdate;

    public AboutDialog(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _paths = new AppPaths();
        InitializeComponent();
        DataContext = viewModel;

        ReleaseStageText.Text = string.Equals(
            ProductInfo.UpdateChannel,
            "development",
            StringComparison.Ordinal)
            ? "DEVELOPMENT BUILD"
            : "STABLE RELEASE";

        UpdatePolicyText.Text =
            $"{Capitalize(ProductInfo.UpdateChannel)} channel • " +
            $"manifest schema {ProductInfo.ReleaseManifestSchemaVersion} • " +
            $"updater protocol {ProductInfo.UpdaterProtocolVersion}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        _updateCheckCancellation = null;
        base.OnClosed(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => OpenPath(_paths.Logs);
    private void OpenCache_Click(object sender, RoutedEventArgs e) => OpenPath(_paths.Cache);
    private void OpenData_Click(object sender, RoutedEventArgs e) => OpenPath(_paths.Root);

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        _updateCheckCancellation = new CancellationTokenSource();

        UpdateCheckButton.IsEnabled = false;
        DryRunButton.IsEnabled = false;
        _eligibleUpdate = null;
        UpdateStatusText.Text =
            $"Checking the approved {ProductInfo.UpdateChannel} manifest endpoint...";

        try
        {
            var result = await _updateDiscoveryService.CheckForUpdatesAsync(
                _updateCheckCancellation.Token);

            if (IsLoaded)
            {
                UpdateStatusText.Text = result.Message;
                _eligibleUpdate = result;
            }
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded)
            {
                UpdateStatusText.Text = "Update check canceled.";
            }
        }
        finally
        {
            if (IsLoaded)
            {
                UpdateCheckButton.IsEnabled = true;
                DryRunButton.IsEnabled = true;
            }
        }
    }

    private async void DryRun_Click(object sender, RoutedEventArgs e)
    {
        DryRunButton.IsEnabled = false;
        UpdateCheckButton.IsEnabled = false;
        try
        {
            UpdateStatusText.Text = "Testing updater handoff...";
            UpdateStatusText.Text = await Services.UpdateDryRunService.RunAsync(_eligibleUpdate);
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = $"Dry run failed: {exception.Message}";
        }
        finally
        {
            DryRunButton.IsEnabled = true;
            UpdateCheckButton.IsEnabled = true;
        }
    }
    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiagnosticsReportBuilder.Build(_viewModel, _paths));
            DiagnosticsStatusText.Text = "Diagnostics copied to the clipboard.";
        }
        catch (Exception exception)
        {
            DiagnosticsStatusText.Text = $"Could not copy diagnostics: {exception.Message}";
        }
    }

    private void ViewNotices_Click(object sender, RoutedEventArgs e)
    {
        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (!File.Exists(noticesPath))
        {
            MessageBox.Show(this, "The third-party notices file was not found beside the application executable.",
                "Cadence Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenPath(noticesPath);
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            DiagnosticsStatusText.Text = $"Opened {path}";
        }
        catch (Exception exception)
        {
            DiagnosticsStatusText.Text = $"Could not open path: {exception.Message}";
        }
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
}
