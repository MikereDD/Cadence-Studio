namespace CadenceStudio.Core.Models;

public sealed class EqualizerSettings
{
    public const int BandCount = 10;
    public static readonly double[] FrequenciesHz = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public bool Enabled { get; set; }
    public string PresetName { get; set; } = "Flat";
    public double PreampDb { get; set; }
    public bool AutoHeadroomEnabled { get; set; } = true;
    public List<double> BandGainsDb { get; set; } = Enumerable.Repeat(0d, BandCount).ToList();

    public void Normalize()
    {
        PresetName = string.IsNullOrWhiteSpace(PresetName) ? "Custom" : PresetName.Trim();
        PreampDb = NormalizeGain(PreampDb);
        BandGainsDb ??= [];

        var normalized = new List<double>(BandCount);
        for (var index = 0; index < BandCount; index++)
        {
            normalized.Add(index < BandGainsDb.Count ? NormalizeGain(BandGainsDb[index]) : 0d);
        }

        BandGainsDb = normalized;
    }

    public EqualizerSettings Clone()
    {
        Normalize();
        return new EqualizerSettings
        {
            Enabled = Enabled,
            PresetName = PresetName,
            PreampDb = PreampDb,
            AutoHeadroomEnabled = AutoHeadroomEnabled,
            BandGainsDb = [.. BandGainsDb]
        };
    }

    public double EffectivePreampDb
    {
        get
        {
            Normalize();
            if (!Enabled)
            {
                return 0;
            }

            var highestBoost = BandGainsDb.Count == 0 ? 0 : Math.Max(0, BandGainsDb.Max());
            return AutoHeadroomEnabled ? PreampDb - highestBoost : PreampDb;
        }
    }

    private static double NormalizeGain(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, -12, 12) : 0;
}
