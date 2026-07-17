using CadenceStudio.Core.Enums;

namespace CadenceStudio.Core.Models;

/// <summary>
/// Persistent presentation preferences shared by the embedded Now Playing
/// visualizer and the future detached Window Visualizer. The renderer itself
/// remains isolated from playback so visual failures can never stop audio.
/// </summary>
public sealed class VisualizerExperienceSettings
{
    public bool NowPlayingVisualizerEnabled { get; set; } = true;
    public VisualizerPreset NowPlayingPreset { get; set; } = VisualizerPreset.CelestialResonance;
    public double NowPlayingMotionIntensity { get; set; } = 0.72;
    public double NowPlayingGlowIntensity { get; set; } = 0.68;

    public bool WindowVisualizerEnabled { get; set; } = true;
    public VisualizerPreset WindowPreset { get; set; } = VisualizerPreset.CelestialResonance;
    public VisualizerWindowMode DefaultWindowMode { get; set; } = VisualizerWindowMode.PopOut;
    public VisualizerRenderQuality RenderQuality { get; set; } = VisualizerRenderQuality.High;
    public int FrameRateLimit { get; set; } = 60;
    public bool HardwareAcceleration { get; set; } = true;
    public bool AlbumColorIntegration { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool RememberWindowPlacement { get; set; } = true;
    public bool AutoOpenOnPlayback { get; set; }
    public bool ShowPlaybackControls { get; set; } = true;
    public double ControlsAutoHideSeconds { get; set; } = 2.5;
    public double ParticleIntensity { get; set; } = 0.72;
    public double GlowIntensity { get; set; } = 0.78;
    public double ReflectionIntensity { get; set; } = 0.58;
    public double FrequencyResponse { get; set; } = 0.76;

    public bool SongChangePopupEnabled { get; set; } = true;
    public double SongChangePopupDurationSeconds { get; set; } = 4.0;
    public string SongChangePopupCorner { get; set; } = "BottomRight";
    public bool SuppressPopupWhileCadenceFocused { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1180;
    public double WindowHeight { get; set; } = 720;
    public string? WindowMonitorDeviceName { get; set; }

    public void Normalize()
    {
        NowPlayingPreset = Enum.IsDefined(NowPlayingPreset) ? NowPlayingPreset : VisualizerPreset.CelestialResonance;
        WindowPreset = Enum.IsDefined(WindowPreset) ? WindowPreset : VisualizerPreset.CelestialResonance;
        DefaultWindowMode = Enum.IsDefined(DefaultWindowMode) ? DefaultWindowMode : VisualizerWindowMode.PopOut;
        RenderQuality = Enum.IsDefined(RenderQuality) ? RenderQuality : VisualizerRenderQuality.High;
        FrameRateLimit = FrameRateLimit is 30 or 60 or 120 ? FrameRateLimit : 60;
        ControlsAutoHideSeconds = Normalize(ControlsAutoHideSeconds, 0.5, 10, 2.5);
        NowPlayingMotionIntensity = NormalizeUnit(NowPlayingMotionIntensity, 0.72);
        NowPlayingGlowIntensity = NormalizeUnit(NowPlayingGlowIntensity, 0.68);
        ParticleIntensity = NormalizeUnit(ParticleIntensity, 0.72);
        GlowIntensity = NormalizeUnit(GlowIntensity, 0.78);
        ReflectionIntensity = NormalizeUnit(ReflectionIntensity, 0.58);
        FrequencyResponse = NormalizeUnit(FrequencyResponse, 0.76);
        SongChangePopupDurationSeconds = Normalize(SongChangePopupDurationSeconds, 1.5, 12, 4.0);
        SongChangePopupCorner = SongChangePopupCorner is "TopLeft" or "TopRight" or "BottomLeft" or "BottomRight" ? SongChangePopupCorner : "BottomRight";
        WindowWidth = Normalize(WindowWidth, 480, 7680, 1180);
        WindowHeight = Normalize(WindowHeight, 320, 4320, 720);
        WindowLeft = NormalizeCoordinate(WindowLeft);
        WindowTop = NormalizeCoordinate(WindowTop);
        WindowMonitorDeviceName = string.IsNullOrWhiteSpace(WindowMonitorDeviceName)
            ? null
            : WindowMonitorDeviceName.Trim();
    }

    private static double NormalizeUnit(double value, double fallback) => Normalize(value, 0, 1, fallback);

    private static double Normalize(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static double? NormalizeCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;
}
