using System.Windows;
using System.Windows.Media;
using CadenceStudio.Core.Enums;

namespace CadenceStudio.App.Services;

public static class TypographyManager
{
    private sealed record TypographyPalette(
        string BodyFamily,
        string DisplayFamily,
        string TechnicalFamily);

    private sealed record ScalePalette(
        double Small,
        double Caption,
        double Body,
        double Control,
        double Navigation,
        double PanelTitle,
        double PageTitle,
        double CompactTrackTitle,
        double ExpandedTrackTitle,
        double QueueTitle,
        double Lyrics,
        double LyricsLineHeight,
        double DialogHero);

    private static readonly IReadOnlyDictionary<TypographyProfile, TypographyPalette> Profiles =
        new Dictionary<TypographyProfile, TypographyPalette>
        {
            [TypographyProfile.Cadence] = new(
                "Segoe UI Variable Text, Segoe UI",
                "Segoe UI Variable Display, Segoe UI",
                "Cascadia Mono, Consolas"),
            [TypographyProfile.Precision] = new(
                "Bahnschrift, Segoe UI",
                "Bahnschrift, Segoe UI",
                "Cascadia Mono, Consolas"),
            [TypographyProfile.Clear] = new(
                "Verdana, Segoe UI",
                "Verdana, Segoe UI",
                "Cascadia Mono, Consolas")
        };

    private static readonly IReadOnlyDictionary<TextScale, ScalePalette> Scales =
        new Dictionary<TextScale, ScalePalette>
        {
            [TextScale.Standard] = new(11, 12, 13, 12, 14, 18, 30, 22, 34, 14, 15, 27, 28),
            [TextScale.Large] = new(12, 13, 14, 13, 15, 20, 33, 24, 37, 15, 17, 30, 30),
            [TextScale.ExtraLarge] = new(13, 14, 15, 14, 16, 22, 36, 27, 40, 17, 19, 34, 33)
        };

    public static TypographyProfile CurrentProfile { get; private set; } = TypographyProfile.Cadence;
    public static TextScale CurrentScale { get; private set; } = TextScale.Standard;

    public static void Apply(TypographyProfile profile, TextScale scale)
    {
        if (!Profiles.TryGetValue(profile, out var typography))
        {
            profile = TypographyProfile.Cadence;
            typography = Profiles[profile];
        }

        if (!Scales.TryGetValue(scale, out var sizing))
        {
            scale = TextScale.Standard;
            sizing = Scales[scale];
        }

        CurrentProfile = profile;
        CurrentScale = scale;

        SetResourceValue("AppBodyFontFamily", new FontFamily(typography.BodyFamily));
        SetResourceValue("AppDisplayFontFamily", new FontFamily(typography.DisplayFamily));
        SetResourceValue("AppTechnicalFontFamily", new FontFamily(typography.TechnicalFamily));

        SetResourceValue("AppSmallFontSize", sizing.Small);
        SetResourceValue("AppCaptionFontSize", sizing.Caption);
        SetResourceValue("AppBodyFontSize", sizing.Body);
        SetResourceValue("AppControlFontSize", sizing.Control);
        SetResourceValue("AppNavigationFontSize", sizing.Navigation);
        SetResourceValue("AppPanelTitleFontSize", sizing.PanelTitle);
        SetResourceValue("AppPageTitleFontSize", sizing.PageTitle);
        SetResourceValue("AppCompactTrackTitleFontSize", sizing.CompactTrackTitle);
        SetResourceValue("AppExpandedTrackTitleFontSize", sizing.ExpandedTrackTitle);
        SetResourceValue("AppQueueTitleFontSize", sizing.QueueTitle);
        SetResourceValue("AppLyricsFontSize", sizing.Lyrics);
        SetResourceValue("AppLyricsLineHeight", sizing.LyricsLineHeight);
        SetResourceValue("AppDialogHeroFontSize", sizing.DialogHero);
    }

    private static void SetResourceValue(object key, object value)
    {
        if (!TrySetResource(Application.Current.Resources, key, value))
        {
            Application.Current.Resources[key] = value;
        }
    }

    private static bool TrySetResource(ResourceDictionary dictionary, object key, object value)
    {
        if (dictionary.Contains(key))
        {
            dictionary[key] = value;
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TrySetResource(merged, key, value))
            {
                return true;
            }
        }

        return false;
    }
}
