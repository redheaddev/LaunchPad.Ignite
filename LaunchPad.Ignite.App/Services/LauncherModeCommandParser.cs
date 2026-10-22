using System;
using LaunchPad.Ignite.App.Services.WindowBehavior;

namespace LaunchPad.Ignite.App.Services;

public static class LauncherModeCommandParser
{
    public static bool TryParse(string action, LauncherWindowMode currentMode, out LauncherWindowMode targetMode)
    {
        targetMode = currentMode;
        switch (action.ToLowerInvariant())
        {
            case "toggle":
                targetMode = currentMode == LauncherWindowMode.Main
                    ? LauncherWindowMode.Mini
                    : LauncherWindowMode.Main;
                return true;
            case "main":
                targetMode = LauncherWindowMode.Main;
                return true;
            case "mini":
                targetMode = LauncherWindowMode.Mini;
                return true;
            default:
                return false;
        }
    }
}
