using CadenceStudio.Core.Enums;

namespace CadenceStudio.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 10;
    public double Volume { get; set; } = 0.72;
    public bool IsMuted { get; set; }
    public bool ShuffleEnabled { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
    public VisualizerPalette VisualizerPalette { get; set; } = VisualizerPalette.Monochrome;
    public AppTheme Theme { get; set; } = AppTheme.DarkMonochrome;
    public TypographyProfile Typography { get; set; } = TypographyProfile.Cadence;
    public TextScale TextSize { get; set; } = TextScale.Standard;
    public bool ReduceMotion { get; set; }
    public bool IsNowPlayingExpanded { get; set; }
    public EqualizerSettings Equalizer { get; set; } = new();
    public VisualizerSettings Visualizer { get; set; } = new();
    public VisualizerExperienceSettings VisualizerExperience { get; set; } = new();
    public string SelectedSection { get; set; } = "Library";
    public string SelectedNowPlayingTab { get; set; } = "Details";
    public Guid? SelectedPlaylistId { get; set; }
    public List<string> LibraryRoots { get; set; } = [];
    public List<string> QueuePaths { get; set; } = [];
    public string? CurrentTrackPath { get; set; }
    public string? SelectedQueueTrackPath { get; set; }
    public double PositionSeconds { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }

    public void Normalize()
    {
        SchemaVersion = Math.Max(10, SchemaVersion);
        Volume = Math.Clamp(Volume, 0, 1);
        SelectedSection = string.IsNullOrWhiteSpace(SelectedSection) ? "Library" : SelectedSection;
        SelectedNowPlayingTab = SelectedNowPlayingTab is "Details" or "Lyrics" or "Bio"
            ? SelectedNowPlayingTab
            : "Details";
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.DarkMonochrome;
        Typography = Enum.IsDefined(Typography) ? Typography : TypographyProfile.Cadence;
        TextSize = Enum.IsDefined(TextSize) ? TextSize : TextScale.Standard;
        LibraryRoots ??= [];
        QueuePaths ??= [];
        Equalizer ??= new EqualizerSettings();
        Equalizer.Normalize();
        Visualizer ??= new VisualizerSettings();
        Visualizer.Normalize();
        VisualizerExperience ??= new VisualizerExperienceSettings();
        VisualizerExperience.Normalize();

        LibraryRoots = LibraryRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        QueuePaths = QueuePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        PositionSeconds = Math.Max(0, PositionSeconds);
        WindowWidth = NormalizeDimension(WindowWidth, 1260, 7680, 1440);
        WindowHeight = NormalizeDimension(WindowHeight, 720, 4320, 900);
        WindowLeft = NormalizeCoordinate(WindowLeft);
        WindowTop = NormalizeCoordinate(WindowTop);
    }

    private static double NormalizeDimension(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private static double? NormalizeCoordinate(double? value) =>
        value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;
}
