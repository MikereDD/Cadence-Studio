namespace CadenceStudio.Core.Models;

public sealed class VisualizerSettings
{
    private static readonly int[] SupportedBarCounts = [24, 32, 48];

    public bool Enabled { get; set; } = true;
    public double Sensitivity { get; set; } = 1.0;
    public double Decay { get; set; } = 0.82;
    public int BarCount { get; set; } = 32;

    public VisualizerSettings Clone() => new()
    {
        Enabled = Enabled,
        Sensitivity = Sensitivity,
        Decay = Decay,
        BarCount = BarCount
    };

    public void Normalize()
    {
        Sensitivity = double.IsFinite(Sensitivity) ? Math.Clamp(Sensitivity, 0.5, 2.0) : 1.0;
        Decay = double.IsFinite(Decay) ? Math.Clamp(Decay, 0.55, 0.96) : 0.82;
        BarCount = SupportedBarCounts
            .OrderBy(value => Math.Abs(value - BarCount))
            .First();
    }
}
