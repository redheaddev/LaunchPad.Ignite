using System;
using LaunchPad.Ignite.App.Services.Hotkeys;

namespace LaunchPad.Ignite.App.Services.WindowBehavior.Linux;

internal static class WaylandShortcutTrigger
{
    public static bool TryFormat(GlobalActivationHotkey hotkey, out string trigger)
    {
        var key = FormatKey(hotkey.Key.ToString());
        if (key is null)
        {
            trigger = string.Empty;
            return false;
        }

        trigger = $"{(hotkey.Control ? "<Control>" : string.Empty)}{(hotkey.Alt ? "<Alt>" : string.Empty)}{(hotkey.Shift ? "<Shift>" : string.Empty)}{(hotkey.Meta ? "<Super>" : string.Empty)}{key}";
        return true;
    }

    private static string? FormatKey(string key)
    {
        if (key.Length == 3 && key.StartsWith("Vc", StringComparison.Ordinal))
        {
            return key[2] is >= 'A' and <= 'Z'
                ? char.ToLowerInvariant(key[2]).ToString()
                : key[2].ToString();
        }

        if (key.StartsWith("VcF", StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(3), out var functionKey)
            && functionKey is >= 1 and <= 35)
        {
            return $"F{functionKey}";
        }

        if (key.StartsWith("VcNumPad", StringComparison.Ordinal)
            && key.Length == "VcNumPad".Length + 1
            && char.IsAsciiDigit(key[^1]))
        {
            return $"KP_{key[^1]}";
        }

        return key switch
        {
            "VcSpace" => "space",
            "VcEnter" => "Return",
            "VcEscape" => "Escape",
            "VcTab" => "Tab",
            "VcBackspace" => "BackSpace",
            "VcDelete" => "Delete",
            "VcInsert" => "Insert",
            "VcHome" => "Home",
            "VcEnd" => "End",
            "VcPageUp" => "Page_Up",
            "VcPageDown" => "Page_Down",
            "VcLeft" => "Left",
            "VcRight" => "Right",
            "VcUp" => "Up",
            "VcDown" => "Down",
            "VcMinus" => "minus",
            "VcEquals" => "equal",
            "VcOpenBracket" => "bracketleft",
            "VcCloseBracket" => "bracketright",
            "VcBackslash" => "backslash",
            "VcSemicolon" => "semicolon",
            "VcQuote" => "apostrophe",
            "VcComma" => "comma",
            "VcPeriod" => "period",
            "VcSlash" => "slash",
            _ => null
        };
    }
}