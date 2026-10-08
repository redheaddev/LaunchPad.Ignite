using System;
using System.Collections.Generic;
using System.Linq;

namespace LaunchPad.Ignite.Core.Schema;

public sealed class ShortcutResolver
{
    private readonly IReadOnlyDictionary<string, string> _expansionsByAlias;
    private readonly IReadOnlyDictionary<KeyboardShortcutChord, string> _expansionsByChord;

    public ShortcutResolver(IEnumerable<CommandDefinition> commands)
    {
        var expansionsByAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var expansionsByChord = new Dictionary<KeyboardShortcutChord, string>();
        foreach (var command in commands)
        {
            string expansion;
            if (string.Equals(command.Kind, "shortcut", StringComparison.OrdinalIgnoreCase))
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
                expansionsByChord.Add(chord, expansion);
            }
        }

        _expansionsByAlias = expansionsByAlias;
        _expansionsByChord = expansionsByChord;
    }

    public bool TryExpandAlias(string inputText, out string expansion)
        => _expansionsByAlias.TryGetValue(inputText, out expansion!);

    public bool TryExpandChord(KeyboardShortcutChord chord, out string expansion)
        => _expansionsByChord.TryGetValue(chord, out expansion!);
}
