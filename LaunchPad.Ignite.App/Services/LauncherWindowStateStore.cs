using System;
using System.Globalization;

namespace LaunchPad.Ignite.App.Services;

public enum LauncherWindowMode
{
    Main,
    Mini
}

public readonly record struct LauncherWindowPosition(int X, int Y);
public readonly record struct LauncherWindowSize(int Width, int Height);

public sealed class LauncherWindowStateStore
{
    private const string ModeSettingName = "app:windowMode";
    private const string PositionSettingPrefix = "app:windowPosition:";
    private const string SizeSettingPrefix = "app:windowSize:";
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

    public bool TryGetSize(LauncherWindowMode mode, out LauncherWindowSize size)
    {
        var value = _settingsStore.GetValue(GetSizeSettingName(mode), string.Empty);
        var separator = value.IndexOf(',');
        if (separator > 0
            && int.TryParse(value.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            && int.TryParse(value.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            && width > 0
            && height > 0)
        {
            size = new LauncherWindowSize(width, height);
            return true;
        }

        size = default;
        return false;
    }

    public void SetSize(LauncherWindowMode mode, LauncherWindowSize size)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{size.Width},{size.Height}");
        SetValueIfChanged(GetSizeSettingName(mode), value);
    }

    private static string GetPositionSettingName(LauncherWindowMode mode)
        => $"{PositionSettingPrefix}{(mode == LauncherWindowMode.Mini ? "mini" : "main")}";

    private static string GetSizeSettingName(LauncherWindowMode mode)
        => $"{SizeSettingPrefix}{(mode == LauncherWindowMode.Mini ? "mini" : "main")}";

    private void SetValueIfChanged(string name, string value)
    {
        if (!string.Equals(_settingsStore.GetValue(name, string.Empty), value, StringComparison.Ordinal))
        {
            _settingsStore.SetValue(name, value);
        }
    }
}
