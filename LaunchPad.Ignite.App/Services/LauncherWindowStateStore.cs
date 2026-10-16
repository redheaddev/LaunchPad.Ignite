using System;
using System.Globalization;

namespace LaunchPad.Ignite.App.Services;

public enum LauncherWindowMode
{
    Main,
    Mini
}

public readonly record struct LauncherWindowPosition(int X, int Y);

public sealed class LauncherWindowStateStore
{
    private const string ModeSettingName = "app:windowMode";
    private const string PositionSettingPrefix = "app:windowPosition:";
    private readonly ProfileSettingsStore _settingsStore;

    public LauncherWindowStateStore(ProfileSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public LauncherWindowMode Mode
        => string.Equals(_settingsStore.GetValue(ModeSettingName, "main"), "mini", StringComparison.OrdinalIgnoreCase)
            ? LauncherWindowMode.Mini
            : LauncherWindowMode.Main;

    public void SetMode(LauncherWindowMode mode)
        => SetValueIfChanged(ModeSettingName, mode == LauncherWindowMode.Mini ? "mini" : "main");

    public bool TryGetPosition(LauncherWindowMode mode, out LauncherWindowPosition position)
    {
        var value = _settingsStore.GetValue(GetPositionSettingName(mode), string.Empty);
        var separator = value.IndexOf(',');
        if (separator > 0
            && int.TryParse(value.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(value.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            position = new LauncherWindowPosition(x, y);
            return true;
        }

        position = default;
        return false;
    }

    public void SetPosition(LauncherWindowMode mode, LauncherWindowPosition position)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{position.X},{position.Y}");
        SetValueIfChanged(GetPositionSettingName(mode), value);
    }

    private static string GetPositionSettingName(LauncherWindowMode mode)
        => $"{PositionSettingPrefix}{(mode == LauncherWindowMode.Mini ? "mini" : "main")}";

    private void SetValueIfChanged(string name, string value)
    {
        if (!string.Equals(_settingsStore.GetValue(name, string.Empty), value, StringComparison.Ordinal))
        {
            _settingsStore.SetValue(name, value);
        }
    }
}
