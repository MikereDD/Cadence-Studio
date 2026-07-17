using CadenceStudio.Core.Models;
using NAudio.Dsp;
using NAudio.Wave;

namespace CadenceStudio.Infrastructure.Services;

internal sealed class EqualizerSampleProvider : ISampleProvider
{
    private const float FilterQ = 1.4f;

    private readonly object _sync = new();
    private readonly ISampleProvider _source;
    private BiQuadFilter[][] _filters = [];
    private EqualizerSettings _settings = new();
    private float _preampMultiplier = 1f;

    public EqualizerSampleProvider(ISampleProvider source, EqualizerSettings settings)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        WaveFormat = source.WaveFormat;
        UpdateSettings(settings);
    }

    public WaveFormat WaveFormat { get; }

    public void UpdateSettings(EqualizerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            _settings = settings.Clone();
            _preampMultiplier = DbToMultiplier(_settings.EffectivePreampDb);
            RebuildFilters();
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead == 0)
        {
            return 0;
        }

        lock (_sync)
        {
            if (!_settings.Enabled)
            {
                return samplesRead;
            }

            var channels = Math.Max(1, WaveFormat.Channels);
            for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex++)
            {
                var bufferIndex = offset + sampleIndex;
                var channel = bufferIndex % channels;
                var sample = buffer[bufferIndex];

                foreach (var filter in _filters[channel])
                {
                    sample = filter.Transform(sample);
                }

                sample *= _preampMultiplier;

                // Final safety guard. Auto headroom normally prevents clipping,
                // while this clamp protects the output when the user disables it.
                buffer[bufferIndex] = Math.Clamp(sample, -1f, 1f);
            }
        }

        return samplesRead;
    }

    private void RebuildFilters()
    {
        var channels = Math.Max(1, WaveFormat.Channels);
        _filters = new BiQuadFilter[channels][];

        for (var channel = 0; channel < channels; channel++)
        {
            _filters[channel] = new BiQuadFilter[EqualizerSettings.BandCount];
            for (var band = 0; band < EqualizerSettings.BandCount; band++)
            {
                var safeFrequency = Math.Min(
                    (float)EqualizerSettings.FrequenciesHz[band],
                    WaveFormat.SampleRate * 0.45f);

                _filters[channel][band] = BiQuadFilter.PeakingEQ(
                    WaveFormat.SampleRate,
                    safeFrequency,
                    FilterQ,
                    (float)_settings.BandGainsDb[band]);
            }
        }
    }

    private static float DbToMultiplier(double db) => (float)Math.Pow(10, db / 20d);
}
