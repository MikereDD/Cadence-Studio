using System.ComponentModel;
using System.Diagnostics;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using Icon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CadenceStudio.App.Services;
using CadenceStudio.App.ViewModels;
using CadenceStudio.Core;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Infrastructure;
using CadenceStudio.Infrastructure.Services;
using Forms = System.Windows.Forms;

namespace CadenceStudio.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CadenceStudio.SingleInstance";
    private const string ActivationPipeName = "CadenceStudio.Activation";

    private IAppLogger? _logger;
    private MainWindowViewModel? _mainViewModel;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayDrawingIcon;
    private CancellationTokenSource? _activationListenerCancellation;
    private bool _isExiting;

    public bool IsExiting => _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        var startupTimer = Stopwatch.StartNew();
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            SignalExistingInstance();
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
        InitializeTrayIcon();
        StartActivationListener();
        _logger.Info($"Main window shown. StartupMs={startupTimer.ElapsedMilliseconds}, SessionLoadMs={sessionTimer.ElapsedMilliseconds}.");

        _ = _mainViewModel.InitializeSessionPlaybackAsync();
        _ = _mainViewModel.InitializeLibraryAsync();
        _ = _mainViewModel.InitializePlaylistsAsync();
    }

    public void RestoreMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow window)
            {
                window.RestoreFromTray();
            }
        });
    }

    public void RequestExit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        Dispatcher.BeginInvoke(new Action(Shutdown));
    }

    private void InitializeTrayIcon()
    {
        _trayDrawingIcon = LoadTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayDrawingIcon,
            Text = ProductInfo.Name,
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        var openItem = menu.Items.Add("Open Cadence Studio", null, (_, _) => RestoreMainWindow());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Previous", null, (_, _) => Dispatcher.Invoke(() => _mainViewModel?.PreviousCommand.Execute(null)));
        menu.Items.Add("Play / Pause", null, (_, _) => Dispatcher.Invoke(() => _mainViewModel?.TogglePlaybackCommand.Execute(null)));
        menu.Items.Add("Next", null, (_, _) => Dispatcher.Invoke(() => _mainViewModel?.NextCommand.Execute(null)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Cadence Studio", null, (_, _) => RequestExit());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => RestoreMainWindow();

        if (_mainViewModel is not null)
        {
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            UpdateTrayTooltip();
        }
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "cadence-studio.ico");
        if (File.Exists(iconPath))
        {
            return new Icon(iconPath);
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            var extracted = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.NowPlayingTitle) or nameof(MainWindowViewModel.NowPlayingArtist))
        {
            UpdateTrayTooltip();
        }
    }

    private void UpdateTrayTooltip()
    {
        if (_trayIcon is null || _mainViewModel is null)
        {
            return;
        }

        var title = _mainViewModel.NowPlayingTitle;
        var artist = _mainViewModel.NowPlayingArtist;
        var text = title == "Nothing playing"
            ? ProductInfo.Name
            : $"{ProductInfo.Name} • {artist} — {title}";

        // NotifyIcon tooltip text is intentionally bounded for broad Windows compatibility.
        _trayIcon.Text = text.Length <= 63 ? text : text[..60] + "...";
    }

    private void StartActivationListener()
    {
        _activationListenerCancellation = new CancellationTokenSource();
        _ = ListenForActivationRequestsAsync(_activationListenerCancellation.Token);
    }

    private async Task ListenForActivationRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    ActivationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync();
                if (string.Equals(message, "activate", StringComparison.Ordinal))
                {
                    await Dispatcher.InvokeAsync(RestoreMainWindow);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger?.Error("Single-instance activation listener failed.", exception);
                await Task.Delay(250, cancellationToken);
            }
        }
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", ActivationPipeName, PipeDirection.Out);
                client.Connect(500);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine("activate");
                return;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(150);
            }
            catch (IOException)
            {
                Thread.Sleep(150);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isExiting = true;

        try
        {
            _activationListenerCancellation?.Cancel();
            if (_mainViewModel is not null)
            {
                _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
            }

            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayDrawingIcon?.Dispose();
            _trayDrawingIcon = null;

            _mainViewModel?.Shutdown();
            _logger?.Info("Cadence Studio shutdown completed.");
        }
        catch (Exception exception)
        {
            _logger?.Error("Shutdown persistence failed.", exception);
        }
        finally
        {
            _activationListenerCancellation?.Dispose();
            _activationListenerCancellation = null;

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
