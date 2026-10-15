using System;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class CommandLaunchPreferenceStore(ProfileSettingsStore settingsStore)
{
    private const string SettingPrefix = "commandLaunchMode:";

    public static string GetSettingName(CommandDefinition command)
        => $"{SettingPrefix}{command.QualifiedName}";

    public CommandLaunchMode GetLaunchMode(CommandDefinition command)
    {
        if (!string.Equals(command.ExecutionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            return CommandLaunchMode.Normal;
        }

        var configured = settingsStore.GetValue(GetSettingName(command), nameof(CommandLaunchMode.Normal));
        return Enum.TryParse<CommandLaunchMode>(configured, ignoreCase: true, out var mode)
            ? mode
            : CommandLaunchMode.Normal;
    }

    public void SetLaunchMode(CommandDefinition command, CommandLaunchMode mode)
    {
        if (!string.Equals(command.ExecutionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Launch preferences can only be set for shell commands.", nameof(command));
        }

        var key = GetSettingName(command);
        if (mode == CommandLaunchMode.Normal)
        {
            settingsStore.RemoveValue(key);
            return;
        }

        settingsStore.SetValue(key, mode.ToString());
    }
}
