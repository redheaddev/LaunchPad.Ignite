using System;
using System.Collections.Generic;
using System.Linq;

namespace LaunchPad.Ignite.Core.Schema;

public sealed class ShortcutResolver
{
    private readonly IReadOnlyDictionary<string, string> _expansionsByAlias;
    private readonly IReadOnlyDictionary<KeyboardShortcutChord, (string Expansion, bool ExecuteImmediately)> _expansionsByChord;

    public ShortcutResolver(IEnumerable<CommandDefinition> commands)
    {
        var expansionsByAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var expansionsByChord = new Dictionary<KeyboardShortcutChord, (string Expansion, bool ExecuteImmediately)>();
        foreach (var command in commands)
        {
            string expansion;
            var isShortcutCommand = string.Equals(command.Kind, "shortcut", StringComparison.OrdinalIgnoreCase);
            if (isShortcutCommand)
            {
                var defaultAction = command.Actions.FirstOrDefault(action =>
                    string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase));
                if (defaultAction is null)
                {
                    continue;
                }

                expansion = defaultAction.TargetTemplate;
                expansionsByAlias.Add(command.QualifiedName, expansion);
            }
            else
            {
                expansion = $"{command.QualifiedName} ";
            }

            if (KeyboardShortcutChord.TryParse(command.KeyboardShortcut, out var chord))
            {
                expansionsByChord.Add(chord, (expansion, command.ExecuteOnKeyboardShortcut));
            }

            foreach (var action in command.Actions)
            {
                if (!KeyboardShortcutChord.TryParse(action.KeyboardShortcut, out chord))
                {
                    continue;
                }

                var actionExpansion = isShortcutCommand
                    ? action.TargetTemplate
                    : string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase)
                        ? $"{command.QualifiedName} "
                        : $"{command.QualifiedName} {action.Name} ";
                expansionsByChord.Add(chord, (actionExpansion, action.ExecuteOnKeyboardShortcut));
            }
        }

        _expansionsByAlias = expansionsByAlias;
        _expansionsByChord = expansionsByChord;
    }

    public bool TryExpandAlias(string inputText, out string expansion)
        => _expansionsByAlias.TryGetValue(inputText, out expansion!);

    public IReadOnlyList<KeyValuePair<string, string>> GetAliasExpansions(string prefix)
        => _expansionsByAlias
            .Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value))
            .ToArray();

    public bool TryExpandChord(KeyboardShortcutChord chord, out string expansion)
    {
        var found = TryExpandChord(chord, out expansion, out _);
        return found;
    }

    public bool TryExpandChord(KeyboardShortcutChord chord, out string expansion, out bool executeImmediately)
    {
        if (_expansionsByChord.TryGetValue(chord, out var shortcut))
        {
            expansion = shortcut.Expansion;
            executeImmediately = shortcut.ExecuteImmediately;
            return true;
        }

        expansion = string.Empty;
        executeImmediately = false;
        return false;
    }
}
