using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class CommandNamesArgumentValueProvider : IArgumentValueProvider
{
    private readonly Func<IReadOnlyList<CommandDefinition>> _getCommands;

    public CommandNamesArgumentValueProvider(Func<IReadOnlyList<CommandDefinition>> getCommands)
    {
        _getCommands = getCommands;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, "list:commands", StringComparison.OrdinalIgnoreCase)
            ? _getCommands()
                .Select(command => command.QualifiedName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
}
