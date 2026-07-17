using CadenceStudio.App.Mvvm;
using CadenceStudio.App.Services;
using CadenceStudio.Core.Enums;

namespace CadenceStudio.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private AppTheme _currentTheme;
    private TypographyProfile _currentTypography;
    private TextScale _currentTextSize;
    private bool _reduceMotion;
    private bool _isNowPlayingExpanded;

    public RelayCommand<string> ApplyThemeCommand { get; private set; } = null!;
    public RelayCommand<string> ApplyTypographyCommand { get; private set; } = null!;
    public RelayCommand<string> ApplyTextSizeCommand { get; private set; } = null!;
    public RelayCommand ToggleReducedMotionCommand { get; private set; } = null!;
    public RelayCommand ToggleNowPlayingModeCommand { get; private set; } = null!;

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (SetProperty(ref _currentTheme, value))
            {
                NotifyThemeSelection();
            }
        }
    }

    public string ThemeName => CurrentTheme switch
    {
        AppTheme.OledBlack => "OLED Black",
        AppTheme.Graphite => "Graphite",
        AppTheme.MidnightIndigo => "Midnight Indigo",
        AppTheme.ObsidianGold => "Obsidian Gold",
        AppTheme.AuroraPulse => "Aurora Pulse",
        _ => "Dark Monochrome"
    };

    public string ThemeDescription => CurrentTheme switch
    {
        AppTheme.OledBlack => "True-black surfaces for OLED displays with restrained cool accents.",
        AppTheme.Graphite => "A softer neutral-gray Studio workspace with reduced contrast.",
        AppTheme.MidnightIndigo => "Deep indigo glass, luminous violet accents, and richer night-sky depth.",
        AppTheme.ObsidianGold => "Blackened metal, warm gold illumination, and cinematic luxury inspired by the new visualizer direction.",
        AppTheme.AuroraPulse => "Deep ocean-black surfaces with electric teal light and cool atmospheric depth.",
        _ => "The Studio suite signature: layered graphite metal, luminous violet accents, and premium tactile depth."
    };

    public bool IsDarkMonochromeTheme => CurrentTheme == AppTheme.DarkMonochrome;
    public bool IsOledBlackTheme => CurrentTheme == AppTheme.OledBlack;
    public bool IsGraphiteTheme => CurrentTheme == AppTheme.Graphite;
    public bool IsMidnightIndigoTheme => CurrentTheme == AppTheme.MidnightIndigo;
    public bool IsObsidianGoldTheme => CurrentTheme == AppTheme.ObsidianGold;
    public bool IsAuroraPulseTheme => CurrentTheme == AppTheme.AuroraPulse;

    public TypographyProfile CurrentTypography
    {
        get => _currentTypography;
        private set
        {
            if (SetProperty(ref _currentTypography, value))
            {
                NotifyTypographySelection();
            }
        }
    }

    public string TypographyName => CurrentTypography switch
    {
        TypographyProfile.Precision => "Precision",
        TypographyProfile.Clear => "Clear",
        _ => "Cadence"
    };

    public string TypographyFamilyName => CurrentTypography switch
    {
        TypographyProfile.Precision => "Bahnschrift",
        TypographyProfile.Clear => "Verdana",
        _ => "Segoe UI Variable"
    };

    public string TypographyDescription => CurrentTypography switch
    {
        TypographyProfile.Precision => "A strong geometric presentation with a technical edge and substantial letterforms.",
        TypographyProfile.Clear => "Wide, distinct shapes and generous spacing for maximum readability.",
        _ => "The official Cadence Studio identity: modern Windows typography with excellent clarity."
    };

    public bool IsCadenceTypography => CurrentTypography == TypographyProfile.Cadence;
    public bool IsPrecisionTypography => CurrentTypography == TypographyProfile.Precision;
    public bool IsClearTypography => CurrentTypography == TypographyProfile.Clear;

    public TextScale CurrentTextSize
    {
        get => _currentTextSize;
        private set
        {
            if (SetProperty(ref _currentTextSize, value))
            {
                NotifyTextSizeSelection();
            }
        }
    }

    public string TextSizeName => CurrentTextSize switch
    {
        TextScale.Large => "Large",
        TextScale.ExtraLarge => "Extra Large",
        _ => "Standard"
    };

    public string TextSizeDescription => CurrentTextSize switch
    {
        TextScale.Large => "More comfortable interface text with modestly larger headings, controls, and lyrics.",
        TextScale.ExtraLarge => "Maximum built-in readability for reading-glasses and distance viewing.",
        _ => "Balanced sizing for normal desktop viewing while retaining substantial font weights."
    };

    public bool IsStandardTextSize => CurrentTextSize == TextScale.Standard;
    public bool IsLargeTextSize => CurrentTextSize == TextScale.Large;
    public bool IsExtraLargeTextSize => CurrentTextSize == TextScale.ExtraLarge;

    public bool ReduceMotion
    {
        get => _reduceMotion;
        private set
        {
            if (SetProperty(ref _reduceMotion, value))
            {
                OnPropertyChanged(nameof(MotionPreferenceLabel));
                OnPropertyChanged(nameof(MotionPreferenceDescription));
            }
        }
    }

    public bool IsNowPlayingExpanded
    {
        get => _isNowPlayingExpanded;
        private set
        {
            if (SetProperty(ref _isNowPlayingExpanded, value))
            {
                OnPropertyChanged(nameof(NowPlayingModeLabel));
                OnPropertyChanged(nameof(NowPlayingModeDescription));
            }
        }
    }

    public string NowPlayingModeLabel => IsNowPlayingExpanded ? "Compact" : "Expand";

    public string NowPlayingModeDescription => IsNowPlayingExpanded
        ? "Expanded artwork-led Now Playing focus is active."
        : "Compact Now Playing keeps Library and Queue visible.";

    public string MotionPreferenceLabel => ReduceMotion ? "Reduced motion" : "Subtle motion";

    public string MotionPreferenceDescription => ReduceMotion
        ? "Nonessential hover scaling is disabled while responsive feedback remains."
        : "Crisp hover and press feedback is enabled for the Eye Candy interface.";

    private void InitializeAppearanceState(
        AppTheme theme,
        TypographyProfile typography,
        TextScale textSize,
        bool reduceMotion,
        bool isNowPlayingExpanded)
    {
        _currentTheme = Enum.IsDefined(theme) ? theme : AppTheme.DarkMonochrome;
        _currentTypography = Enum.IsDefined(typography) ? typography : TypographyProfile.Cadence;
        _currentTextSize = Enum.IsDefined(textSize) ? textSize : TextScale.Standard;
        _reduceMotion = reduceMotion;
        _isNowPlayingExpanded = isNowPlayingExpanded;
    }

    private void InitializeAppearanceCommands()
    {
        ApplyThemeCommand = new RelayCommand<string>(themeName =>
        {
            if (!Enum.TryParse<AppTheme>(themeName, ignoreCase: true, out var theme))
            {
                return;
            }

            ThemeManager.Apply(theme);
            var changed = CurrentTheme != theme;
            CurrentTheme = theme;
            if (!changed)
            {
                NotifyThemeSelection();
            }

            _settings.Theme = theme;
            RefreshVisualizerBrushes();
            StatusText = $"Theme changed to {ThemeName}.";
        });

        ApplyTypographyCommand = new RelayCommand<string>(profileName =>
        {
            if (!Enum.TryParse<TypographyProfile>(profileName, ignoreCase: true, out var profile))
            {
                return;
            }

            TypographyManager.Apply(profile, CurrentTextSize);
            var changed = CurrentTypography != profile;
            CurrentTypography = profile;
            if (!changed)
            {
                NotifyTypographySelection();
            }

            _settings.Typography = profile;
            StatusText = $"Typography changed to {TypographyName} • {TypographyFamilyName}.";
        });

        ApplyTextSizeCommand = new RelayCommand<string>(scaleName =>
        {
            if (!Enum.TryParse<TextScale>(scaleName, ignoreCase: true, out var scale))
            {
                return;
            }

            TypographyManager.Apply(CurrentTypography, scale);
            var changed = CurrentTextSize != scale;
            CurrentTextSize = scale;
            if (!changed)
            {
                NotifyTextSizeSelection();
            }

            _settings.TextSize = scale;
            StatusText = $"Interface text size changed to {TextSizeName}.";
        });

        ToggleReducedMotionCommand = new RelayCommand(() =>
        {
            ReduceMotion = !ReduceMotion;
            _settings.ReduceMotion = ReduceMotion;
            StatusText = ReduceMotion
                ? "Reduced motion enabled."
                : "Subtle Eye Candy motion enabled.";
        });

        ToggleNowPlayingModeCommand = new RelayCommand(() =>
        {
            IsNowPlayingExpanded = !IsNowPlayingExpanded;
            _settings.IsNowPlayingExpanded = IsNowPlayingExpanded;
            StatusText = IsNowPlayingExpanded
                ? "Expanded Now Playing focus enabled."
                : "Compact Library workspace restored.";
        });
    }

    private void NotifyThemeSelection()
    {
        OnPropertyChanged(nameof(ThemeName));
        OnPropertyChanged(nameof(ThemeDescription));
        OnPropertyChanged(nameof(IsDarkMonochromeTheme));
        OnPropertyChanged(nameof(IsOledBlackTheme));
        OnPropertyChanged(nameof(IsGraphiteTheme));
        OnPropertyChanged(nameof(IsMidnightIndigoTheme));
        OnPropertyChanged(nameof(IsObsidianGoldTheme));
        OnPropertyChanged(nameof(IsAuroraPulseTheme));
    }

    private void NotifyTypographySelection()
    {
        OnPropertyChanged(nameof(TypographyName));
        OnPropertyChanged(nameof(TypographyFamilyName));
        OnPropertyChanged(nameof(TypographyDescription));
        OnPropertyChanged(nameof(IsCadenceTypography));
        OnPropertyChanged(nameof(IsPrecisionTypography));
        OnPropertyChanged(nameof(IsClearTypography));
    }

    private void NotifyTextSizeSelection()
    {
        OnPropertyChanged(nameof(TextSizeName));
        OnPropertyChanged(nameof(TextSizeDescription));
        OnPropertyChanged(nameof(IsStandardTextSize));
        OnPropertyChanged(nameof(IsLargeTextSize));
        OnPropertyChanged(nameof(IsExtraLargeTextSize));
    }
}
