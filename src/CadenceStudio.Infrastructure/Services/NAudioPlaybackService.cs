using CadenceStudio.Core.Contracts;
using CorePlaybackState = CadenceStudio.Core.Enums.PlaybackState;
using NAudioPlaybackState = NAudio.Wave.PlaybackState;
using CadenceStudio.Core.Models;
using NAudio.Wave;

namespace CadenceStudio.Infrastructure.Services;

public sealed class NAudioPlaybackService : IPlaybackService
{
    private readonly object _sync = new();
    private readonly IAppLogger _logger;
    private readonly Timer _positionTimer;

    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioReader;
    private EqualizerSampleProvider? _equalizerProvider;
    private SpectrumSampleProvider? _spectrumProvider;
    private EqualizerSettings _equalizerSettings = new();
    private VisualizerSettings _visualizerSettings = new();
    private PlaybackSnapshot _snapshot = PlaybackSnapshot.Empty();
    private bool _disposed;

    public NAudioPlaybackService(IAppLogger logger)
    {
        _logger = logger;
        _positionTimer = new Timer(UpdatePosition, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsPlaybackAvailable => true;

    public PlaybackSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<PlaybackSnapshot>? SnapshotChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<VisualizerFrame>? VisualizerFrameReady;

    public Task OpenAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();

        PlaybackSnapshot snapshot;

        lock (_sync)
        {
            ThrowIfDisposed();
            DisposePlaybackGraph();

            if (!File.Exists(track.FilePath))
            {
                snapshot = SetFaultedSnapshot(track, $"Audio file not found: {track.FileName}");
            }
            else
            {
                try
                {
                    var reader = new AudioFileReader(track.FilePath);
                    var output = new WaveOutEvent
                    {
                        DesiredLatency = 120,
                        NumberOfBuffers = 3
                    };

                    var equalizer = new EqualizerSampleProvider(reader, _equalizerSettings);
                    var spectrum = new SpectrumSampleProvider(equalizer, _visualizerSettings);

                    _audioReader = reader;
                    _equalizerProvider = equalizer;
                    _spectrumProvider = spectrum;
                    _outputDevice = output;
                    output.PlaybackStopped += OnPlaybackStopped;
                    spectrum.FrameReady += OnVisualizerFrameReady;
                    output.Init(spectrum);
                    output.Volume = (float)Math.Clamp(_snapshot.Volume, 0, 1);

                    var hydratedTrack = track with
                    {
                        Duration = reader.TotalTime,
                        SampleRateHz = track.SampleRateHz > 0 ? track.SampleRateHz : reader.WaveFormat.SampleRate,
                        Channels = track.Channels > 0 ? track.Channels : reader.WaveFormat.Channels
                    };
                    _snapshot = new PlaybackSnapshot(
                        CorePlaybackState.Stopped,
                        hydratedTrack,
                        TimeSpan.Zero,
                        reader.TotalTime,
                        _snapshot.Volume,
                        null,
                        hydratedTrack.FormatDescription);

                    _positionTimer.Change(0, 250);
                    snapshot = _snapshot;
                    _logger.Info($"Opened audio file: {track.FilePath}");
                }
                catch (Exception exception)
                {
                    DisposePlaybackGraph();
                    snapshot = SetFaultedSnapshot(track, $"Could not open {track.FileName}: {exception.Message}");
                    _logger.Error($"Audio open failed: {track.FilePath}", exception);
                }
            }
        }

        Publish(snapshot);
        return Task.CompletedTask;
    }

    public void Play()
    {
        PlaybackSnapshot snapshot;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_outputDevice is null || _audioReader is null)
            {
                snapshot = _snapshot with
                {
                    State = CorePlaybackState.Stopped,
                    ErrorMessage = "Open a music file before pressing play."
                };
                _snapshot = snapshot;
            }
            else
            {
                try
                {
                    if (_audioReader.TotalTime > TimeSpan.Zero &&
                        _audioReader.CurrentTime >= _audioReader.TotalTime - TimeSpan.FromMilliseconds(150))
                    {
                        _audioReader.CurrentTime = TimeSpan.Zero;
                    }

                    _outputDevice.Play();
                    _snapshot = _snapshot with
                    {
                        State = CorePlaybackState.Playing,
                        Position = _audioReader.CurrentTime,
                        ErrorMessage = null
                    };
                    snapshot = _snapshot;
                    _positionTimer.Change(0, 250);
                }
                catch (Exception exception)
                {
                    snapshot = SetOperationFault("Could not start playback", exception);
                }
            }
        }

        Publish(snapshot);
    }

    public void Pause()
    {
        PlaybackSnapshot snapshot;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_outputDevice is null || _audioReader is null)
            {
                return;
            }

            try
            {
                _outputDevice.Pause();
                _snapshot = _snapshot with
                {
                    State = CorePlaybackState.Paused,
                    Position = _audioReader.CurrentTime,
                    ErrorMessage = null
                };
                snapshot = _snapshot;
            }
            catch (Exception exception)
            {
                snapshot = SetOperationFault("Could not pause playback", exception);
            }
        }

        Publish(snapshot);
    }

    public void Stop()
    {
        PlaybackSnapshot? snapshot = null;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_outputDevice is null || _audioReader is null)
            {
                return;
            }

            try
            {
                if (_outputDevice.PlaybackState is NAudioPlaybackState.Playing or NAudioPlaybackState.Paused)
                {
                    _outputDevice.Pause();
                }

                _audioReader.CurrentTime = TimeSpan.Zero;
                _snapshot = _snapshot with
                {
                    State = CorePlaybackState.Stopped,
                    Position = TimeSpan.Zero,
                    ErrorMessage = null
                };
                snapshot = _snapshot;
            }
            catch (Exception exception)
            {
                snapshot = SetOperationFault("Could not stop playback", exception);
            }
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public void Seek(TimeSpan position)
    {
        PlaybackSnapshot? snapshot = null;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_audioReader is null)
            {
                return;
            }

            try
            {
                var duration = _audioReader.TotalTime;
                var clampedTicks = Math.Clamp(position.Ticks, 0, Math.Max(0, duration.Ticks));
                _audioReader.CurrentTime = TimeSpan.FromTicks(clampedTicks);
                _snapshot = _snapshot with
                {
                    Position = _audioReader.CurrentTime,
                    ErrorMessage = null
                };
                snapshot = _snapshot;
            }
            catch (Exception exception)
            {
                snapshot = SetOperationFault("Could not seek", exception);
            }
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public void PreparePaused(TimeSpan position)
    {
        PlaybackSnapshot? snapshot = null;

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_outputDevice is null || _audioReader is null)
            {
                return;
            }

            try
            {
                var upperBound = _audioReader.TotalTime > TimeSpan.Zero
                    ? _audioReader.TotalTime
                    : TimeSpan.Zero;
                var normalized = position < TimeSpan.Zero
                    ? TimeSpan.Zero
                    : position > upperBound
                        ? upperBound
                        : position;

                _audioReader.CurrentTime = normalized;
                _snapshot = _snapshot with
                {
                    State = CorePlaybackState.Paused,
                    Position = _audioReader.CurrentTime,
                    ErrorMessage = null
                };
                snapshot = _snapshot;
            }
            catch (Exception exception)
            {
                snapshot = SetOperationFault("Could not restore the paused playback position", exception);
            }
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public void SetEqualizer(EqualizerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            ThrowIfDisposed();
            _equalizerSettings = settings.Clone();
            _equalizerProvider?.UpdateSettings(_equalizerSettings);
        }
    }

    public void SetVisualizer(VisualizerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            ThrowIfDisposed();
            _visualizerSettings = settings.Clone();
            _visualizerSettings.Normalize();
            _spectrumProvider?.UpdateSettings(_visualizerSettings);
        }
    }

    public void SetVolume(double volume)
    {
        PlaybackSnapshot snapshot;

        lock (_sync)
        {
            ThrowIfDisposed();

            var normalized = Math.Clamp(volume, 0, 1);
            try
            {
                if (_outputDevice is not null)
                {
                    _outputDevice.Volume = (float)normalized;
                }

                _snapshot = _snapshot with { Volume = normalized };
                snapshot = _snapshot;
            }
            catch (Exception exception)
            {
                snapshot = SetOperationFault("Could not change volume", exception);
            }
        }

        Publish(snapshot);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
            DisposePlaybackGraph();
            _positionTimer.Dispose();
        }
    }

    private void UpdatePosition(object? state)
    {
        PlaybackSnapshot? snapshot = null;

        lock (_sync)
        {
            if (_disposed || _audioReader is null || _snapshot.State is not (CorePlaybackState.Playing or CorePlaybackState.Paused))
            {
                return;
            }

            _snapshot = _snapshot with
            {
                Position = _audioReader.CurrentTime,
                Duration = _audioReader.TotalTime
            };
            snapshot = _snapshot;
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    private void OnVisualizerFrameReady(object? sender, VisualizerFrame frame)
    {
        if (_disposed || !ReferenceEquals(sender, _spectrumProvider))
        {
            return;
        }

        VisualizerFrameReady?.Invoke(this, frame);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackSnapshot? snapshot = null;
        var endedNaturally = false;

        lock (_sync)
        {
            if (_disposed || !ReferenceEquals(sender, _outputDevice))
            {
                return;
            }

            if (e.Exception is not null)
            {
                _snapshot = _snapshot with
                {
                    State = CorePlaybackState.Faulted,
                    ErrorMessage = $"Playback stopped unexpectedly: {e.Exception.Message}"
                };
                snapshot = _snapshot;
                _logger.Error("NAudio playback stopped with an error.", e.Exception);
            }
            else if (_audioReader is not null)
            {
                endedNaturally = _audioReader.TotalTime > TimeSpan.Zero &&
                                 (_audioReader.Position >= _audioReader.Length ||
                                  _audioReader.CurrentTime >= _audioReader.TotalTime - TimeSpan.FromMilliseconds(250));

                _snapshot = _snapshot with
                {
                    State = CorePlaybackState.Stopped,
                    Position = endedNaturally ? _audioReader.TotalTime : _audioReader.CurrentTime,
                    ErrorMessage = null
                };
                snapshot = _snapshot;
            }
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }

        if (endedNaturally)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }


    private PlaybackSnapshot SetOperationFault(string action, Exception exception)
    {
        var message = $"{action}: {exception.Message}";
        _snapshot = _snapshot with
        {
            State = CorePlaybackState.Faulted,
            ErrorMessage = message
        };
        _logger.Error(action, exception);
        return _snapshot;
    }

    private PlaybackSnapshot SetFaultedSnapshot(Track track, string message)
    {
        _snapshot = new PlaybackSnapshot(
            CorePlaybackState.Faulted,
            track,
            TimeSpan.Zero,
            track.Duration,
            _snapshot.Volume,
            message,
            track.ExtensionLabel);
        return _snapshot;
    }

    private void DisposePlaybackGraph()
    {
        _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_outputDevice is not null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _outputDevice.Stop();
            }
            catch
            {
                // Best-effort cleanup during track changes and shutdown.
            }

            _outputDevice.Dispose();
            _outputDevice = null;
        }

        if (_spectrumProvider is not null)
        {
            _spectrumProvider.FrameReady -= OnVisualizerFrameReady;
            _spectrumProvider = null;
        }

        _equalizerProvider = null;
        _audioReader?.Dispose();
        _audioReader = null;
    }


    private void Publish(PlaybackSnapshot snapshot) => SnapshotChanged?.Invoke(this, snapshot);

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NAudioPlaybackService));
        }
    }
}
