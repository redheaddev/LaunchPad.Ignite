using System;
using System.Linq;

namespace LaunchPad.Ignite.Core.Schema;

public sealed record KeyboardShortcutChord(
    bool Control,
    bool Alt,
    bool Shift,
    bool Meta,
    string Key)
{
    public static bool TryParse(string? value, out KeyboardShortcutChord chord)
    {
        chord = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        var control = false;
        var alt = false;
        var shift = false;
        var meta = false;
        string? key = null;
        foreach (var part in parts)
        {
            if (string.Equals(part, "ctrl", StringComparison.OrdinalIgnoreCase))
            {
                if (control) return false;
                control = true;
            }
            else if (string.Equals(part, "alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt) return false;
                alt = true;
            }
            else if (string.Equals(part, "shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift) return false;
                shift = true;
            }
            else if (string.Equals(part, "meta", StringComparison.OrdinalIgnoreCase))
            {
                if (meta) return false;
                meta = true;
            }
            else if (key is null && part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
            {
                key = part.ToLowerInvariant();
            }
            else
            {
                return false;
            }
        }

        if (key is null || !(control || alt || shift || meta))
        {
            return false;
        }

        chord = new KeyboardShortcutChord(control, alt, shift, meta, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new System.Collections.Generic.List<string>();
        if (Control) parts.Add("ctrl");
        if (Alt) parts.Add("alt");
        if (Shift) parts.Add("shift");
        if (Meta) parts.Add("meta");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}
