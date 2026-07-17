using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CadenceStudio.App.Mvvm;
using CadenceStudio.App.Services;
using CadenceStudio.Core.Contracts;
using CadenceStudio.Core.Enums;
using CadenceStudio.Core.Models;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isVisualizerEnabled;
    private double _visualizerSensitivity;
    private double _visualizerDecay;
    private int _visualizerBarCount;
    private bool _visualizerReceivingAudio;
    private double _expandedVisualizerMaxBarHeight = 122;
    private string _nowPlayingVisualizerMode = "MirroredWaveform";

    public ObservableCollection<VisualizerBarViewModel> VisualizerBars { get; } = [];

    public RelayCommand ToggleVisualizerCommand { get; private set; } = null!;
    public RelayCommand<object> SetVisualizerDensityCommand { get; private set; } = null!;
    public RelayCommand<object> SetNowPlayingVisualizerModeCommand { get; private set; } = null!;
    public RelayCommand ToggleNowPlayingVisualizerExperienceCommand { get; private set; } = null!;
    public RelayCommand ToggleWindowVisualizerExperienceCommand { get; private set; } = null!;
    public RelayCommand ToggleAlbumColorIntegrationCommand { get; private set; } = null!;
    public RelayCommand ToggleWindowAlwaysOnTopCommand { get; private set; } = null!;
    public RelayCommand ToggleRememberWindowPlacementCommand { get; private set; } = null!;
    public RelayCommand ToggleAutoOpenWindowVisualizerCommand { get; private set; } = null!;
    public RelayCommand ToggleWindowPlaybackControlsCommand { get; private set; } = null!;
    public RelayCommand<object> SetWindowVisualizerModeCommand { get; private set; } = null!;
    public RelayCommand<object> SetWindowVisualizerQualityCommand { get; private set; } = null!;
    public RelayCommand<object> SetWindowVisualizerFrameRateCommand { get; private set; } = null!;
    public RelayCommand<object> SetWindowVisualizerPresetCommand { get; private set; } = null!;

    public string NowPlayingVisualizerMode
    {
        get => _nowPlayingVisualizerMode;
        private set
        {
            if (SetProperty(ref _nowPlayingVisualizerMode, value))
            {
                OnPropertyChanged(nameof(IsSpectrumHorizonMode));
                OnPropertyChanged(nameof(IsMirroredWaveformMode));
                OnPropertyChanged(nameof(NowPlayingVisualizerModeLabel));
            }
        }
    }

    public bool IsSpectrumHorizonMode => NowPlayingVisualizerMode == "SpectrumHorizon";
    public bool IsMirroredWaveformMode => NowPlayingVisualizerMode == "MirroredWaveform";
    public string NowPlayingVisualizerModeLabel => IsMirroredWaveformMode ? "Mirrored Waveform" : "Spectrum Horizon";

    private VisualizerExperienceSettings Experience => _settings.VisualizerExperience;

    public bool IsNowPlayingVisualizerExperienceEnabled => Experience.NowPlayingVisualizerEnabled;
    public string NowPlayingVisualizerExperienceLabel => IsNowPlayingVisualizerExperienceEnabled ? "Enabled" : "Disabled";
    public bool IsWindowVisualizerExperienceEnabled => Experience.WindowVisualizerEnabled;
    public string WindowVisualizerExperienceLabel => IsWindowVisualizerExperienceEnabled ? "Enabled" : "Disabled";
    public bool IsAlbumColorIntegrationEnabled => Experience.AlbumColorIntegration;
    public bool IsWindowVisualizerAlwaysOnTop => Experience.AlwaysOnTop;
    public bool IsRememberWindowPlacementEnabled => Experience.RememberWindowPlacement;
    public bool IsAutoOpenWindowVisualizerEnabled => Experience.AutoOpenOnPlayback;
    public bool IsWindowPlaybackControlsEnabled => Experience.ShowPlaybackControls;
    public double WindowVisualizerControlsAutoHideSeconds => Experience.ControlsAutoHideSeconds;

    public bool SongChangePopupEnabled
    {
        get => Experience.SongChangePopupEnabled;
        set { if (Experience.SongChangePopupEnabled == value) return; Experience.SongChangePopupEnabled = value; OnPropertyChanged(); }
    }
    public bool SuppressPopupWhileCadenceFocused
    {
        get => Experience.SuppressPopupWhileCadenceFocused;
        set { if (Experience.SuppressPopupWhileCadenceFocused == value) return; Experience.SuppressPopupWhileCadenceFocused = value; OnPropertyChanged(); }
    }
    public double SongChangePopupDurationSeconds
    {
        get => Experience.SongChangePopupDurationSeconds;
        set { var next = Math.Clamp(value, 1.5, 12); if (Math.Abs(Experience.SongChangePopupDurationSeconds-next)<0.01) return; Experience.SongChangePopupDurationSeconds=next; OnPropertyChanged(); OnPropertyChanged(nameof(SongChangePopupDurationText)); }
    }
    public string SongChangePopupDurationText => $"{SongChangePopupDurationSeconds:0.#} seconds";
    public string SongChangePopupCorner
    {
        get => Experience.SongChangePopupCorner;
        set { if (value is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight") || Experience.SongChangePopupCorner == value) return; Experience.SongChangePopupCorner=value; OnPropertyChanged(); OnPropertyChanged(nameof(SongChangePopupCornerLabel)); }
    }
    public string SongChangePopupCornerLabel => SongChangePopupCorner switch { "TopLeft" => "Top left", "TopRight" => "Top right", "BottomLeft" => "Bottom left", _ => "Bottom right" };

    public double NowPlayingMotionIntensity
    {
        get => Experience.NowPlayingMotionIntensity;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(Experience.NowPlayingMotionIntensity - next) < 0.001) return;
            Experience.NowPlayingMotionIntensity = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingMotionIntensityText));
        }
    }
    public string NowPlayingMotionIntensityText => $"{NowPlayingMotionIntensity * 100:0}%";

    public double NowPlayingGlowIntensity
    {
        get => Experience.NowPlayingGlowIntensity;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(Experience.NowPlayingGlowIntensity - next) < 0.001) return;
            Experience.NowPlayingGlowIntensity = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NowPlayingGlowIntensityText));
        }
    }
    public string NowPlayingGlowIntensityText => $"{NowPlayingGlowIntensity * 100:0}%";

    public double WindowParticleIntensity
    {
        get => Experience.ParticleIntensity;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(Experience.ParticleIntensity - next) < 0.001) return;
            Experience.ParticleIntensity = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowParticleIntensityText));
        }
    }
    public string WindowParticleIntensityText => $"{WindowParticleIntensity * 100:0}%";

    public double WindowGlowIntensity
    {
        get => Experience.GlowIntensity;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(Experience.GlowIntensity - next) < 0.001) return;
            Experience.GlowIntensity = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowGlowIntensityText));
        }
    }
    public string WindowGlowIntensityText => $"{WindowGlowIntensity * 100:0}%";

    public double WindowReflectionIntensity
    {
        get => Experience.ReflectionIntensity;
        set
        {
            var next = Math.Clamp(value, 0, 1);
            if (Math.Abs(Experience.ReflectionIntensity - next) < 0.001) return;
            Experience.ReflectionIntensity = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowReflectionIntensityText));
        }
    }
    public string WindowReflectionIntensityText => $"{WindowReflectionIntensity * 100:0}%";

    public bool IsWindowModePopOut => Experience.DefaultWindowMode == VisualizerWindowMode.PopOut;
    public bool IsWindowModeFloating => Experience.DefaultWindowMode == VisualizerWindowMode.Floating;
    public bool IsWindowModeFullscreen => Experience.DefaultWindowMode == VisualizerWindowMode.Fullscreen;
    public string WindowVisualizerModeLabel => Experience.DefaultWindowMode switch
    {
        VisualizerWindowMode.Floating => "Floating",
        VisualizerWindowMode.Fullscreen => "Fullscreen",
        _ => "Pop-out"
    };

    public bool IsWindowQualityBalanced => Experience.RenderQuality == VisualizerRenderQuality.Balanced;
    public bool IsWindowQualityHigh => Experience.RenderQuality == VisualizerRenderQuality.High;
    public bool IsWindowQualityUltra => Experience.RenderQuality == VisualizerRenderQuality.Ultra;
    public string WindowVisualizerQualityLabel => Experience.RenderQuality.ToString();
    public bool IsWindowFrameRate30 => Experience.FrameRateLimit == 30;
    public bool IsWindowFrameRate60 => Experience.FrameRateLimit == 60;
    public bool IsWindowFrameRate120 => Experience.FrameRateLimit == 120;
    public string WindowVisualizerFrameRateLabel => $"{Experience.FrameRateLimit} FPS";

    public double WindowVisualizerSavedWidth => Experience.WindowWidth;
    public double WindowVisualizerSavedHeight => Experience.WindowHeight;
    public double? WindowVisualizerSavedLeft => Experience.WindowLeft;
    public double? WindowVisualizerSavedTop => Experience.WindowTop;
    public bool ShouldRememberWindowVisualizerPlacement => Experience.RememberWindowPlacement;
    public VisualizerWindowMode WindowVisualizerWindowMode => Experience.DefaultWindowMode;
    public VisualizerPreset WindowVisualizerPreset => Experience.WindowPreset;
    public string WindowVisualizerPresetLabel => Experience.WindowPreset switch
    {
        VisualizerPreset.AuroraCascade => "Aurora Cascade",
        VisualizerPreset.GlassHorizon => "Glass Horizon",
        VisualizerPreset.InfiniteWindows => "Infinite Windows",
        _ => "Celestial Resonance"
    };
    public bool IsWindowPresetCelestial => Experience.WindowPreset == VisualizerPreset.CelestialResonance;
    public bool IsWindowPresetAurora => Experience.WindowPreset == VisualizerPreset.AuroraCascade;
    public bool IsWindowPresetGlass => Experience.WindowPreset == VisualizerPreset.GlassHorizon;
    public bool IsWindowPresetInfinite => Experience.WindowPreset == VisualizerPreset.InfiniteWindows;

    public void CaptureWindowVisualizerPlacement(double left, double top, double width, double height)
    {
        if (!Experience.RememberWindowPlacement) return;
        Experience.WindowLeft = left;
        Experience.WindowTop = top;
        Experience.WindowWidth = Math.Clamp(width, 480, 7680);
        Experience.WindowHeight = Math.Clamp(height, 320, 4320);
    }

    public bool IsVisualizerEnabled
    {
        get => _isVisualizerEnabled;
        private set
        {
            if (SetProperty(ref _isVisualizerEnabled, value))
            {
                OnPropertyChanged(nameof(VisualizerToggleLabel));
                OnPropertyChanged(nameof(VisualizerActivityText));
                ApplyVisualizerSettings();
                if (!value)
                {
                    ResetVisualizerBars();
                }
            }
        }
    }

    public string VisualizerToggleLabel => IsVisualizerEnabled ? "Visualizer on" : "Visualizer off";

    public double VisualizerSensitivity
    {
        get => _visualizerSensitivity;
        set
        {
            var normalized = Math.Clamp(value, 0.5, 2.0);
            if (SetProperty(ref _visualizerSensitivity, normalized))
            {
                OnPropertyChanged(nameof(VisualizerSensitivityText));
                OnPropertyChanged(nameof(VisualizerStatusText));
                ApplyVisualizerSettings();
            }
        }
    }

    public string VisualizerSensitivityText => $"{VisualizerSensitivity:0.0}×";

    public double VisualizerDecay
    {
        get => _visualizerDecay;
        set
        {
            var normalized = Math.Clamp(value, 0.55, 0.96);
            if (SetProperty(ref _visualizerDecay, normalized))
            {
                OnPropertyChanged(nameof(VisualizerDecayText));
                OnPropertyChanged(nameof(VisualizerStatusText));
                ApplyVisualizerSettings();
            }
        }
    }

    public string VisualizerDecayText => VisualizerDecay switch
    {
        < 0.72 => "Fast",
        < 0.88 => "Balanced",
        _ => "Smooth"
    };

    public int VisualizerBarCount
    {
        get => _visualizerBarCount;
        private set
        {
            if (SetProperty(ref _visualizerBarCount, value))
            {
                RebuildVisualizerBars();
                OnPropertyChanged(nameof(VisualizerDensityText));
                OnPropertyChanged(nameof(VisualizerStatusText));
                OnPropertyChanged(nameof(IsVisualizer24Bars));
                OnPropertyChanged(nameof(IsVisualizer32Bars));
                OnPropertyChanged(nameof(IsVisualizer48Bars));
                ResetVisualizerBars();
                ApplyVisualizerSettings();
            }
        }
    }

    public string VisualizerDensityText => $"{VisualizerBarCount} bars";

    public bool IsVisualizer24Bars => VisualizerBarCount == 24;
    public bool IsVisualizer32Bars => VisualizerBarCount == 32;
    public bool IsVisualizer48Bars => VisualizerBarCount == 48;

    public string VisualizerStatusText =>
        $"Live FFT • {VisualizerDensityText} • {VisualizerSensitivityText} sensitivity • {VisualizerDecayText.ToLowerInvariant()} decay";

    public string VisualizerActivityText => !IsVisualizerEnabled
        ? "Visualizer disabled"
        : _visualizerReceivingAudio
            ? $"Live audio spectrum • {VisualizerBarCount} bands"
            : "Spectrum ready • play music to begin";

    private void InitializeVisualizerState(VisualizerSettings settings)
    {
        settings ??= new VisualizerSettings();
        settings.Normalize();

        _isVisualizerEnabled = settings.Enabled;
        _visualizerSensitivity = settings.Sensitivity;
        _visualizerDecay = settings.Decay;
        _visualizerBarCount = settings.BarCount;
        RebuildVisualizerBars();
    }

    private void InitializeVisualizerCommands()
    {
        ToggleVisualizerCommand = new RelayCommand(() =>
        {
            IsVisualizerEnabled = !IsVisualizerEnabled;
            StatusText = VisualizerToggleLabel;
        });

        SetVisualizerDensityCommand = new RelayCommand<object>(value =>
        {
            if (int.TryParse(value?.ToString(), out var count) && count is 24 or 32 or 48)
            {
                // Accept either string or numeric XAML command parameters. The property
                // setter rebuilds the collection and updates the active DSP settings.
                VisualizerBarCount = count;
                StatusText = $"Visualizer density set to {VisualizerDensityText}.";
            }
        });

        SetNowPlayingVisualizerModeCommand = new RelayCommand<object>(value =>
        {
            var mode = value?.ToString();
            if (mode is "SpectrumHorizon" or "MirroredWaveform")
            {
                NowPlayingVisualizerMode = mode;
                StatusText = $"Now Playing Visualizer: {NowPlayingVisualizerModeLabel}.";
            }
        });

        ToggleNowPlayingVisualizerExperienceCommand = new RelayCommand(() =>
        {
            Experience.NowPlayingVisualizerEnabled = !Experience.NowPlayingVisualizerEnabled;
            OnPropertyChanged(nameof(IsNowPlayingVisualizerExperienceEnabled));
            OnPropertyChanged(nameof(NowPlayingVisualizerExperienceLabel));
            StatusText = $"Now Playing Visualizer {NowPlayingVisualizerExperienceLabel.ToLowerInvariant()}.";
        });
        ToggleWindowVisualizerExperienceCommand = new RelayCommand(() =>
        {
            Experience.WindowVisualizerEnabled = !Experience.WindowVisualizerEnabled;
            OnPropertyChanged(nameof(IsWindowVisualizerExperienceEnabled));
            OnPropertyChanged(nameof(WindowVisualizerExperienceLabel));
            StatusText = $"Window Visualizer {WindowVisualizerExperienceLabel.ToLowerInvariant()}.";
        });
        ToggleAlbumColorIntegrationCommand = new RelayCommand(() =>
        {
            Experience.AlbumColorIntegration = !Experience.AlbumColorIntegration;
            OnPropertyChanged(nameof(IsAlbumColorIntegrationEnabled));
        });
        ToggleWindowAlwaysOnTopCommand = new RelayCommand(() =>
        {
            Experience.AlwaysOnTop = !Experience.AlwaysOnTop;
            OnPropertyChanged(nameof(IsWindowVisualizerAlwaysOnTop));
        });
        ToggleRememberWindowPlacementCommand = new RelayCommand(() =>
        {
            Experience.RememberWindowPlacement = !Experience.RememberWindowPlacement;
            OnPropertyChanged(nameof(IsRememberWindowPlacementEnabled));
        });
        ToggleAutoOpenWindowVisualizerCommand = new RelayCommand(() =>
        {
            Experience.AutoOpenOnPlayback = !Experience.AutoOpenOnPlayback;
            OnPropertyChanged(nameof(IsAutoOpenWindowVisualizerEnabled));
        });
        ToggleWindowPlaybackControlsCommand = new RelayCommand(() =>
        {
            Experience.ShowPlaybackControls = !Experience.ShowPlaybackControls;
            OnPropertyChanged(nameof(IsWindowPlaybackControlsEnabled));
        });
        SetWindowVisualizerModeCommand = new RelayCommand<object>(value =>
        {
            if (Enum.TryParse<VisualizerWindowMode>(value?.ToString(), out var mode))
            {
                Experience.DefaultWindowMode = mode;
                OnPropertyChanged(nameof(IsWindowModePopOut));
                OnPropertyChanged(nameof(IsWindowModeFloating));
                OnPropertyChanged(nameof(IsWindowModeFullscreen));
                OnPropertyChanged(nameof(WindowVisualizerModeLabel));
            }
        });
        SetWindowVisualizerQualityCommand = new RelayCommand<object>(value =>
        {
            if (Enum.TryParse<VisualizerRenderQuality>(value?.ToString(), out var quality))
            {
                Experience.RenderQuality = quality;
                OnPropertyChanged(nameof(IsWindowQualityBalanced));
                OnPropertyChanged(nameof(IsWindowQualityHigh));
                OnPropertyChanged(nameof(IsWindowQualityUltra));
                OnPropertyChanged(nameof(WindowVisualizerQualityLabel));
            }
        });
        SetWindowVisualizerFrameRateCommand = new RelayCommand<object>(value =>
        {
            if (int.TryParse(value?.ToString(), out var fps) && fps is 30 or 60 or 120)
            {
                Experience.FrameRateLimit = fps;
                OnPropertyChanged(nameof(IsWindowFrameRate30));
                OnPropertyChanged(nameof(IsWindowFrameRate60));
                OnPropertyChanged(nameof(IsWindowFrameRate120));
                OnPropertyChanged(nameof(WindowVisualizerFrameRateLabel));
            }
        });
        SetWindowVisualizerPresetCommand = new RelayCommand<object>(value =>
        {
            if (!Enum.TryParse<VisualizerPreset>(value?.ToString(), out var preset) ||
                preset is not (VisualizerPreset.CelestialResonance or VisualizerPreset.AuroraCascade or VisualizerPreset.GlassHorizon or VisualizerPreset.InfiniteWindows))
                return;
            Experience.WindowPreset = preset;
            OnPropertyChanged(nameof(WindowVisualizerPreset));
            OnPropertyChanged(nameof(WindowVisualizerPresetLabel));
            OnPropertyChanged(nameof(IsWindowPresetCelestial));
            OnPropertyChanged(nameof(IsWindowPresetAurora));
            OnPropertyChanged(nameof(IsWindowPresetGlass));
            OnPropertyChanged(nameof(IsWindowPresetInfinite));
            StatusText = $"Window Visualizer preset: {WindowVisualizerPresetLabel}.";
        });
    }

    private VisualizerSettings BuildVisualizerSettings() => new()
    {
        Enabled = IsVisualizerEnabled,
        Sensitivity = VisualizerSensitivity,
        Decay = VisualizerDecay,
        BarCount = VisualizerBarCount
    };

    private void ApplyVisualizerSettings()
    {
        if (_disposed)
        {
            return;
        }

        _playbackService.SetVisualizer(BuildVisualizerSettings());
    }

    private void OnVisualizerFrameReady(object? sender, VisualizerFrame frame)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() => ApplyVisualizerFrame(frame)));
    }

    private void ApplyVisualizerFrame(VisualizerFrame frame)
    {
        if (_disposed ||
            !IsVisualizerEnabled ||
            _playbackService.Snapshot.State != PlaybackState.Playing ||
            frame.Bands.Count == 0 ||
            VisualizerBars.Count != frame.Bands.Count)
        {
            return;
        }

        if (!_visualizerReceivingAudio)
        {
            _visualizerReceivingAudio = true;
            OnPropertyChanged(nameof(VisualizerActivityText));
        }

        for (var index = 0; index < VisualizerBars.Count; index++)
        {
            var bar = VisualizerBars[index];
            var target = Math.Clamp(frame.Bands[index], 0f, 1f);
            var nextLevel = target >= bar.Level
                ? bar.Level + ((target - bar.Level) * 0.72)
                : bar.Level * VisualizerDecay;

            bar.Level = nextLevel;
            bar.Height = 6 + (nextLevel * 120);
            bar.ExpandedHeight = 8 + (nextLevel * Math.Max(0, _expandedVisualizerMaxBarHeight - 8));
        }
    }

    internal void SetExpandedVisualizerHeight(double availableHeight)
    {
        _expandedVisualizerMaxBarHeight = Math.Clamp(availableHeight, 72, 240);
        foreach (var bar in VisualizerBars)
        {
            bar.ExpandedHeight = 8 + (bar.Level * Math.Max(0, _expandedVisualizerMaxBarHeight - 8));
        }
    }

    private void ResetVisualizerBars()
    {
        _visualizerReceivingAudio = false;
        OnPropertyChanged(nameof(VisualizerActivityText));

        foreach (var bar in VisualizerBars)
        {
            bar.Level = 0;
            bar.Height = 6;
            bar.ExpandedHeight = 8;
        }
    }

    private void RebuildVisualizerBars()
    {
        VisualizerBars.Clear();
        for (var index = 0; index < Math.Max(1, VisualizerBarCount); index++)
        {
            VisualizerBars.Add(new VisualizerBarViewModel
            {
                Fill = CreateVisualizerBrush(index, Math.Max(1, VisualizerBarCount)),
                ExpandedFill = CreateExpandedStageBrush(index, Math.Max(1, VisualizerBarCount))
            });
        }
    }

    private void RefreshVisualizerBrushes()
    {
        for (var index = 0; index < VisualizerBars.Count; index++)
        {
            VisualizerBars[index].Fill = CreateVisualizerBrush(index, VisualizerBars.Count);
            VisualizerBars[index].ExpandedFill = CreateExpandedStageBrush(index, VisualizerBars.Count);
        }
    }


    private static Brush CreateExpandedStageBrush(int index, int count)
    {
        var ratio = count <= 1 ? 0d : index / (double)(count - 1);
        var stops = ThemeManager.CurrentVisualizerBandColors.ToArray();

        var scaled = ratio * (stops.Length - 1);
        var left = Math.Clamp((int)Math.Floor(scaled), 0, stops.Length - 1);
        var right = Math.Clamp(left + 1, 0, stops.Length - 1);
        var localRatio = scaled - left;
        var color = InterpolateColor(stops[left], stops[right], localRatio);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Brush CreateVisualizerBrush(int index, int count)
    {
        var ratio = count <= 1 ? 0d : index / (double)(count - 1);
        Color color;

        switch (VisualizerPalette)
        {
            case VisualizerPalette.Indigo:
                color = InterpolateColor(Color.FromRgb(82, 86, 126), Color.FromRgb(145, 149, 213), ratio);
                break;
            case VisualizerPalette.Spectrum:
                color = CreateMutedSpectrumColor(ratio);
                break;
            default:
                color = InterpolateColor(Color.FromRgb(76, 84, 96), Color.FromRgb(214, 219, 226), ratio);
                break;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color CreateMutedSpectrumColor(double ratio)
    {
        var stops = new[]
        {
            Color.FromRgb(105, 112, 128),
            Color.FromRgb(100, 128, 139),
            Color.FromRgb(117, 137, 116),
            Color.FromRgb(151, 134, 105),
            Color.FromRgb(139, 107, 117)
        };

        var position = Math.Clamp(ratio, 0, 1) * (stops.Length - 1);
        var left = (int)Math.Floor(position);
        var right = Math.Min(stops.Length - 1, left + 1);
        return InterpolateColor(stops[left], stops[right], position - left);
    }

    private static Color InterpolateColor(Color start, Color end, double amount)
    {
        var value = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(start.R + ((end.R - start.R) * value)),
            (byte)Math.Round(start.G + ((end.G - start.G) * value)),
            (byte)Math.Round(start.B + ((end.B - start.B) * value)));
    }
}
