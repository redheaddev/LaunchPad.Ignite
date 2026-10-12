using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media;

namespace LaunchPad.Ignite.App.Services;

public static class ThemePaletteImporter
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Regex PaletteRegex = new("data-palette=[\\\"']([0-9A-Fa-f-]+)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HexRegex = new("data-hex=[\\\"']#?([0-9A-Fa-f]{6})[\\\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int MaximumPageBytes = 2 * 1024 * 1024;

    public static async Task<ColorTheme> ImportAsync(Uri uri, ColorTheme theme)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Use a secure HTTPS palette URL.", nameof(uri));
        }

        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumPageBytes)
        {
            throw new InvalidDataException("The palette page is larger than 2 MB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + bytesRead > MaximumPageBytes)
            {
                throw new InvalidDataException("The palette page is larger than 2 MB.");
            }

            buffer.Write(chunk, 0, bytesRead);
        }

        var html = Encoding.UTF8.GetString(buffer.ToArray());
        var colors = ParseColors(html);
        if (colors.Count < 2)
        {
            throw new InvalidDataException("No palette with at least two colors was found at that URL.");
        }

        return CreateTheme(colors, theme);
    }

    public static ColorTheme CreateTheme(IReadOnlyList<string> colors, ColorTheme theme)
    {
        if (colors.Count < 2)
        {
            throw new ArgumentException("A palette must have at least two colors.", nameof(colors));
        }

        var ranked = colors.Select(value => (Value: value, Color: Color.Parse(value)))
            .OrderBy(entry => Luminance(entry.Color))
            .ToArray();
        var darkest = ranked[0].Color;
        var middle = ranked[ranked.Length / 2].Color;
        var brightest = ranked[^1].Color;
        var accent = ranked
            .Where(entry => Luminance(entry.Color) is > 0.08 and < 0.62)
            .OrderByDescending(entry => Saturation(entry.Color) * (1 - Math.Abs(Luminance(entry.Color) - 0.35)))
            .FirstOrDefault(ranked.OrderByDescending(entry => Saturation(entry.Color)).First())
            .Color;
        var target = ranked
            .Where(entry => entry.Color != accent)
            .OrderByDescending(entry => Saturation(entry.Color))
            .FirstOrDefault((Value: "", Color: accent))
            .Color;
        var secondBrightest = Blend(brightest, middle, 0.28);
        var muted = Blend(brightest, middle, 0.7);
        var targetColor = target == accent ? brightest : target;

        return theme with
        {
            WindowBackground = Format(darkest),
            PanelBackground = Format(Blend(darkest, middle, 0.12)),
            InputBackground = Format(Blend(darkest, Color.FromRgb(0, 0, 0), 0.12)),
            Border = Format(Blend(darkest, middle, 0.4)),
            Text = Format(brightest),
            SecondaryText = Format(secondBrightest),
            MutedText = Format(muted),
            Accent = Format(accent),
            Highlight = Format(Blend(darkest, accent, 0.38)),
            TextSelection = "#FFFF008C",
            CommandFocus = Format(accent),
            NavigationSelected = Format(Blend(darkest, accent, 0.38)),
            Target = Format(targetColor),
            RequiredArgument = Format(accent),
            OptionalArgument = Format(secondBrightest),
            Checked = Format(accent),
            Status = Format(muted)
        };
    }

    private static Color Blend(Color start, Color end, double amount)
        => Color.FromRgb(
            (byte)Math.Round(start.R + ((end.R - start.R) * amount)),
            (byte)Math.Round(start.G + ((end.G - start.G) * amount)),
            (byte)Math.Round(start.B + ((end.B - start.B) * amount)));

    private static string Format(Color color) => $"#FF{color.R:X2}{color.G:X2}{color.B:X2}";

    public static IReadOnlyList<string> ParseColors(string html)
    {
        var palette = PaletteRegex.Match(html);
        var values = palette.Success
            ? palette.Groups[1].Value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            : HexRegex.Matches(html).Select(match => match.Groups[1].Value).ToArray();

        return values
            .Where(value => value.Length == 6 && value.All(Uri.IsHexDigit))
            .Select(value => $"#FF{value.ToUpperInvariant()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    private static double Saturation(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }
}
