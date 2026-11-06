using System.Text.RegularExpressions;

namespace LaunchPad.Ignite.Core.Schema;

public sealed class CommandSchemaValidator
{
    private static readonly Regex ArgumentTemplatePlaceholder = new("\\{([^{}]+)\\}", RegexOptions.Compiled);

    public IReadOnlyList<string> Validate(CommandSchema schema, IEnumerable<string>? supportedCommandKinds = null)
    {
        var errors = new List<string>();
        var listKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commandKinds = new HashSet<string>(supportedCommandKinds ?? [], StringComparer.OrdinalIgnoreCase)
        {
            "url",
            "shortcut"
        };
        var keyboardShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in schema.Lists)
        {
            if (!Enum.IsDefined(list.MatchMode))
            {
                errors.Add($"List '{list.Name}' has unsupported match mode '{list.MatchMode}'.");
            }

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

            if (list.Default is not null
                && !list.Values.Contains(list.Default, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"List '{list.Name}' default '{list.Default}' must match one of its values.");
            }
        }

        var settingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in schema.Settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Name)
                || setting.Name.Contains(':')
                || !settingNames.Add(setting.QualifiedName))
            {
                errors.Add($"Setting name '{setting.QualifiedName}' is empty or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(setting.Label)
                || string.IsNullOrWhiteSpace(setting.Description)
                || string.IsNullOrWhiteSpace(setting.DefaultValue))
            {
                errors.Add($"Setting '{setting.QualifiedName}' requires a label, description, and non-empty default value.");
            }

            if (setting.Type is not ("text" or "password" or "directory" or "boolean" or "secretName" or "select"))
            {
                errors.Add($"Setting '{setting.QualifiedName}' has unsupported type '{setting.Type}'.");
            }

            if (setting.Type == "select"
                && (setting.Options is null
                    || setting.Options.Count == 0
                    || !setting.Options.Contains(setting.DefaultValue, StringComparer.OrdinalIgnoreCase)))
            {
                errors.Add($"Select setting '{setting.QualifiedName}' must provide options containing its default value.");
            }

            if (setting.Namespace is not null
                && (string.IsNullOrWhiteSpace(setting.Namespace) || setting.Namespace.Contains(':')))
            {
                errors.Add($"Setting '{setting.Name}' has an invalid namespace '{setting.Namespace}'.");
            }
        }

        var variableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in schema.Variables)
        {
            if (variable.Namespace is not null
                && (string.IsNullOrWhiteSpace(variable.Namespace) || variable.Namespace.Contains(':')))
            {
                errors.Add($"Variable '{variable.Name}' has an invalid namespace '{variable.Namespace}'.");
            }

            if (string.IsNullOrWhiteSpace(variable.Name)
                || variable.Name.Contains(':')
                || !variableNames.Add(variable.QualifiedName))
            {
                errors.Add($"Variable name '{variable.QualifiedName}' is empty or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(variable.Value))
            {
                errors.Add($"Variable '{variable.QualifiedName}' must have a non-empty value.");
            }
        }

        var functionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var function in schema.Functions)
        {
            if (string.IsNullOrWhiteSpace(function.Name)
                || function.Name.Contains(':')
                || !functionNames.Add(function.QualifiedName))
            {
                errors.Add($"Function name '{function.QualifiedName}' is empty or duplicated.");
            }

            if (!Enum.IsDefined(function.ReturnType))
            {
                errors.Add($"Function '{function.QualifiedName}' has an unsupported return type '{function.ReturnType}'.");
            }

            if (!Enum.IsDefined(function.MatchMode)
                || (function.ReturnType != CommandFunctionReturnType.List
                    && function.MatchMode != ListMatchMode.StartsWith))
            {
                errors.Add($"Function '{function.QualifiedName}' has an invalid list match mode '{function.MatchMode}'.");
            }

            if (function.ReturnType == CommandFunctionReturnType.List && function.Kind is not null)
            {
                errors.Add($"List function '{function.QualifiedName}' must use an extension executor.");
            }

            var parameterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in function.Parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Name) || !parameterNames.Add(parameter.Name))
                {
                    errors.Add($"Parameter name '{parameter.Name}' in function '{function.QualifiedName}' is empty or duplicated.");
                }

                if (parameter.ListFunction is null)
                {
                    continue;
                }

                if (!CommandListFunctionCall.TryParse(parameter.ListFunction, out var listFunctionCall))
                {
                    errors.Add($"Parameter '{parameter.Name}' in function '{function.QualifiedName}' has an invalid list function source '{parameter.ListFunction}'.");
                    continue;
                }

                var listFunction = schema.Functions.FirstOrDefault(candidate =>
                    string.Equals(candidate.QualifiedName, listFunctionCall.FunctionName, StringComparison.OrdinalIgnoreCase));
                if (listFunction is null || listFunction.ReturnType != CommandFunctionReturnType.List)
                {
                    errors.Add($"Parameter '{parameter.Name}' in function '{function.QualifiedName}' references an undefined list function '{listFunctionCall.FunctionName}'.");
                    continue;
                }

                if (listFunctionCall.ArgumentNames.Count != listFunction.Parameters.Count)
                {
                    errors.Add($"List function source '{parameter.ListFunction}' must provide {listFunction.Parameters.Count} argument(s).");
                }
            }
        }

        var commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in schema.Commands)
        {
            if (command.Namespace is not null
                && ((command.Namespace.Length > 0 && string.IsNullOrWhiteSpace(command.Namespace))
                    || command.Namespace.Contains(':')))
            {
                errors.Add($"Command '{command.Name}' has an invalid namespace '{command.Namespace}'.");
            }

            if (string.IsNullOrWhiteSpace(command.Name)
                || command.Name.Contains(':')
                || !commandNames.Add(command.QualifiedName))
            {
                errors.Add($"Command name '{command.QualifiedName}' is empty or duplicated.");
            }

            if (!commandKinds.Contains(command.ExecutionHandler))
            {
                errors.Add($"Command '{command.Name}' has unsupported handler '{command.ExecutionHandler}'.");
            }

            if (string.Equals(command.ExecutionHandler, "shortcut", StringComparison.OrdinalIgnoreCase))
            {
                if (command.Actions.Count != 1
                    || !string.Equals(command.Actions[0].Name, "default", StringComparison.OrdinalIgnoreCase)
                    || command.Actions[0].Arguments.Count != 0
                    || command.Actions[0].Execution is not null)
                {
                    errors.Add($"Shortcut command '{command.Name}' must define only a default action with no arguments or execution.");
                }

            }

            if (command.KeyboardShortcut is not null)
            {
                if (!KeyboardShortcutChord.TryParse(command.KeyboardShortcut, out var chord))
                {
                    errors.Add($"Command '{command.Name}' has an invalid keyboard shortcut '{command.KeyboardShortcut}'.");
                }
                else if (!keyboardShortcuts.Add(chord.ToString()))
                {
                    errors.Add($"Keyboard shortcut '{command.KeyboardShortcut}' is assigned more than once.");
                }
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

                if (action.KeyboardShortcut is not null)
                {
                    if (!KeyboardShortcutChord.TryParse(action.KeyboardShortcut, out var actionChord))
                    {
                        errors.Add($"Action '{action.Name}' in command '{command.Name}' has an invalid keyboard shortcut '{action.KeyboardShortcut}'.");
                    }
                    else if (!keyboardShortcuts.Add(actionChord.ToString()))
                    {
                        errors.Add($"Keyboard shortcut '{action.KeyboardShortcut}' is assigned more than once.");
                    }
                }

                if (string.IsNullOrWhiteSpace(action.TargetTemplate))
                {
                    errors.Add($"Action '{action.Name}' in command '{command.Name}' must define a target template.");
                }

                ValidateArguments(command, action, schema, errors);
                ValidateArgumentTemplates(command, action, errors);
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

            var supportsCustomValues = argument.Ref is not null
                && (argument.Ref.StartsWith("enum:", StringComparison.OrdinalIgnoreCase)
                    || argument.Ref.StartsWith("list:", StringComparison.OrdinalIgnoreCase))
                || argument.ListFunction is not null;
            if (argument.AllowCustomValues
                && !supportsCustomValues)
            {
                errors.Add($"Argument '{argument.Name}' can allow custom values only when referencing an enum or list.");
            }

            if (argument.ListFunction is not null)
            {
                if (argument.Ref is not null)
                {
                    errors.Add($"Argument '{argument.Name}' cannot specify both a list reference and a list function.");
                }

                if (!CommandListFunctionCall.TryParse(argument.ListFunction, out var functionCall))
                {
                    errors.Add($"Argument '{argument.Name}' has an invalid list function source '{argument.ListFunction}'.");
                    continue;
                }

                var listFunction = schema.Functions.FirstOrDefault(function =>
                    string.Equals(function.QualifiedName, functionCall.FunctionName, StringComparison.OrdinalIgnoreCase));
                if (listFunction is null || listFunction.ReturnType != CommandFunctionReturnType.List)
                {
                    errors.Add($"Argument '{argument.Name}' references an undefined list function '{functionCall.FunctionName}'.");
                    continue;
                }

                if (functionCall.ArgumentNames.Count != listFunction.Parameters.Count)
                {
                    errors.Add($"List function source '{argument.ListFunction}' must provide {listFunction.Parameters.Count} argument(s).");
                }

                foreach (var argumentName in functionCall.ArgumentNames)
                {
                    if (!action.Arguments.Any(candidate =>
                        string.Equals(candidate.Name, argumentName, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"List function source '{argument.ListFunction}' references unknown command argument '{argumentName}'.");
                    }
                }
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
                    && (string.Equals(list.Name, refName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(list.QualifiedName, refName, StringComparison.OrdinalIgnoreCase))))
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

    private static void ValidateArgumentTemplates(
        CommandDefinition command,
        CommandActionDefinition action,
        ICollection<string> errors)
    {
        if (action.ArgumentTemplates.Count == 0)
        {
            return;
        }

        if (!string.Equals(command.ExecutionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Action '{action.Name}' in command '{command.Name}' can define argument templates only for shell commands.");
            return;
        }

        var argumentNames = action.Arguments
            .Select(argument => argument.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var template in action.ArgumentTemplates)
        {
            var placeholders = ArgumentTemplatePlaceholder.Matches(template)
                .Select(match => match.Groups[1].Value)
                .ToArray();
            var unknownPlaceholder = placeholders.FirstOrDefault(name => !argumentNames.Contains(name));
            if (unknownPlaceholder is not null)
            {
                errors.Add($"Action '{action.Name}' in command '{command.Name}' argument template contains unknown placeholder '{{{unknownPlaceholder}}}'.");
            }

            var remainder = ArgumentTemplatePlaceholder.Replace(template, string.Empty);
            if (remainder.Contains('{') || remainder.Contains('}'))
            {
                errors.Add($"Action '{action.Name}' in command '{command.Name}' has an invalid argument template '{template}'.");
            }
        }
    }
}
