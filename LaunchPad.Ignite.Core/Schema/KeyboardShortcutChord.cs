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
        var numpad = false;
        string? key = null;
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
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
            else if (string.Equals(part, "num", StringComparison.OrdinalIgnoreCase)
                && key is null
                && !numpad
                && index + 1 < parts.Length
                && parts[index + 1].Length == 1
                && parts[index + 1][0] is >= '0' and <= '9')
            {
                numpad = true;
                key = $"num{parts[++index]}";
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
        if (Key.StartsWith("num", StringComparison.Ordinal) && Key.Length == 4)
        {
            parts.Add("num");
            parts.Add(Key[3].ToString());
        }
        else
        {
            parts.Add(Key);
        }
        return string.Join('+', parts);
    }
}
