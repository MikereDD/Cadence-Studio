using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface IPlaybackService : IDisposable
{
    bool IsPlaybackAvailable { get; }
    PlaybackSnapshot Snapshot { get; }

    event EventHandler<PlaybackSnapshot>? SnapshotChanged;
    event EventHandler? PlaybackEnded;
    event EventHandler<VisualizerFrame>? VisualizerFrameReady;

    Task OpenAsync(Track track, CancellationToken cancellationToken = default);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);
    void PreparePaused(TimeSpan position);
    void SetVolume(double volume);
    void SetEqualizer(EqualizerSettings settings);
    void SetVisualizer(VisualizerSettings settings);
}
