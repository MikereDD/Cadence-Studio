using CadenceStudio.Core.Enums;

namespace CadenceStudio.Core.Models;

public sealed record PlaybackSnapshot(
    PlaybackState State,
    Track? CurrentTrack,
    TimeSpan Position,
    TimeSpan Duration,
    double Volume,
    string? ErrorMessage = null,
    string? FormatDescription = null)
{
    public static PlaybackSnapshot Empty(double volume = 0.72) =>
        new(PlaybackState.Stopped, null, TimeSpan.Zero, TimeSpan.Zero, volume);
}
