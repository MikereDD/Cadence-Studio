using System.Collections.ObjectModel;
using CadenceStudio.App.Mvvm;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isEqualizerEnabled;
    private string _equalizerPresetName = "Flat";
    private double _equalizerPreampDb;
    private bool _isEqualizerAutoHeadroomEnabled = true;
    private bool _suppressEqualizerUpdates;

    public ObservableCollection<EqualizerBandViewModel> EqualizerBands { get; } = [];

    public RelayCommand ToggleEqualizerCommand { get; private set; } = null!;
    public RelayCommand ToggleEqualizerHeadroomCommand { get; private set; } = null!;
    public RelayCommand ResetEqualizerCommand { get; private set; } = null!;
    public RelayCommand<string> ApplyEqualizerPresetCommand { get; private set; } = null!;

    public bool IsEqualizerEnabled
    {
        get => _isEqualizerEnabled;
        set
        {
            if (SetProperty(ref _isEqualizerEnabled, value))
            {
                OnPropertyChanged(nameof(EqualizerToggleLabel));
                ApplyEqualizerToPlayback();
                StatusText = value ? $"Equalizer enabled • {EqualizerPresetName}" : "Equalizer bypassed";
            }
        }
    }

    public string EqualizerToggleLabel => IsEqualizerEnabled ? "EQ enabled" : "EQ bypassed";

    public string EqualizerPresetName
    {
        get => _equalizerPresetName;
        private set
        {
            if (SetProperty(ref _equalizerPresetName, value))
            {
                OnPropertyChanged(nameof(EqualizerStatusText));
            }
        }
    }

    public double EqualizerPreampDb
    {
        get => _equalizerPreampDb;
        set
        {
            var normalized = Math.Round(Math.Clamp(value, -12, 12), 1);
            if (SetProperty(ref _equalizerPreampDb, normalized))
            {
                OnPropertyChanged(nameof(EqualizerPreampText));
                MarkEqualizerCustomAndApply();
            }
        }
    }

    public string EqualizerPreampText => $"{EqualizerPreampDb:+0.0;-0.0;0.0} dB";

    public bool IsEqualizerAutoHeadroomEnabled
    {
        get => _isEqualizerAutoHeadroomEnabled;
        set
        {
            if (SetProperty(ref _isEqualizerAutoHeadroomEnabled, value))
            {
                ApplyEqualizerToPlayback();
                StatusText = value ? "Equalizer auto headroom enabled" : "Equalizer auto headroom disabled";
            }
        }
    }

    public string EqualizerHeadroomLabel => IsEqualizerAutoHeadroomEnabled ? "Auto headroom on" : "Auto headroom off";

    public string EqualizerEffectivePreampText
    {
        get
        {
            var settings = BuildEqualizerSettings();
            return settings.Enabled
                ? $"Effective preamp {settings.EffectivePreampDb:+0.0;-0.0;0.0} dB"
                : "Equalizer bypassed";
        }
    }

    public string EqualizerStatusText => IsEqualizerEnabled
        ? $"{EqualizerPresetName} • {EqualizerEffectivePreampText}"
        : "Bypassed • No equalizer processing is applied";

    private void InitializeEqualizerState(EqualizerSettings? settings)
    {
        settings ??= new EqualizerSettings();
        settings.Normalize();

        _isEqualizerEnabled = settings.Enabled;
        _equalizerPresetName = settings.PresetName;
        _equalizerPreampDb = settings.PreampDb;
        _isEqualizerAutoHeadroomEnabled = settings.AutoHeadroomEnabled;

        var labels = new[] { "31 Hz", "62 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz" };
        for (var index = 0; index < EqualizerSettings.BandCount; index++)
        {
            EqualizerBands.Add(new EqualizerBandViewModel(
                EqualizerSettings.FrequenciesHz[index],
                labels[index],
                settings.BandGainsDb[index],
                OnEqualizerBandChanged));
        }
    }

    private void InitializeEqualizerCommands()
    {
        ToggleEqualizerCommand = new RelayCommand(() => IsEqualizerEnabled = !IsEqualizerEnabled);
        ToggleEqualizerHeadroomCommand = new RelayCommand(() => IsEqualizerAutoHeadroomEnabled = !IsEqualizerAutoHeadroomEnabled);
        ResetEqualizerCommand = new RelayCommand(() => ApplyEqualizerPreset("Flat"));
        ApplyEqualizerPresetCommand = new RelayCommand<string>(ApplyEqualizerPreset);
    }

    private void OnEqualizerBandChanged(EqualizerBandViewModel band)
    {
        if (_suppressEqualizerUpdates)
        {
            return;
        }

        EqualizerPresetName = "Custom";
        ApplyEqualizerToPlayback();
    }

    private void MarkEqualizerCustomAndApply()
    {
        if (_suppressEqualizerUpdates)
        {
            return;
        }

        EqualizerPresetName = "Custom";
        ApplyEqualizerToPlayback();
    }

    private void ApplyEqualizerPreset(string? presetName)
    {
        var normalized = string.IsNullOrWhiteSpace(presetName) ? "Flat" : presetName.Trim();
        var (preamp, gains) = normalized switch
        {
            "Bass Boost" => (0d, new double[] { 6, 5, 4, 2, 0, -1, -1, 0, 1, 2 }),
            "Treble Boost" => (0d, new double[] { -2, -1, 0, 0, 1, 2, 3, 4, 5, 6 }),
            "Vocal" => (0d, new double[] { -2, -1, 0, 1, 3, 4, 3, 1, 0, -1 }),
            "Rock" => (0d, new double[] { 4, 3, 2, 0, -1, -1, 1, 3, 4, 4 }),
            "Electronic" => (0d, new double[] { 5, 4, 1, 0, -1, 1, 2, 3, 4, 5 }),
            "Classical" => (0d, new double[] { 0, 0, -1, -1, 0, 2, 3, 3, 2, 1 }),
            "Night" => (-2d, new double[] { -4, -3, -2, 0, 2, 3, 2, 0, -2, -3 }),
            _ => (0d, new double[EqualizerSettings.BandCount])
        };

        _suppressEqualizerUpdates = true;
        try
        {
            EqualizerPresetName = normalized == "Custom" ? "Flat" : normalized;
            _equalizerPreampDb = preamp;
            OnPropertyChanged(nameof(EqualizerPreampDb));
            OnPropertyChanged(nameof(EqualizerPreampText));

            for (var index = 0; index < EqualizerBands.Count; index++)
            {
                EqualizerBands[index].SetGainSilently(gains[index]);
            }

            _isEqualizerEnabled = true;
            OnPropertyChanged(nameof(IsEqualizerEnabled));
            OnPropertyChanged(nameof(EqualizerToggleLabel));
        }
        finally
        {
            _suppressEqualizerUpdates = false;
        }

        ApplyEqualizerToPlayback();
        StatusText = $"Equalizer preset applied • {EqualizerPresetName}";
    }

    private EqualizerSettings BuildEqualizerSettings()
    {
        var settings = new EqualizerSettings
        {
            Enabled = IsEqualizerEnabled,
            PresetName = EqualizerPresetName,
            PreampDb = EqualizerPreampDb,
            AutoHeadroomEnabled = IsEqualizerAutoHeadroomEnabled,
            BandGainsDb = EqualizerBands.Select(band => band.GainDb).ToList()
        };
        settings.Normalize();
        return settings;
    }

    private void ApplyEqualizerToPlayback()
    {
        if (_suppressEqualizerUpdates || _disposed)
        {
            return;
        }

        _playbackService.SetEqualizer(BuildEqualizerSettings());
        OnPropertyChanged(nameof(EqualizerHeadroomLabel));
        OnPropertyChanged(nameof(EqualizerEffectivePreampText));
        OnPropertyChanged(nameof(EqualizerStatusText));
    }
}
