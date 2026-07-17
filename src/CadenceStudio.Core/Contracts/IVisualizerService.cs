using CadenceStudio.Core.Enums;

namespace CadenceStudio.Core.Contracts;

public interface IVisualizerService
{
    bool IsConnected { get; }
    VisualizerPalette Palette { get; set; }
    event EventHandler<VisualizerFrame>? FrameReady;
}

public sealed record VisualizerFrame(DateTimeOffset Timestamp, IReadOnlyList<float> Bands);
