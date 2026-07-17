using System.Diagnostics;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Models;
using NAudio.Dsp;
using NAudio.Wave;

namespace CadenceStudio.Infrastructure.Services;

internal sealed class SpectrumSampleProvider : ISampleProvider
{
    private const int FftLength = 2048;
    private const int FftExponent = 11;
    private const double MinimumFrequencyHz = 30;
    private const double MaximumFrequencyHz = 18_000;
    private const double HammingCoherentGain = 0.54;
    private const int MinimumPublishIntervalMs = 30;

    private readonly object _sync = new();
    private readonly ISampleProvider _source;
    private readonly Complex[][] _fftBuffers;
    private readonly float[] _window = new float[FftLength];
    private readonly Stopwatch _publishClock = Stopwatch.StartNew();

    private VisualizerSettings _settings;
    private int _fftPosition;
    private int _channelPosition;

    public SpectrumSampleProvider(ISampleProvider source, VisualizerSettings settings)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        WaveFormat = source.WaveFormat;
        _settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
        _settings.Normalize();

        var channelCount = Math.Max(1, WaveFormat.Channels);
        _fftBuffers = Enumerable.Range(0, channelCount)
            .Select(_ => new Complex[FftLength])
            .ToArray();

        for (var index = 0; index < FftLength; index++)
        {
            _window[index] = (float)FastFourierTransform.HammingWindow(index, FftLength);
        }
    }

    public WaveFormat WaveFormat { get; }

    public event EventHandler<VisualizerFrame>? FrameReady;

    public void UpdateSettings(VisualizerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            _settings = settings.Clone();
            _settings.Normalize();
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = _source.Read(buffer, offset, count);
        if (samplesRead <= 0)
        {
            return samplesRead;
        }

        VisualizerFrame? newestFrame = null;

        lock (_sync)
        {
            var channelCount = _fftBuffers.Length;
            for (var sampleIndex = 0; sampleIndex < samplesRead; sampleIndex++)
            {
                var channel = _channelPosition;
                var sample = buffer[offset + sampleIndex];

                _fftBuffers[channel][_fftPosition].X = sample * _window[_fftPosition];
                _fftBuffers[channel][_fftPosition].Y = 0;

                _channelPosition++;
                if (_channelPosition < channelCount)
                {
                    continue;
                }

                _channelPosition = 0;
                _fftPosition++;
                if (_fftPosition < FftLength)
                {
                    continue;
                }

                _fftPosition = 0;
                if (!_settings.Enabled || _publishClock.ElapsedMilliseconds < MinimumPublishIntervalMs)
                {
                    continue;
                }

                newestFrame = BuildFrame();
                _publishClock.Restart();
            }
        }

        if (newestFrame is not null)
        {
            FrameReady?.Invoke(this, newestFrame);
        }

        return samplesRead;
    }

    private VisualizerFrame BuildFrame()
    {
        foreach (var channelBuffer in _fftBuffers)
        {
            FastFourierTransform.FFT(true, FftExponent, channelBuffer);
        }

        var bandCount = _settings.BarCount;
        var compensatedDb = new double[bandCount];
        var sampleRate = Math.Max(1, WaveFormat.SampleRate);
        var nyquist = sampleRate / 2d;
        var maximumFrequency = Math.Min(MaximumFrequencyHz, nyquist * 0.94d);
        var frequencyRange = Math.Max(1.01d, maximumFrequency / MinimumFrequencyHz);
        var maximumBin = (FftLength / 2) - 1;

        for (var bandIndex = 0; bandIndex < bandCount; bandIndex++)
        {
            var startRatio = bandIndex / (double)bandCount;
            var endRatio = (bandIndex + 1) / (double)bandCount;
            var startFrequency = MinimumFrequencyHz * Math.Pow(frequencyRange, startRatio);
            var endFrequency = MinimumFrequencyHz * Math.Pow(frequencyRange, endRatio);
            var centerFrequency = Math.Sqrt(startFrequency * endFrequency);
            var startBin = Math.Clamp((int)Math.Floor(startFrequency * FftLength / sampleRate), 1, maximumBin);
            var endBin = Math.Clamp((int)Math.Ceiling(endFrequency * FftLength / sampleRate), startBin, maximumBin);

            var strongest = 0d;
            var secondStrongest = 0d;
            var thirdStrongest = 0d;
            var sumSquares = 0d;
            var binCount = 0;

            for (var bin = startBin; bin <= endBin; bin++)
            {
                var channelPower = 0d;
                foreach (var channelBuffer in _fftBuffers)
                {
                    var real = channelBuffer[bin].X;
                    var imaginary = channelBuffer[bin].Y;
                    channelPower += (real * real) + (imaginary * imaginary);
                }

                // Combine channel energy after the FFT instead of summing L/R samples
                // first. A mono sum can cancel wide stereo vocals, guitars, and cymbals
                // while leaving centered bass intact—the exact "first six bars only"
                // failure this provider is designed to avoid.
                var magnitude = Math.Sqrt(channelPower / _fftBuffers.Length);
                magnitude *= 2d / (FftLength * HammingCoherentGain);

                sumSquares += magnitude * magnitude;
                binCount++;

                if (magnitude > strongest)
                {
                    thirdStrongest = secondStrongest;
                    secondStrongest = strongest;
                    strongest = magnitude;
                }
                else if (magnitude > secondStrongest)
                {
                    thirdStrongest = secondStrongest;
                    secondStrongest = magnitude;
                }
                else if (magnitude > thirdStrongest)
                {
                    thirdStrongest = magnitude;
                }
            }

            var topCount = Math.Min(3, binCount);
            var upperEnergy = topCount switch
            {
                1 => strongest,
                2 => (strongest + secondStrongest) / 2d,
                _ => (strongest + secondStrongest + thirdStrongest) / 3d
            };
            var rmsEnergy = binCount > 0 ? Math.Sqrt(sumSquares / binCount) : 0d;
            var representativeMagnitude = (upperEnergy * 0.72d) + (rmsEnergy * 0.28d);
            var decibels = 20d * Math.Log10(representativeMagnitude + 1e-12d);

            compensatedDb[bandIndex] = decibels + GetSpectralCompensationDb(centerFrequency);
        }

        // Follow the current track's level without flattening the spectrum. The 82nd
        // percentile keeps a single bass transient from defining the scale for every
        // band, while the fixed dynamic range preserves meaningful differences.
        var sortedLevels = compensatedDb.OrderBy(value => value).ToArray();
        var referenceIndex = Math.Clamp((int)Math.Round((sortedLevels.Length - 1) * 0.82d), 0, sortedLevels.Length - 1);
        var referenceDb = sortedLevels[referenceIndex];
        var ceilingDb = Math.Clamp(referenceDb + 6d, -38d, -10d);
        var floorDb = Math.Max(-96d, ceilingDb - 58d);
        var sensitivityDb = 20d * Math.Log10(Math.Max(0.01d, _settings.Sensitivity));

        var normalizedBands = new float[bandCount];
        for (var bandIndex = 0; bandIndex < bandCount; bandIndex++)
        {
            var levelDb = compensatedDb[bandIndex] + sensitivityDb;
            var normalized = (levelDb - floorDb) / Math.Max(1d, ceilingDb - floorDb);
            normalized = Math.Clamp(normalized, 0d, 1d);

            // Gentle compression keeps vocals and cymbals visible while silence stays
            // at zero. It does not force every bar to move equally.
            normalizedBands[bandIndex] = (float)Math.Pow(normalized, 0.62d);
        }

        // A small cross-band blend makes the display coherent without smearing the
        // actual frequency response or turning it into a decorative animation.
        var bands = new float[bandCount];
        for (var bandIndex = 0; bandIndex < bandCount; bandIndex++)
        {
            var center = normalizedBands[bandIndex];
            var left = bandIndex > 0 ? normalizedBands[bandIndex - 1] : center;
            var right = bandIndex + 1 < bandCount ? normalizedBands[bandIndex + 1] : center;
            bands[bandIndex] = Math.Clamp((center * 0.76f) + (left * 0.12f) + (right * 0.12f), 0f, 1f);
        }

        return new VisualizerFrame(DateTimeOffset.UtcNow, bands);
    }

    private static double GetSpectralCompensationDb(double centerFrequency)
    {
        // Recorded music naturally falls in level as frequency rises. Apply a modest
        // tilt so mids and treble remain visible, while retaining stronger bass motion.
        var octavesFromReference = Math.Log2(Math.Max(20d, centerFrequency) / 125d);
        return Math.Clamp(octavesFromReference * 2.4d, -4d, 16d);
    }
}
