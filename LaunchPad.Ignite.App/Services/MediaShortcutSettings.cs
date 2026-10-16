using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public static class MediaShortcutSettings
{
    public const string Namespace = "media";
    public const string SettingPrefix = "app:keyboardShortcut:media:";
    private static readonly IReadOnlyDictionary<string, string> DefaultShortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["start"] = "alt+num+8",
        ["stop"] = "alt+num+2",
        ["next"] = "alt+num+6",
        ["prev"] = "alt+num+4",
        ["toggle"] = "alt+num+5"
    };

    public static string GetSettingName(string actionName)
        => $"{SettingPrefix}{actionName}";

    public static string GetDefaultShortcut(string actionName)
        => DefaultShortcuts.GetValueOrDefault(actionName, string.Empty);

    public static string GetValue(string actionName, string? defaultValue, IReadOnlyDictionary<string, string> settings)
        => settings.TryGetValue(GetSettingName(actionName), out var value) ? value : defaultValue ?? string.Empty;

    public static IReadOnlyList<CommandDefinition> ApplyOverrides(
        IEnumerable<CommandDefinition> commands,
        IReadOnlyDictionary<string, string> settings)
        => commands.Select(command => !IsMediaCommand(command)
            ? command
            : command with
            {
                Actions = command.Actions.Select(action => settings.TryGetValue(GetSettingName(action.Name), out var shortcut)
                    ? action with { KeyboardShortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut }
                    : action).ToArray()
            }).ToArray();

    public static bool IsAvailable(string? chord)
        => KeyboardShortcutChord.TryParse(chord, out _);

    public static bool IsAvailable(string? chord, IEnumerable<CommandDefinition> commands, string actionName)
        => IsAvailable(chord, commands, actionName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static bool IsAvailable(
        string? chord,
        IEnumerable<CommandDefinition> commands,
        string actionName,
        IReadOnlyDictionary<string, string> settings)
    {
        if (!KeyboardShortcutChord.TryParse(chord, out var candidate))
        {
            return false;
        }

        foreach (var command in commands)
        {
            if (KeyboardShortcutChord.TryParse(command.KeyboardShortcut, out var commandChord) && commandChord == candidate)
            {
                return false;
            }

            foreach (var action in command.Actions)
            {
                if (IsMediaCommand(command)
                    && string.Equals(action.Name, actionName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsMediaCommand(command)
                    && settings.TryGetValue(GetSettingName(action.Name), out var configuredShortcut))
                {
                    if (KeyboardShortcutChord.TryParse(configuredShortcut, out var configuredChord)
                        && configuredChord == candidate)
                    {
                        return false;
                    }
                    continue;
                }

                if (KeyboardShortcutChord.TryParse(action.KeyboardShortcut, out var actionChord) && actionChord == candidate)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool IsMediaCommand(CommandDefinition command)
        => string.Equals(command.Namespace, Namespace, StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.ExecutionHandler, "media", StringComparison.OrdinalIgnoreCase);
}
