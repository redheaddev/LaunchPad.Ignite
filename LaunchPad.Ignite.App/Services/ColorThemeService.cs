using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;

namespace LaunchPad.Ignite.App.Services;

public static class ColorThemeService
{
    private const string LegacyCharcoalMigrationKey = "app:themes:legacyCharcoalMigrated";
    private static readonly Regex HexColor = new("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, string> LegacyCharcoalPalette = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        [nameof(ColorTheme.WindowBackground)] = "#FF202321",
        [nameof(ColorTheme.PanelBackground)] = "#FF292D28",
        [nameof(ColorTheme.InputBackground)] = "#FF171A18",
        [nameof(ColorTheme.Border)] = "#FF454B40",
        [nameof(ColorTheme.Text)] = "#FFF1F3E8",
        [nameof(ColorTheme.SecondaryText)] = "#FFB9C0AC",
        [nameof(ColorTheme.MutedText)] = "#FF858D78",
        [nameof(ColorTheme.Accent)] = "#FFA3E635",
        [nameof(ColorTheme.Highlight)] = "#FF3A4230",
        [nameof(ColorTheme.TextSelection)] = "#FFA3E635",
        [nameof(ColorTheme.CommandFocus)] = "#FFA3E635",
        [nameof(ColorTheme.NavigationSelected)] = "#FF3A4230",
        [nameof(ColorTheme.Target)] = "#FFFACC15",
        [nameof(ColorTheme.RequiredArgument)] = "#FFA3E635",
        [nameof(ColorTheme.OptionalArgument)] = "#FFB9C0AC",
        [nameof(ColorTheme.Checked)] = "#FFFACC15",
        [nameof(ColorTheme.Status)] = "#FF858D78"
    };
    public static event System.Action? ThemeChanged;

    public static IReadOnlyList<ColorTheme> LoadThemes(ProfileSettingsStore settingsStore)
    {
        var storedThemes = settingsStore.GetThemes();
        var builtIn = Load(ColorTheme.Default, settingsStore);
        var customThemes = storedThemes
            .Where(theme => !string.Equals(theme.Id, ColorTheme.Default.Id, System.StringComparison.OrdinalIgnoreCase))
            .Select(theme => Load(ColorTheme.Default with { Id = theme.Id, Name = theme.Name }, settingsStore));

        return [builtIn, .. customThemes];
    }

    public static bool IsValidHexColor(string? value)
        => value is not null && HexColor.IsMatch(value);

    public static void MigrateLegacyThemes(ProfileSettingsStore settingsStore)
    {
        if (settingsStore.IsLegacyCharcoalMigrated)
        {
            return;
        }

        if (!settingsStore.TryGetTheme(ColorTheme.CharcoalLimeYellow.Id, out _))
        {
            settingsStore.SetTheme(ToProfileTheme(ColorTheme.CharcoalLimeYellow));
        }

        if (settingsStore.TryGetTheme(ColorTheme.CharcoalLimeYellow.Id, out var charcoal))
        {
            var colors = new Dictionary<string, string>(charcoal.Colors, System.StringComparer.OrdinalIgnoreCase);
            foreach (var colorEntry in ColorTheme.EditableColors)
            {
                if ((!colors.TryGetValue(colorEntry.Property, out var existing) || !HexColor.IsMatch(existing))
                    && LegacyCharcoalPalette.TryGetValue(colorEntry.Property, out var defaultColor))
                {
                    colors[colorEntry.Property] = defaultColor;
                }
            }

            settingsStore.SetTheme(charcoal with { Colors = colors });
        }

        settingsStore.MarkLegacyCharcoalMigrated();
    }

    public static bool DeleteTheme(string themeId, ProfileSettingsStore settingsStore)
    {
        if (string.IsNullOrWhiteSpace(themeId)
            || string.Equals(themeId, ColorTheme.Default.Id, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!settingsStore.TryGetTheme(themeId, out _))
        {
            return false;
        }

        settingsStore.RemoveTheme(themeId);
        return true;
    }

    public static bool RenameTheme(string themeId, string? name, ProfileSettingsStore settingsStore)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(themeId)
            || string.Equals(themeId, ColorTheme.Default.Id, System.StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        if (!settingsStore.TryGetTheme(themeId, out var theme))
        {
            return false;
        }

        settingsStore.SetTheme(theme with { Name = normalizedName });
        return true;
    }

    public static ColorTheme Resolve(string? id, ProfileSettingsStore settingsStore)
        => LoadThemes(settingsStore).FirstOrDefault(theme => string.Equals(theme.Id, id, System.StringComparison.OrdinalIgnoreCase))
            ?? ColorTheme.Default;

    public static ColorTheme Load(ColorTheme theme, ProfileSettingsStore settingsStore)
    {
        if (!settingsStore.TryGetTheme(theme.Id, out var storedTheme))
        {
            return theme;
        }

        var colors = new Dictionary<string, string>(storedTheme.Colors, System.StringComparer.OrdinalIgnoreCase);
        foreach (var colorEntry in ColorTheme.EditableColors)
        {
            var value = colors.GetValueOrDefault(colorEntry.Property, string.Empty);
            if (IsValidHexColor(value) && Color.TryParse(value, out var color))
            {
                theme = theme.WithColor(colorEntry.Property, color.ToString());
            }
            else if (!string.IsNullOrWhiteSpace(value))
            {
                colors.Remove(colorEntry.Property);
            }
        }

        if (colors.Count != storedTheme.Colors.Count)
        {
            settingsStore.SetTheme(storedTheme with { Colors = colors });
        }

        return theme;
    }

    public static void SaveColorOverrides(string themeId, IEnumerable<KeyValuePair<string, string>> colors, ProfileSettingsStore settingsStore)
    {
        var current = settingsStore.TryGetTheme(themeId, out var storedTheme)
            ? storedTheme
            : new ProfileThemeSettings(themeId, themeId, new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase));
        var updatedColors = new Dictionary<string, string>(current.Colors, System.StringComparer.OrdinalIgnoreCase);
        foreach (var (property, value) in colors)
        {
            updatedColors[property] = value;
        }

        settingsStore.SetTheme(current with { Colors = updatedColors });
    }

    public static ProfileThemeSettings ToProfileTheme(ColorTheme theme)
        => new(theme.Id, theme.Name, ColorTheme.EditableColors.ToDictionary(
            color => color.Property,
            color => GetColor(theme, color.Property),
            System.StringComparer.OrdinalIgnoreCase));

    public static string ColorSettingKey(string themeId, string property)
        => $"app:theme:{themeId}:{property}";

    public static string CustomThemeNameKey(string themeId)
        => $"app:customTheme:{themeId}:name";

    private static string GetColor(ColorTheme theme, string property) => property switch
    {
        nameof(ColorTheme.WindowBackground) => theme.WindowBackground,
        nameof(ColorTheme.PanelBackground) => theme.PanelBackground,
        nameof(ColorTheme.InputBackground) => theme.InputBackground,
        nameof(ColorTheme.Border) => theme.Border,
        nameof(ColorTheme.Text) => theme.Text,
        nameof(ColorTheme.SecondaryText) => theme.SecondaryText,
        nameof(ColorTheme.MutedText) => theme.MutedText,
        nameof(ColorTheme.Accent) => theme.Accent,
        nameof(ColorTheme.Highlight) => theme.Highlight,
        nameof(ColorTheme.TextSelection) => theme.TextSelection,
        nameof(ColorTheme.CommandFocus) => theme.CommandFocus,
        nameof(ColorTheme.NavigationSelected) => theme.NavigationSelected,
        nameof(ColorTheme.Target) => theme.Target,
        nameof(ColorTheme.RequiredArgument) => theme.RequiredArgument,
        nameof(ColorTheme.OptionalArgument) => theme.OptionalArgument,
        nameof(ColorTheme.Checked) => theme.Checked,
        nameof(ColorTheme.Status) => theme.Status,
        _ => string.Empty
    };

    public static void Apply(ColorTheme theme)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        var resources = application.Resources;
        resources["ThemeWindowBackground"] = Brush(theme.WindowBackground);
        resources["ThemePanelBackground"] = Brush(theme.PanelBackground);
        resources["ThemeInputBackground"] = Brush(theme.InputBackground);
        resources["ThemeBorder"] = Brush(theme.Border);
        resources["ThemeText"] = Brush(theme.Text);
        resources["ThemeSecondaryText"] = Brush(theme.SecondaryText);
        resources["ThemeMutedText"] = Brush(theme.MutedText);
        resources["ThemeAccent"] = Brush(theme.Accent);
        resources["ThemeHighlight"] = Brush(theme.Highlight);
        resources["ThemeTextSelection"] = Brush(theme.TextSelection);
        resources["ThemeTextSelectionForeground"] = new SolidColorBrush(ContrastingTextColor(Color.Parse(theme.TextSelection)));
        resources["ThemeCommandFocus"] = Brush(theme.CommandFocus);
        resources["ThemeNavigationSelected"] = Brush(theme.NavigationSelected);
        var targetBrush = Brush(theme.Target);
        resources["ThemeTarget"] = targetBrush;
        resources["ThemePinkTarget"] = targetBrush;
        resources["ThemeRequiredArgument"] = Brush(theme.RequiredArgument);
        resources["ThemeOptionalArgument"] = Brush(theme.OptionalArgument);
        resources["ThemeChecked"] = Brush(theme.Checked);
        resources["ThemeStatus"] = Brush(theme.Status);
        resources["SelectedColorTheme"] = theme.Id;
        ThemeChanged?.Invoke();
    }

    private static Color ContrastingTextColor(Color background)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : System.Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linear(background.R)) + (0.7152 * Linear(background.G)) + (0.0722 * Linear(background.B));
        var blackContrast = (luminance + 0.05) / 0.05;
        var whiteContrast = 1.05 / (luminance + 0.05);
        return blackContrast >= whiteContrast ? Colors.Black : Colors.White;
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
