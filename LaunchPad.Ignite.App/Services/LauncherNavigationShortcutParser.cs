using System;
using Avalonia.Input;
using LaunchPad.Ignite.App.Models;

namespace LaunchPad.Ignite.App.Services;

public static class LauncherNavigationShortcutParser
{
    public static bool TryParse(Key key, KeyModifiers modifiers, out LauncherNavigationTarget target)
    {
        target = default;
        if (modifiers != KeyModifiers.Control)
        {
            return false;
        }

        switch (key)
        {
            case Key.I:
                target = LauncherNavigationTarget.ImportExport;
                return true;
            case Key.H:
                target = LauncherNavigationTarget.Help;
                return true;
            case Key.OemPeriod:
            case Key.Decimal:
                target = LauncherNavigationTarget.Settings;
                return true;
            default:
                return false;
        }
    }
}