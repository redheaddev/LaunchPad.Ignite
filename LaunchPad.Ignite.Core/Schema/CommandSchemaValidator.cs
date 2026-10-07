namespace LaunchPad.Ignite.Core.Schema;

public sealed class CommandSchemaValidator
{
    public IReadOnlyList<string> Validate(CommandSchema schema)
    {
        var errors = new List<string>();
        var listKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in schema.Lists)
        {
            if (!string.Equals(list.Type, "enum", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"List '{list.Name}' has unsupported type '{list.Type}'.");
            }

            if (string.IsNullOrWhiteSpace(list.Name) || !listKeys.Add($"{list.Type}:{list.Name}"))
            {
                errors.Add($"List name '{list.Name}' is empty or duplicated.");
            }

            if (list.Values.Count == 0 || list.Values.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"List '{list.Name}' must contain non-empty values.");
            }

            if (list.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Values.Count)
            {
                errors.Add($"List '{list.Name}' contains duplicate values.");
            }
        }

        var commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in schema.Commands)
        {
            if (string.IsNullOrWhiteSpace(command.Name) || !commandNames.Add(command.Name))
            {
                errors.Add($"Command name '{command.Name}' is empty or duplicated.");
            }

            if (!string.Equals(command.Kind, "url", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Command '{command.Name}' has unsupported kind '{command.Kind}'.");
            }

            if (command.Actions.Count == 0)
            {
                errors.Add($"Command '{command.Name}' must declare at least one action.");
                continue;
            }

            var actionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var action in command.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Name) || !actionNames.Add(action.Name))
                {
                    errors.Add($"Action name '{action.Name}' in command '{command.Name}' is empty or duplicated.");
                }

                if (string.IsNullOrWhiteSpace(action.TargetTemplate))
                {
                    errors.Add($"Action '{action.Name}' in command '{command.Name}' must define a target template.");
                }

                ValidateArguments(command, action, schema, errors);
            }
        }

        return errors;
    }

    private static void ValidateArguments(
        CommandDefinition command,
        CommandActionDefinition action,
        CommandSchema schema,
        ICollection<string> errors)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var optionalSeen = false;
        foreach (var argument in action.Arguments)
        {
            if (string.IsNullOrWhiteSpace(argument.Name) || !names.Add(argument.Name))
            {
                errors.Add($"Argument name '{argument.Name}' in action '{action.Name}' is empty or duplicated.");
            }

            optionalSeen |= argument.Optional;
            if (!argument.Optional && optionalSeen)
            {
                errors.Add($"Required arguments cannot follow optional arguments in action '{action.Name}'.");
            }

            if (argument.InputType is not null
                && !string.Equals(argument.InputType, "csv", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Argument '{argument.Name}' uses unsupported input type '{argument.InputType}'.");
            }

            if (argument.AllowCustomValues
                && (argument.Ref is null || !argument.Ref.StartsWith("enum:", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Argument '{argument.Name}' can allow custom values only when referencing an enum.");
            }

            if (argument.Ref is null)
            {
                continue;
            }

            var separator = argument.Ref.IndexOf(':');
            if (separator <= 0 || separator == argument.Ref.Length - 1)
            {
                errors.Add($"Argument '{argument.Name}' has an invalid list reference '{argument.Ref}'.");
                continue;
            }

            var refType = argument.Ref[..separator];
            var refName = argument.Ref[(separator + 1)..];
            if (string.Equals(refType, "enum", StringComparison.OrdinalIgnoreCase))
            {
                if (!schema.Lists.Any(list =>
                    string.Equals(list.Type, "enum", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(list.Name, refName, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Argument '{argument.Name}' references undefined enum '{refName}'.");
                }
            }
            else if (!string.Equals(refType, "list", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Argument '{argument.Name}' references unsupported list type '{refType}'.");
            }
        }

        if (action.Execution is null)
        {
            return;
        }

        if (action.Execution.Mode == CommandExecutionMode.TargetPerValue && action.Execution.Arguments.Count == 0)
        {
            errors.Add($"Action '{action.Name}' must name at least one argument for targetPerValue execution.");
        }

        foreach (var executionArgumentName in action.Execution.Arguments)
        {
            var executionArgument = action.Arguments.FirstOrDefault(argument =>
                string.Equals(argument.Name, executionArgumentName, StringComparison.OrdinalIgnoreCase));
            if (executionArgument is null || !string.Equals(executionArgument.InputType, "csv", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Action '{action.Name}' execution argument '{executionArgumentName}' must reference a CSV argument.");
            }
        }
    }
}
