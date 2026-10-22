using System;
using System.Linq;
using SharpHook.Data;

namespace LaunchPad.Ignite.App.Services.Hotkeys;

public sealed record GlobalActivationHotkey(bool Control, bool Alt, bool Shift, bool Meta, KeyCode Key)
{
    public const string DefaultValue = "Alt+Space";

    public string DisplayName => string.Join("+", new[]
    {
        Control ? "Ctrl" : null,
        Alt ? "Alt" : null,
        Shift ? "Shift" : null,
        Meta ? "Win" : null,
        FormatKey(Key)
    }.Where(part => part is not null));

    public bool Matches(bool control, bool alt, bool shift, bool meta, KeyCode key)
        => Control == control && Alt == alt && Shift == shift && Meta == meta && Key == key;

    public string ToSettingValue() => $"{(Control ? "Ctrl+" : string.Empty)}{(Alt ? "Alt+" : string.Empty)}{(Shift ? "Shift+" : string.Empty)}{(Meta ? "Win+" : string.Empty)}{Key}";

    public static bool TryParse(string? value, out GlobalActivationHotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var control = false;
        var alt = false;
        var shift = false;
        var meta = false;
        foreach (var modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    control = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "meta":
                    meta = true;
                    break;
                default:
                    return false;
            }
        }

        if (!control && !alt && !shift && !meta)
        {
            return false;
        }

        var keyName = parts[^1].StartsWith("Vc", StringComparison.OrdinalIgnoreCase)
            ? parts[^1]
            : ToHookKeyName(parts[^1]);
        if (!Enum.TryParse<KeyCode>(keyName, ignoreCase: true, out var key)
            || IsModifierKey(key))
        {
            return false;
        }

        hotkey = new GlobalActivationHotkey(control, alt, shift, meta, key);
        return true;
    }

    public static bool TryCreate(bool control, bool alt, bool shift, bool meta, string keyName, out GlobalActivationHotkey hotkey)
    {
        hotkey = null!;
        if ((!control && !alt && !shift && !meta)
            || string.IsNullOrWhiteSpace(keyName)
            || !TryParse($"{(control ? "Ctrl+" : string.Empty)}{(alt ? "Alt+" : string.Empty)}{(shift ? "Shift+" : string.Empty)}{(meta ? "Win+" : string.Empty)}{keyName}", out var parsed))
        {
            return false;
        }

        hotkey = parsed;
        return true;
    }

    private static string ToHookKeyName(string keyName)
    {
        var normalized = keyName switch
        {
            "Space" => "VcSpace",
            "D0" => "Vc0",
            "D1" => "Vc1",
            "D2" => "Vc2",
            "D3" => "Vc3",
            "D4" => "Vc4",
            "D5" => "Vc5",
            "D6" => "Vc6",
            "D7" => "Vc7",
            "D8" => "Vc8",
            "D9" => "Vc9",
            "OemMinus" => "VcMinus",
            "OemPlus" => "VcEquals",
            "OemOpenBrackets" => "VcOpenBracket",
            "OemCloseBrackets" => "VcCloseBracket",
            "OemPipe" => "VcBackslash",
            "OemSemicolon" => "VcSemicolon",
            "OemQuotes" => "VcQuote",
            "OemComma" => "VcComma",
            "OemPeriod" => "VcPeriod",
            "OemQuestion" => "VcSlash",
            _ => keyName.StartsWith("Vc", StringComparison.OrdinalIgnoreCase) ? keyName : $"Vc{keyName}"
        };
        return normalized;
    }

    private static string FormatKey(KeyCode key)
    {
        var name = key.ToString();
        if (name.StartsWith("VcNumPad", StringComparison.Ordinal))
        {
            return $"NumPad {name[8..]}";
        }

        if (name.StartsWith("Vc", StringComparison.Ordinal))
        {
            name = name[2..];
        }

        return name switch
        {
            "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" => name,
            "Minus" => "-",
            "Equals" => "=",
            "OpenBracket" => "[",
            "CloseBracket" => "]",
            "Backslash" => "\\",
            "Semicolon" => ";",
            "Quote" => "'",
            "Comma" => ",",
            "Period" => ".",
            "Slash" => "/",
            _ => name
        };
    }

    private static bool IsModifierKey(KeyCode key)
    {
        var name = key.ToString();
        return name.Contains("Alt", StringComparison.Ordinal)
            || name.Contains("Control", StringComparison.Ordinal)
            || name.Contains("Shift", StringComparison.Ordinal)
            || name.Contains("Meta", StringComparison.Ordinal);
    }
}
