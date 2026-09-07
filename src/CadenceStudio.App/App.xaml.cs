using System.ComponentModel;
using System.Diagnostics;
using DrawingColor = System.Drawing.Color;
using DrawingSystemColors = System.Drawing.SystemColors;
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
using Microsoft.Win32;

namespace CadenceStudio.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\CadenceStudio.SingleInstance";
    private const string ActivationPipeName = "CadenceStudio.Activation";
    private const string ThemePersonalizeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private IAppLogger? _logger;
    private MainWindowViewModel? _mainViewModel;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ContextMenuStrip? _trayMenu;
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

        _trayMenu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Padding = new Forms.Padding(5, 5, 5, 5),
            DropShadowEnabled = true
        };

        var openItem = _trayMenu.Items.Add("Open Cadence Studio", null, (_, _) => RestoreMainWindow());
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);

        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var previousItem = _trayMenu.Items.Add(
            "Previous",
            null,
            (_, _) => Dispatcher.Invoke(() => _mainViewModel?.PreviousCommand.Execute(null)));

        var playPauseItem = _trayMenu.Items.Add(
            "Play / Pause",
            null,
            (_, _) => Dispatcher.Invoke(() => _mainViewModel?.TogglePlaybackCommand.Execute(null)));

        var nextItem = _trayMenu.Items.Add(
            "Next",
            null,
            (_, _) => Dispatcher.Invoke(() => _mainViewModel?.NextCommand.Execute(null)));

        _trayMenu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = _trayMenu.Items.Add("Exit Cadence Studio", null, (_, _) => RequestExit());

        foreach (var item in new[] { openItem, previousItem, playPauseItem, nextItem, exitItem })
        {
            item.Padding = new Forms.Padding(8, 4, 14, 4);
        }

        ApplyTrayMenuTheme();
        _trayMenu.Opening += (_, _) => ApplyTrayMenuTheme();

        _trayIcon.ContextMenuStrip = _trayMenu;
        _trayIcon.DoubleClick += (_, _) => RestoreMainWindow();

        if (_mainViewModel is not null)
        {
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            UpdateTrayTooltip();
        }
    }

    private void ApplyTrayMenuTheme()
    {
        if (_trayMenu is null)
        {
            return;
        }

        if (ShouldUseDarkTrayMenu())
        {
            var background = DrawingColor.FromArgb(30, 30, 32);
            var foreground = DrawingColor.FromArgb(242, 242, 244);

            _trayMenu.BackColor = background;
            _trayMenu.ForeColor = foreground;
            _trayMenu.Renderer = new CadenceTrayRenderer(new DarkTrayColorTable());

            foreach (Forms.ToolStripItem item in _trayMenu.Items)
            {
                item.BackColor = background;
                item.ForeColor = foreground;
            }

            return;
        }

        _trayMenu.BackColor = DrawingSystemColors.Menu;
        _trayMenu.ForeColor = DrawingSystemColors.MenuText;
        _trayMenu.Renderer = new Forms.ToolStripSystemRenderer();

        foreach (Forms.ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = DrawingSystemColors.Menu;
            item.ForeColor = DrawingSystemColors.MenuText;
        }
    }

    private static bool ShouldUseDarkTrayMenu()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(ThemePersonalizeRegistryPath);
            var value = personalize?.GetValue("AppsUseLightTheme");

            return value switch
            {
                int appsUseLightTheme => appsUseLightTheme == 0,
                _ => true
            };
        }
        catch
        {
            // A dark fallback avoids a glaring white context menu when theme
            // detection is unavailable or blocked.
            return true;
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
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(cancellationToken);
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

                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
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
                _trayIcon.ContextMenuStrip = null;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayMenu?.Dispose();
            _trayMenu = null;

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

    private sealed class DarkTrayColorTable : Forms.ProfessionalColorTable
    {
        private static readonly DrawingColor Background = DrawingColor.FromArgb(29, 29, 32);
        private static readonly DrawingColor Selected = DrawingColor.FromArgb(49, 49, 54);
        private static readonly DrawingColor Border = DrawingColor.FromArgb(64, 61, 57);
        private static readonly DrawingColor Separator = DrawingColor.FromArgb(56, 56, 61);

        public DarkTrayColorTable()
        {
            UseSystemColors = false;
        }

        public override DrawingColor ToolStripDropDownBackground => Background;
        public override DrawingColor ImageMarginGradientBegin => Background;
        public override DrawingColor ImageMarginGradientMiddle => Background;
        public override DrawingColor ImageMarginGradientEnd => Background;
        public override DrawingColor MenuBorder => Border;
        public override DrawingColor ToolStripBorder => Border;
        public override DrawingColor MenuItemBorder => Border;
        public override DrawingColor MenuItemSelected => Selected;
        public override DrawingColor MenuItemSelectedGradientBegin => Selected;
        public override DrawingColor MenuItemSelectedGradientEnd => Selected;
        public override DrawingColor MenuItemPressedGradientBegin => Selected;
        public override DrawingColor MenuItemPressedGradientMiddle => Selected;
        public override DrawingColor MenuItemPressedGradientEnd => Selected;
        public override DrawingColor SeparatorDark => Separator;
        public override DrawingColor SeparatorLight => Separator;
    }

    private sealed class CadenceTrayRenderer : Forms.ToolStripProfessionalRenderer
    {
        private static readonly DrawingColor MenuBorder = DrawingColor.FromArgb(68, 64, 58);
        private static readonly DrawingColor HoverFill = DrawingColor.FromArgb(50, 50, 55);
        private static readonly DrawingColor HoverBorder = DrawingColor.FromArgb(88, 76, 55);
        private static readonly DrawingColor Accent = DrawingColor.FromArgb(176, 137, 70);
        private static readonly DrawingColor Separator = DrawingColor.FromArgb(58, 58, 63);

        public CadenceTrayRenderer(Forms.ProfessionalColorTable colorTable)
            : base(colorTable)
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new System.Drawing.Pen(MenuBorder);
            var bounds = new System.Drawing.Rectangle(
                0,
                0,
                Math.Max(0, e.ToolStrip.Width - 1),
                Math.Max(0, e.ToolStrip.Height - 1));
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            var left = 10;
            var right = Math.Max(left, e.Item.Width - 10);

            using var pen = new System.Drawing.Pen(Separator);
            e.Graphics.DrawLine(pen, left, y, right, y);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }

            var bounds = new System.Drawing.Rectangle(
                3,
                1,
                Math.Max(0, e.Item.Width - 6),
                Math.Max(0, e.Item.Height - 2));

            using var fill = new System.Drawing.SolidBrush(HoverFill);
            using var border = new System.Drawing.Pen(HoverBorder);
            using var accent = new System.Drawing.SolidBrush(Accent);

            e.Graphics.FillRectangle(fill, bounds);
            e.Graphics.DrawRectangle(border, bounds);
            e.Graphics.FillRectangle(
                accent,
                bounds.Left,
                bounds.Top + 2,
                2,
                Math.Max(0, bounds.Height - 3));
        }
    }
}
