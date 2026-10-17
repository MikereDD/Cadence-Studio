using CadenceStudio.Core.Models;
using CadenceStudio.Infrastructure.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using CadenceStudio.Core.Contracts;

var failures = 0;
void Check(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex}"); }
}
void Near(double actual, double expected, double tolerance = 1e-6)
{
    if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
        throw new Exception($"expected {expected}, got {actual}");
}
var input = Enumerable.Range(0, 48000).Select(i =>
    (float)(0.1 * Math.Sin(2 * Math.PI * (i % 2 == 0 ? 1000 : 250) * (i / 2) / 48000))).ToArray();
float[] Render(ISampleProvider source, int offset = 0, int chunk = 4096)
{
    var result = new List<float>();
    var buffer = new float[chunk + offset];
    int read;
    while ((read = source.Read(buffer, offset, chunk)) > 0)
        result.AddRange(buffer.Skip(offset).Take(read));
    return result.ToArray();
}
void Same(float[] actual, float[] expected)
{
    if (actual.Length != expected.Length) throw new Exception("Sample count changed");
    for (var i = 0; i < actual.Length; i++) Near(actual[i], expected[i]);
}
Check("Bypass is transparent even with non-flat EQ and preamp", () => {
    var settings = new EqualizerSettings { Enabled = false, PreampDb = -12 };
    settings.BandGainsDb[5] = 12;
    Same(Render(new EqualizerSampleProvider(new Samples(input), settings)), input);
});
Check("Flat enabled EQ is unity", () =>
    Same(Render(new EqualizerSampleProvider(new Samples(input), new() { Enabled = true })), input));
Check("Master volume unity, half amplitude, and mute", () => {
    foreach (var level in new[] { 1f, 0.5f, 0f }) {
        var eq = new EqualizerSampleProvider(new Samples(input), new() { Enabled = true });
        var spectrum = new SpectrumSampleProvider(eq, new VisualizerSettings());
        Same(Render(new VolumeSampleProvider(spectrum) { Volume = level }), input.Select(x => x * level).ToArray());
    }
});
Check("Auto headroom does not double-count an existing negative preamp", () => {
    var settings = new EqualizerSettings { Enabled = true, PreampDb = -6 };
    settings.BandGainsDb[5] = 6;
    Near(settings.EffectivePreampDb, -6);
    settings.PreampDb = -9; Near(settings.EffectivePreampDb, -9);
    settings.PreampDb = 0; Near(settings.EffectivePreampDb, -6);
    settings.AutoHeadroomEnabled = false; Near(settings.EffectivePreampDb, 0);
});
Check("Stereo EQ is independent of buffer offset and odd read sizes", () => {
    var settings = new EqualizerSettings { Enabled = true };
    settings.BandGainsDb[5] = 6;
    Same(Render(new EqualizerSampleProvider(new Samples(input), settings), 1, 103),
         Render(new EqualizerSampleProvider(new Samples(input), settings)));
});
Check("Preamp updates preserve filter history", () => {
    var settings = new EqualizerSettings { Enabled = true, AutoHeadroomEnabled = false };
    settings.BandGainsDb[5] = 6;
    var reference = new EqualizerSampleProvider(new Samples(input), settings);
    var changed = new EqualizerSampleProvider(new Samples(input), settings);
    var a = new float[1200]; var b = new float[1200];
    reference.Read(a, 0, a.Length); changed.Read(b, 0, b.Length);
    settings.PreampDb = -6; changed.UpdateSettings(settings);
    reference.Read(a, 0, a.Length); changed.Read(b, 0, b.Length);
    Same(b, a.Select(x => x * (float)Math.Pow(10, -6d / 20)).ToArray());
});
Check("A 1 kHz boost produces the requested gain", () => {
    var sine = Enumerable.Range(0, 48000).Select(i => (float)(0.01 * Math.Sin(2 * Math.PI * 1000 * i / 48000))).ToArray();
    var settings = new EqualizerSettings { Enabled = true, AutoHeadroomEnabled = false };
    settings.BandGainsDb[5] = 6;
    var output = Render(new EqualizerSampleProvider(new Samples(sine, 1), settings));
    var ratio = Math.Sqrt(output.Skip(4000).Average(x => (double)x*x) / sine.Skip(4000).Average(x => (double)x*x));
    Near(20 * Math.Log10(ratio), 6, 0.02);
});
if (args.Length > 0)
{
    Check("Supplied MP3: bypass and flat EQ at 100% preserve decoded samples", () => {
        using var reader = new AudioFileReader(args[0]);
        var original = Render(reader);
        foreach (var enabled in new[] { false, true }) {
            using var comparison = new AudioFileReader(args[0]);
            var eq = new EqualizerSampleProvider(comparison, new() { Enabled = enabled });
            var actual = Render(new VolumeSampleProvider(eq) { Volume = 1 });
            // Flat EQ's existing safety clamp can affect decoded overs above full scale.
            Same(actual, enabled ? original.Select(x => Math.Clamp(x, -1f, 1f)).ToArray() : original);
        }
        Console.WriteLine($"Decoded {original.Length} samples; peak {original.Max(x => Math.Abs(x)):F6}");
    });
}
if (args.Length > 1 && args[1] == "--device")
{
    Check("WASAPI opens MP3 and player volume leaves Windows mixer untouched (no playback)", () => {
        using var service = new NAudioPlaybackService(new TestLogger());
        service.SetVolume(0.5);
        service.OpenAsync(new Track { FilePath = args[0], Title = "Audio regression test" }).GetAwaiter().GetResult();
        if (service.Snapshot.ErrorMessage is not null) throw new Exception(service.Snapshot.ErrorMessage);
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var endpointVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        var sessionVolume = device.AudioSessionManager.SimpleAudioVolume;
        var level = sessionVolume.Volume;
        foreach (var volume in new[] { 0d, 0.25, 1 }) {
            service.SetVolume(volume);
            Near(service.Snapshot.Volume, volume);
            Near(sessionVolume.Volume, level);
            Near(device.AudioEndpointVolume.MasterVolumeLevelScalar, endpointVolume);
        }
        service.PreparePaused(TimeSpan.FromSeconds(10));
        if (service.Snapshot.ErrorMessage is not null) throw new Exception(service.Snapshot.ErrorMessage);
    });
}
Console.WriteLine($"Failures: {failures}");
return failures == 0 ? 0 : 1;

sealed class Samples(float[] data, int channels = 2) : ISampleProvider
{
    private int position;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
    public int Read(float[] buffer, int offset, int count)
    {
        var read = Math.Min(count, data.Length - position);
        Array.Copy(data, position, buffer, offset, read);
        position += read;
        return read;
    }
}

sealed class TestLogger : IAppLogger
{
    public void Info(string message) { }
    public void Warning(string message) => Console.WriteLine(message);
    public void Error(string message, Exception? exception = null) => Console.WriteLine($"{message}: {exception?.Message}");
}
