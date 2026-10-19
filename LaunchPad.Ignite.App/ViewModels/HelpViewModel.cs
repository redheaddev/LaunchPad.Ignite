using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.ViewModels;

public sealed record HelpKeyboardShortcut(string Shortcut, string Description);
public sealed record HelpExtensionShortcutGroup(string ExtensionId, IReadOnlyList<HelpKeyboardShortcut> Shortcuts);

public sealed class HelpViewModel
{
    public IReadOnlyList<HelpKeyboardShortcut> KeyboardShortcuts { get; }
    public IReadOnlyList<HelpKeyboardShortcut> CommandInputKeyboardShortcuts { get; } =
    [
        new("Right Arrow", "Accepts the highlighted completion or advances to the next segment when selecting a file path."),
        new("Ctrl+Shift+Left", "Selects the previous file-path segment."),
        new("Ctrl+Shift+Right", "Selects the next file-path segment."),
        new("Tab", "Accepts the highlighted completion; accepts a single list value or toggles a checklist value."),
        new("Shift+Tab", "Uses the same completion behavior as Tab."),
        new("Ctrl+A", "Selects all values in a multiple-selection checklist."),
        new("Ctrl+D", "Clears all values in a multiple-selection checklist."),
        new("Space", "Exits a multiple-selection checklist."),
        new("Enter", "Runs a complete command."),
        new("Alt+Enter", "Runs the command as another user."),
        new("Ctrl+Shift+Enter", "Runs the command elevated."),
        new("Shift+Enter", "Opens a selected file path in a terminal."),
        new("Escape", "Closes the active dialog or dismisses the launcher.")
    ];
    public IReadOnlyList<HelpKeyboardShortcut> CommandKeyboardShortcuts { get; }
    public IReadOnlyList<HelpKeyboardShortcut> UrlCommandKeyboardShortcuts { get; }
    public IReadOnlyList<HelpExtensionShortcutGroup> ExtensionShortcutGroups { get; }
    public bool HasCommandKeyboardShortcuts => CommandKeyboardShortcuts.Count > 0;
    public bool HasUrlCommandKeyboardShortcuts => UrlCommandKeyboardShortcuts.Count > 0;
    public bool HasExtensionShortcutGroups => ExtensionShortcutGroups.Count > 0;
    private readonly IReadOnlyDictionary<string, string> _extensionCommandSources;

    public HelpViewModel(
        IEnumerable<CommandDefinition> commands,
        IReadOnlyDictionary<string, string>? extensionCommandSources = null)
    {
        _extensionCommandSources = extensionCommandSources
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shortcuts = new List<HelpKeyboardShortcut>
        {
            new("Ctrl+.", "Open Settings"),
            new("Ctrl+I", "Open Import / Export"),
            new("Ctrl+H", "Open Help"),
            new("Ctrl+M", "Toggle main / mini mode")
        };
        var commandShortcuts = new List<HelpKeyboardShortcut>();
        var urlCommandShortcuts = new List<HelpKeyboardShortcut>
        {
            new("Ctrl+Enter", "Runs the URL command in a new window where supported.")
        };
        var extensionShortcuts = new Dictionary<string, List<HelpKeyboardShortcut>>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            var isUrlCommand = string.Equals(command.ExecutionHandler, "url", StringComparison.OrdinalIgnoreCase);
            var commandType = isUrlCommand ? "URL command" : "Command";
            var actionsWithShortcuts = command.Actions
                .Where(action => !string.IsNullOrWhiteSpace(action.KeyboardShortcut))
                .ToArray();
            if (string.IsNullOrWhiteSpace(command.KeyboardShortcut) && actionsWithShortcuts.Length == 0)
            {
                continue;
            }

            List<HelpKeyboardShortcut> commandShortcutList;
            if (_extensionCommandSources.TryGetValue(command.QualifiedName, out var extensionId))
            {
                if (!extensionShortcuts.TryGetValue(extensionId, out commandShortcutList!))
                {
                    commandShortcutList = [];
                    extensionShortcuts.Add(extensionId, commandShortcutList);
                }
            }
            else
            {
                commandShortcutList = isUrlCommand ? urlCommandShortcuts : commandShortcuts;
            }

            if (!string.IsNullOrWhiteSpace(command.KeyboardShortcut))
            {
                var execution = command.ExecuteOnKeyboardShortcut
                    ? "runs immediately"
                    : "expands in the launcher";
                commandShortcutList.Add(new HelpKeyboardShortcut(
                    FormatShortcut(command.KeyboardShortcut),
                    $"{commandType} {command.QualifiedName} — {command.Description} ({execution})"));
            }

            foreach (var action in actionsWithShortcuts)
            {
                var execution = action.ExecuteOnKeyboardShortcut
                    ? "runs immediately"
                    : "expands in the launcher";
                var actionDescription = string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase)
                    ? $"{commandType} {command.QualifiedName} — {action.Description}"
                    : $"{commandType} {command.QualifiedName} {action.Name} — {action.Description}";
                commandShortcutList.Add(new HelpKeyboardShortcut(
                    FormatShortcut(action.KeyboardShortcut!),
                    $"{actionDescription} ({execution})"));
            }
        }

        KeyboardShortcuts = SortShortcuts(shortcuts);
        CommandKeyboardShortcuts = SortShortcuts(commandShortcuts);
        UrlCommandKeyboardShortcuts = SortShortcuts(urlCommandShortcuts);
        ExtensionShortcutGroups = extensionShortcuts
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HelpExtensionShortcutGroup(group.Key, SortShortcuts(group.Value)))
            .ToArray();
    }

    private static IReadOnlyList<HelpKeyboardShortcut> SortShortcuts(
        IEnumerable<HelpKeyboardShortcut> shortcuts)
        => shortcuts
            .OrderBy(shortcut => shortcut.Shortcut, StringComparer.OrdinalIgnoreCase)
            .ThenBy(shortcut => shortcut.Description, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string FormatShortcut(string shortcut)
        => string.Join(
            "+",
            shortcut.Split('+').Select(part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant())));
}
