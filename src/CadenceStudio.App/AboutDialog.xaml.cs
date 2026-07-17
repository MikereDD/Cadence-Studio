using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows;
using CadenceStudio.App.Support;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Infrastructure;

namespace CadenceStudio.App;

public partial class AboutDialog : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AppPaths _paths;

    public AboutDialog(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _paths = new AppPaths();
        InitializeComponent();
        DataContext = viewModel;
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
}
