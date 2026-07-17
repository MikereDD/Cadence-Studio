using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CadenceStudio.App.Services;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Core;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Infrastructure;
using CadenceStudio.Infrastructure.Services;

namespace CadenceStudio.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CadenceStudio.SingleInstance";

    private IAppLogger? _logger;
    private MainWindowViewModel? _mainViewModel;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var startupTimer = Stopwatch.StartNew();
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show(
                "Cadence Studio is already running.",
                ProductInfo.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        _logger = new FileAppLogger(paths);
        var staleTemporaryFilesRemoved = paths.CleanupStaleTemporaryFiles();
        if (staleTemporaryFilesRemoved > 0)
        {
            _logger.Info($"Removed {staleTemporaryFilesRemoved} stale temporary file(s) during startup.");
        }

        var sessionService = new JsonSessionService(paths, _logger);
        var playbackService = new NAudioPlaybackService(_logger);
        var metadataService = new TagLibMetadataService(_logger);
        var enrichmentService = new MusicEnrichmentService(paths, _logger);
        var libraryService = new JsonLibraryService(paths, metadataService, _logger);
        var playlistService = new JsonPlaylistService(paths, _logger);
        var folderPicker = new FolderPickerService();
        var filePicker = new AudioFilePickerService();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _logger.Info($"Starting {ProductInfo.Name} {ProductInfo.DisplayVersion}. PID={Environment.ProcessId}.");

        var sessionTimer = Stopwatch.StartNew();
        var settings = sessionService.LoadAsync().GetAwaiter().GetResult();
        sessionTimer.Stop();
        ThemeManager.Apply(settings.Theme);
        TypographyManager.Apply(settings.Typography, settings.TextSize);
        _mainViewModel = new MainWindowViewModel(
            playbackService,
            metadataService,
            enrichmentService,
            libraryService,
            playlistService,
            sessionService,
            folderPicker,
            filePicker,
            _logger,
            settings);

        MainWindow = new MainWindow(_mainViewModel);
        MainWindow.Show();
        _logger.Info($"Main window shown. StartupMs={startupTimer.ElapsedMilliseconds}, SessionLoadMs={sessionTimer.ElapsedMilliseconds}.");

        _ = _mainViewModel.InitializeSessionPlaybackAsync();
        _ = _mainViewModel.InitializeLibraryAsync();
        _ = _mainViewModel.InitializePlaylistsAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mainViewModel?.Shutdown();
            _logger?.Info("Cadence Studio shutdown completed.");
        }
        catch (Exception exception)
        {
            _logger?.Error("Shutdown persistence failed.", exception);
        }
        finally
        {
            if (_ownsSingleInstanceMutex)
            {
                try
                {
                    _singleInstanceMutex?.ReleaseMutex();
                }
                catch
                {
                    // The process is already exiting; mutex release is best effort.
                }
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            "Cadence Studio encountered an unexpected error. Details were written to the application log.",
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled application exception.", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
