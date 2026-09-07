using CadenceStudio.App.Mvvm;
using CadenceStudio.App.Services;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _startWithWindows;

    public RelayCommand ToggleStartWithWindowsCommand { get; private set; } = null!;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        private set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                OnPropertyChanged(nameof(StartWithWindowsLabel));
                OnPropertyChanged(nameof(StartWithWindowsDescription));
            }
        }
    }

    public string StartWithWindowsLabel => StartWithWindows
        ? "Start with Windows on"
        : "Start with Windows off";

    public string StartWithWindowsDescription => StartWithWindows
        ? "Cadence Studio will launch automatically when you sign in to Windows."
        : "Cadence Studio will stay closed until you launch it yourself.";

    private void InitializeSystemIntegrationState(bool startWithWindows)
    {
        if (WindowsStartupService.TrySetEnabled(startWithWindows, out var error))
        {
            _startWithWindows = startWithWindows;
            return;
        }

        _startWithWindows = WindowsStartupService.IsEnabled;
        _settings.StartWithWindows = _startWithWindows;
        _logger.Error(
            "Windows startup registration could not be synchronized.",
            new InvalidOperationException(error ?? "Unknown Windows startup registration error."));
    }

    private void InitializeSystemIntegrationCommands()
    {
        ToggleStartWithWindowsCommand = new RelayCommand(() =>
        {
            var requested = !StartWithWindows;
            if (!WindowsStartupService.TrySetEnabled(requested, out var error))
            {
                StatusText = string.IsNullOrWhiteSpace(error)
                    ? "Windows startup could not be updated."
                    : $"Windows startup could not be updated: {error}";
                return;
            }

            StartWithWindows = requested;
            _settings.StartWithWindows = requested;
            StatusText = requested
                ? "Cadence Studio will start automatically when you sign in to Windows."
                : "Cadence Studio will no longer start automatically with Windows.";
        });
    }
}
