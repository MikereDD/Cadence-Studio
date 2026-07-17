using System.Windows;
using System.Windows.Media;
using CadenceStudio.Core.Enums;

namespace CadenceStudio.App.Services;

public static class ThemeManager
{
    private sealed record ThemePalette(
        Color Window,
        Color Sidebar,
        Color Surface,
        Color SurfaceRaised,
        Color SurfaceHover,
        Color Border,
        Color BorderSoft,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color Accent,
        Color AccentText,
        Color Danger,
        Color Success,
        Color ControlFill,
        Color ControlThumb,
        Color ControlActive,
        Color AccentSoft,
        Color WindowDepthTop,
        Color WindowDepthMiddle,
        Color WindowDepthBottom,
        Color AlbumTop,
        Color AlbumMiddle,
        Color AlbumBottom,
        Color VisualizerBand0,
        Color VisualizerBand1,
        Color VisualizerBand2,
        Color VisualizerBand3,
        Color VisualizerBand4,
        Color VisualizerBand5,
        Color VisualizerBand6,
        Color VisualizerCore,
        Color VisualizerAtmosphere);

    private static readonly IReadOnlyDictionary<AppTheme, ThemePalette> Palettes =
        new Dictionary<AppTheme, ThemePalette>
        {
            [AppTheme.DarkMonochrome] = Palette(
                "#07090C", "#0B0E13", "#10141A", "#181E27", "#242C38",
                "#394351", "#202730", "#F6F7F9", "#B9C0CA", "#7D8795",
                "#D7DCE4", "#101216", "#D96F78", "#77C99A",
                "#8E98A6", "#F2F4F7", "#ADB6C2", "#3B424C",
                "#141922", "#090B0F", "#05070A", "#323A45", "#171C23", "#07090C",
                "#9EA7B2", "#B9C1CB", "#8C97A4", "#7A8694", "#AEB8C3", "#D8DEE5", "#F7F9FB", "#F3F6F9", "#65707D"),

            [AppTheme.OledBlack] = Palette(
                "#000000", "#020304", "#050607", "#0B0E11", "#12171C",
                "#2B323A", "#11161B", "#FAFBFC", "#C2C7CE", "#7C848D",
                "#E5E9EE", "#050607", "#DB7079", "#72CC98",
                "#8B949E", "#FFFFFF", "#BCC3CB", "#353B42",
                "#080A0D", "#020303", "#000000", "#23282E", "#090B0D", "#000000",
                "#C8CED5", "#E1E5EA", "#B8C0C9", "#A3ADB8", "#D2D8DE", "#F0F3F6", "#FFFFFF", "#FFFFFF", "#7C858F"),

            [AppTheme.Graphite] = Palette(
                "#101215", "#15181C", "#1B1F24", "#252A31", "#303740",
                "#4A535F", "#2B3138", "#F3F4F6", "#C0C5CC", "#848B95",
                "#C2CAD4", "#121417", "#D1737A", "#7CC49A",
                "#9098A2", "#E5E8EC", "#B2BAC4", "#454B54",
                "#20252B", "#14171A", "#0D0F12", "#3C434C", "#22272D", "#101215",
                "#73808C", "#98A3AE", "#66727E", "#84919E", "#AAB4BE", "#D0D6DC", "#F0F2F4", "#E7EBEF", "#56616D"),

            [AppTheme.MidnightIndigo] = Palette(
                "#050612", "#080B1C", "#0D1329", "#161F3D", "#24315B",
                "#4A5790", "#212B4B", "#F8F6FF", "#C9C6EB", "#898DB5",
                "#9C8BFF", "#0A0715", "#E2768F", "#72D1A1",
                "#8D95BD", "#E8E3FF", "#B8ADF0", "#403582",
                "#171E3D", "#090C1C", "#03040D", "#3A477B", "#171F40", "#050612",
                "#7868D8", "#8B78F0", "#706ACF", "#6686D8", "#87A4F2", "#B8B0FF", "#EEE9FF", "#BCAFFF", "#554B98"),

            [AppTheme.ObsidianGold] = Palette(
                "#080807", "#0E0D0A", "#15130F", "#201C14", "#2E281A",
                "#5A4B2A", "#302816", "#FFF8E8", "#D1C5AA", "#8F8269",
                "#D8A84E", "#FFF9ED", "#D76E6E", "#7CC795",
                "#9A8357", "#F2D79C", "#BFA36A", "#4A3918",
                "#18150E", "#0C0B08", "#060605", "#493818", "#20180C", "#090806",
                "#C47030", "#E29D39", "#B69759", "#56BCB4", "#4E9BCE", "#8475DC", "#D2E2F5", "#FFD978", "#9A5B25"),

            [AppTheme.AuroraPulse] = Palette(
                "#050B12", "#07101A", "#0B1723", "#112437", "#18364D",
                "#28506A", "#142B3B", "#EFFCFF", "#A8CFD8", "#6D909A",
                "#2AD4C8", "#FFFFFF", "#E06E85", "#69D8A5",
                "#4F8593", "#B9F6F0", "#64CFC8", "#164946",
                "#0B1E2D", "#06121D", "#03080D", "#174F5A", "#0B2732", "#041019",
                "#1AA89F", "#27D0C4", "#4AD8D0", "#63E1DC", "#56BBD8", "#7CA7F0", "#DDFEFF", "#7EF4EA", "#1B6C72")
        };

    public static AppTheme CurrentTheme { get; private set; } = AppTheme.DarkMonochrome;


    public static IReadOnlyList<Color> CurrentVisualizerBandColors
    {
        get
        {
            var p = Palettes[CurrentTheme];
            return new[] { p.VisualizerBand0, p.VisualizerBand1, p.VisualizerBand2, p.VisualizerBand3, p.VisualizerBand4, p.VisualizerBand5, p.VisualizerBand6 };
        }
    }

    public static void Apply(AppTheme theme)
    {
        if (!Palettes.TryGetValue(theme, out var palette))
        {
            theme = AppTheme.DarkMonochrome;
            palette = Palettes[theme];
        }

        CurrentTheme = theme;

        SetColorAndBrush("WindowColor", "WindowBrush", palette.Window);
        SetColorAndBrush("SidebarColor", "SidebarBrush", palette.Sidebar);
        SetColorAndBrush("SurfaceColor", "SurfaceBrush", palette.Surface);
        SetColorAndBrush("SurfaceRaisedColor", "SurfaceRaisedBrush", palette.SurfaceRaised);
        SetColorAndBrush("SurfaceHoverColor", "SurfaceHoverBrush", palette.SurfaceHover);
        SetColorAndBrush("BorderColor", "BorderBrush", palette.Border);
        SetColorAndBrush("BorderSoftColor", "BorderSoftBrush", palette.BorderSoft);
        SetColorAndBrush("TextPrimaryColor", "TextPrimaryBrush", palette.TextPrimary);
        SetColorAndBrush("TextSecondaryColor", "TextSecondaryBrush", palette.TextSecondary);
        SetColorAndBrush("TextMutedColor", "TextMutedBrush", palette.TextMuted);
        SetColorAndBrush("AccentColor", "AccentBrush", palette.Accent);
        SetColorAndBrush("AccentTextColor", "AccentTextBrush", palette.AccentText);
        SetColorAndBrush("DangerColor", "DangerBrush", palette.Danger);
        SetColorAndBrush("SuccessColor", "SuccessBrush", palette.Success);
        SetColorAndBrush("ControlFillColor", "ControlFillBrush", palette.ControlFill);
        SetColorAndBrush("ControlThumbColor", "ControlThumbBrush", palette.ControlThumb);
        SetColorAndBrush("ControlActiveColor", "ControlActiveBrush", palette.ControlActive);
        SetColorAndBrush("AccentSoftColor", "AccentSoftBrush", palette.AccentSoft);
        SetColorAndBrush("VisualizerBand0Color", "VisualizerBand0Brush", palette.VisualizerBand0);
        SetColorAndBrush("VisualizerBand1Color", "VisualizerBand1Brush", palette.VisualizerBand1);
        SetColorAndBrush("VisualizerBand2Color", "VisualizerBand2Brush", palette.VisualizerBand2);
        SetColorAndBrush("VisualizerBand3Color", "VisualizerBand3Brush", palette.VisualizerBand3);
        SetColorAndBrush("VisualizerBand4Color", "VisualizerBand4Brush", palette.VisualizerBand4);
        SetColorAndBrush("VisualizerBand5Color", "VisualizerBand5Brush", palette.VisualizerBand5);
        SetColorAndBrush("VisualizerBand6Color", "VisualizerBand6Brush", palette.VisualizerBand6);
        SetColorAndBrush("VisualizerCoreColor", "VisualizerCoreBrush", palette.VisualizerCore);
        SetColorAndBrush("VisualizerAtmosphereColor", "VisualizerAtmosphereBrush", palette.VisualizerAtmosphere);

        SetBrushColor(SystemColors.ControlBrushKey, palette.Surface);
        SetBrushColor(SystemColors.ControlLightBrushKey, palette.SurfaceRaised);
        SetBrushColor(SystemColors.ControlDarkBrushKey, palette.Window);
        SetBrushColor(SystemColors.WindowBrushKey, palette.Window);

        SetGradient("WindowDepthBrush", palette.WindowDepthTop, palette.WindowDepthMiddle, palette.WindowDepthBottom);
        SetGradient("AlbumArtPlaceholderBrush", palette.AlbumTop, palette.AlbumMiddle, palette.AlbumBottom);

        // Premium brushes contain gradient stops. Rebuild them explicitly on every theme
        // change so no frozen/dynamic gradient from the previous theme can bleed through.
        SetGradient("PremiumCardBrush", palette.SurfaceRaised, palette.Surface, palette.Window);
        SetGradient("PremiumCardHoverBrush", palette.SurfaceHover, palette.SurfaceRaised, palette.Surface);
        SetGradient("PremiumButtonBrush", palette.SurfaceHover, palette.SurfaceRaised, palette.Surface);
        SetGradient("PremiumButtonHoverBrush", palette.ControlActive, palette.SurfaceHover, palette.SurfaceRaised);
        SetGradient("PremiumAccentButtonBrush", palette.Accent, palette.AccentSoft, palette.SurfaceRaised);
        SetGradient("PremiumAccentButtonHoverBrush", palette.ControlActive, palette.Accent, palette.AccentSoft);
        SetGradient("PremiumMenuBrush", palette.SurfaceRaised, palette.Surface, palette.Window);
        SetTwoStopGradient("PremiumSelectionBrush", palette.AccentSoft, palette.SurfaceHover);
    }

    private static ThemePalette Palette(params string[] colors)
    {
        if (colors.Length != 33)
        {
            throw new ArgumentException("A Cadence theme palette requires exactly 33 colors.", nameof(colors));
        }

        var parsed = colors.Select(ParseColor).ToArray();
        return new ThemePalette(
            parsed[0], parsed[1], parsed[2], parsed[3], parsed[4], parsed[5], parsed[6], parsed[7],
            parsed[8], parsed[9], parsed[10], parsed[11], parsed[12], parsed[13], parsed[14], parsed[15],
            parsed[16], parsed[17], parsed[18], parsed[19], parsed[20], parsed[21], parsed[22], parsed[23],
            parsed[24], parsed[25], parsed[26], parsed[27], parsed[28], parsed[29], parsed[30], parsed[31], parsed[32]);
    }

    private static Color ParseColor(string value) =>
        (Color)ColorConverter.ConvertFromString(value);

    private static void SetColorAndBrush(string colorKey, string brushKey, Color color)
    {
        SetResourceValue(colorKey, color);
        SetBrushColor(brushKey, color);
    }

    private static void SetBrushColor(object key, Color color)
    {
        if (Application.Current.TryFindResource(key) is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                SetResourceValue(key, new SolidColorBrush(color));
            }
            else
            {
                brush.Color = color;
            }
        }
        else
        {
            SetResourceValue(key, new SolidColorBrush(color));
        }
    }

    private static void SetGradient(string key, Color first, Color middle, Color last)
    {
        if (Application.Current.TryFindResource(key) is not LinearGradientBrush gradient || gradient.GradientStops.Count < 3)
        {
            return;
        }

        if (gradient.IsFrozen)
        {
            var replacement = gradient.Clone();
            replacement.GradientStops[0].Color = first;
            replacement.GradientStops[1].Color = middle;
            replacement.GradientStops[2].Color = last;
            SetResourceValue(key, replacement);
            return;
        }

        gradient.GradientStops[0].Color = first;
        gradient.GradientStops[1].Color = middle;
        gradient.GradientStops[2].Color = last;
    }

    private static void SetTwoStopGradient(string key, Color first, Color last)
    {
        if (Application.Current.TryFindResource(key) is not LinearGradientBrush gradient || gradient.GradientStops.Count < 2)
        {
            return;
        }

        var target = gradient.IsFrozen ? gradient.Clone() : gradient;
        target.GradientStops[0].Color = first;
        target.GradientStops[^1].Color = last;
        if (gradient.IsFrozen)
        {
            SetResourceValue(key, target);
        }
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
