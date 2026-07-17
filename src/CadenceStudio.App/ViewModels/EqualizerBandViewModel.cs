using CadenceStudio.App.Mvvm;

namespace CadenceStudio.App.ViewModels;

public sealed class EqualizerBandViewModel : ObservableObject
{
    private readonly Action<EqualizerBandViewModel> _changed;
    private double _gainDb;

    public EqualizerBandViewModel(double frequencyHz, string label, double gainDb, Action<EqualizerBandViewModel> changed)
    {
        FrequencyHz = frequencyHz;
        Label = label;
        _gainDb = gainDb;
        _changed = changed;
    }

    public double FrequencyHz { get; }
    public string Label { get; }

    public double GainDb
    {
        get => _gainDb;
        set
        {
            var normalized = Math.Round(Math.Clamp(value, -12, 12), 1);
            if (SetProperty(ref _gainDb, normalized))
            {
                OnPropertyChanged(nameof(GainText));
                _changed(this);
            }
        }
    }

    public string GainText => $"{GainDb:+0.0;-0.0;0.0} dB";

    public void SetGainSilently(double gainDb)
    {
        var normalized = Math.Round(Math.Clamp(gainDb, -12, 12), 1);
        if (Math.Abs(_gainDb - normalized) < 0.001)
        {
            return;
        }

        _gainDb = normalized;
        OnPropertyChanged(nameof(GainDb));
        OnPropertyChanged(nameof(GainText));
    }
}
