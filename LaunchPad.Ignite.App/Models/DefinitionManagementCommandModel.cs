using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Models;

public sealed class DefinitionManagementCommandModel
{
    private static readonly Regex UrlBracePlaceholder = new("\\{([^{}]+)\\}", RegexOptions.Compiled);
    public static CommandDefinition Definition { get; } = new(
        "launchpad",
        "system",
        "Create and manage user-defined commands and enum lists",
        [
            new CommandActionDefinition(
                "command",
                "Add, edit, list, show, or remove user commands",
                "launchpad command <add|edit|list|show|remove>",
                "launchpad command",
                []),
            new CommandActionDefinition(
                "list",
                "Add, edit, list, show, or remove user enum lists",
                "launchpad list <add|edit|list|show|remove>",
                "launchpad list",
                [])
        ]) { Handler = "definitionManagement" };
    private readonly CommandSchemaLoader _schemaLoader;
    private readonly Action _schemaUpdated;
    private readonly DefinitionManagementParser _parser = new();

    public DefinitionManagementCommandModel(CommandSchemaLoader schemaLoader, Action schemaUpdated)
    {
        _schemaLoader = schemaLoader;
        _schemaUpdated = schemaUpdated;
    }

    public bool Handles(string input)
        => _parser.IsManagementCommand(input);

    public DefinitionManagementHelp GetHelp(string input)
        => _parser.GetHelp(input);

    public bool ShouldAppendSeparatorOnTab(string input)
        => _parser.ShouldAppendSeparatorOnTab(input);

    public CommandExecutionResult Execute(string input)
    {
        var parsed = _parser.Parse(input);
        if (parsed.Status == DefinitionManagementStatus.NotManagementCommand)
        {
            return new CommandExecutionResult(false, "Not a LaunchPad definition management command.");
        }
        if (parsed.Status == DefinitionManagementStatus.Invalid || parsed.Request is null)
        {
            return new CommandExecutionResult(false, parsed.Error ?? "Invalid management command.");
        }

        try
        {
            var request = parsed.Request;
            return request.Entity == "command" ? ExecuteCommand(request) : ExecuteList(request);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            return new CommandExecutionResult(false, exception.Message);
        }
    }

    private CommandExecutionResult ExecuteCommand(DefinitionManagementRequest request)
    {
        switch (request.Operation)
        {
            case "list":
                return Value(string.Join(Environment.NewLine, _schemaLoader.GetUserCommands()
                    .OrderBy(command => command.QualifiedName, StringComparer.OrdinalIgnoreCase)
                    .Select(command => $"{command.QualifiedName} [{command.Kind}/{command.ExecutionHandler}] - {command.Description}")), "No user commands.");
            case "show":
            {
                var command = _schemaLoader.FindUserCommand(request.Name)
                    ?? throw new InvalidOperationException($"Command '{request.Name}' was not found in the user schema.");
                var action = command.Actions.FirstOrDefault();
                return Value($"{command.QualifiedName} [{command.Kind}/{command.ExecutionHandler}]\n{command.Description}\n{action?.Usage}\nTarget: {action?.TargetTemplate}\nWorking directory: {action?.WorkingDirectoryTemplate}");
            }
            case "remove":
                _schemaLoader.RemoveUserCommand(request.Name);
                _schemaUpdated();
                return Success($"Removed user command '{request.Name}'.");
            case "add":
            {
                var (name, commandNamespace) = ParseName(request.Name);
                var handler = request.Kind!;
                var kind = GetKindForHandler(handler);
                var command = new CommandDefinition(name, kind, request.Description ?? string.Empty,
                    [BuildAction(request.Kind!, request.Name, request.Target!, request.Arguments ?? [], request.TargetArguments ?? [], request.WorkingDirectory)],
                    commandNamespace,
                    KeyboardShortcut: request.KeyboardShortcut,
                    ExecuteOnKeyboardShortcut: request.HasKeyboardShortcut,
                    Handler: handler);
                _schemaLoader.AddUserCommand(command);
                _schemaUpdated();
                return Success($"Added user command '{command.QualifiedName}'.");
            }
            case "edit":
                return EditCommand(request);
            default:
                throw new InvalidOperationException($"Unsupported command operation '{request.Operation}'.");
        }
    }

    private CommandExecutionResult EditCommand(DefinitionManagementRequest request)
    {
        var command = _schemaLoader.FindUserCommand(request.Name)
            ?? throw new InvalidOperationException($"Command '{request.Name}' was not found in the user schema.");
        if (request.HasWorkingDirectory
            && !string.Equals(command.ExecutionHandler, "shell", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("--working-directory is supported only for shell commands.");
        }
        if ((request.HasKeyboardShortcut || request.ClearKeyboardShortcut)
            && !string.Equals(command.ExecutionHandler, "shortcut", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Keyboard key options are supported only for shortcut commands.");
        }

        var currentAction = command.Actions.FirstOrDefault(action => string.Equals(action.Name, "default", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Only commands with a default action can be edited by this command.");
        var updatedArguments = request.HasArguments ? request.Arguments! : currentAction.Arguments;
        var updatedTemplates = request.HasTargetArguments ? request.TargetArguments! : currentAction.ArgumentTemplates;
        var updatedTarget = request.HasTarget ? NormalizeTarget(command.ExecutionHandler, request.Target!) : currentAction.TargetTemplate;
        var updatedWorkingDirectory = request.HasWorkingDirectory ? request.WorkingDirectory : currentAction.WorkingDirectoryTemplate;
        ValidateTargetArguments(command.ExecutionHandler, updatedTarget, updatedTemplates, updatedArguments);
        var updatedAction = currentAction with
        {
            TargetTemplate = updatedTarget,
            Arguments = updatedArguments,
            ArgumentTemplates = updatedTemplates,
            WorkingDirectoryTemplate = updatedWorkingDirectory,
            Usage = request.HasArguments ? BuildUsage(command.QualifiedName, updatedArguments) : currentAction.Usage
        };
        var updated = command with
        {
            Description = request.HasDescription ? request.Description! : command.Description,
            KeyboardShortcut = request.ClearKeyboardShortcut
                ? null
                : request.HasKeyboardShortcut ? request.KeyboardShortcut : command.KeyboardShortcut,
            ExecuteOnKeyboardShortcut = request.ClearKeyboardShortcut
                ? false
                : request.HasKeyboardShortcut || command.ExecuteOnKeyboardShortcut,
            Actions = command.Actions.Select(action => action.Name == currentAction.Name ? updatedAction : action).ToArray()
        };
        _schemaLoader.UpdateUserCommand(updated);
        _schemaUpdated();
        return Success($"Updated user command '{command.QualifiedName}'.");
    }

    private static string GetKindForHandler(string handler)
        => handler.ToLowerInvariant() switch
        {
            "url" => "url",
            "shell" => "shell",
            "shortcut" => "shortcut",
            "variable" => "variable",
            "secret" => "secret",
            "snippet" => "snippet",
            _ => "system"
        };

    private CommandExecutionResult ExecuteList(DefinitionManagementRequest request)
    {
        switch (request.Operation)
        {
            case "list":
                return Value(string.Join(Environment.NewLine, _schemaLoader.GetUserLists()
                    .OrderBy(list => list.QualifiedName, StringComparer.OrdinalIgnoreCase)
                    .Select(list => $"{list.QualifiedName} (default: {list.Default}) - {string.Join(", ", list.Values)}")), "No user enum lists.");
            case "show":
            {
                var list = _schemaLoader.FindUserList(request.Name)
                    ?? throw new InvalidOperationException($"List '{request.Name}' was not found in the user schema.");
                return Value($"enum:{list.QualifiedName}\nDefault: {list.Default}\nValues: {string.Join(", ", list.Values)}");
            }
            case "remove":
                _schemaLoader.RemoveUserList(request.Name);
                _schemaUpdated();
                return Success($"Removed user list '{request.Name}'.");
            case "add":
            {
                var (name, listNamespace) = ParseName(request.Name);
                var values = NormalizeListValues(request.Values!, request.Default!);
                var list = new CommandListDefinition("enum", name, values, values[0], listNamespace);
                _schemaLoader.AddUserList(list);
                _schemaUpdated();
                return Success($"Added user enum list '{list.QualifiedName}'.");
            }
            case "edit":
            {
                var list = _schemaLoader.FindUserList(request.Name)
                    ?? throw new InvalidOperationException($"List '{request.Name}' was not found in the user schema.");
                var values = list.Values.ToList();
                foreach (var update in request.Values ?? [])
                {
                    if (update.Length > 0 && update[0] == '\u001f')
                    {
                        values.RemoveAll(value => string.Equals(value, update[1..], StringComparison.OrdinalIgnoreCase));
                    }
                    else if (!values.Contains(update, StringComparer.OrdinalIgnoreCase))
                    {
                        values.Add(update);
                    }
                }
                if (request.HasValues)
                {
                    values = (request.Values ?? []).ToList();
                }

                var defaultValue = request.HasDefault ? request.Default : list.Default;
                var normalized = NormalizeListValues(values, defaultValue);
                _schemaLoader.UpdateUserList(list, list with { Values = normalized, Default = normalized[0] });
                _schemaUpdated();
                return Success($"Updated user enum list '{list.QualifiedName}'.");
            }
            default:
                throw new InvalidOperationException($"Unsupported list operation '{request.Operation}'.");
        }
    }

    private static CommandActionDefinition BuildAction(
        string kind,
        string commandName,
        string target,
        IReadOnlyList<CommandArgumentDefinition> arguments,
        IReadOnlyList<string> targetArguments,
        string? workingDirectory)
    {
        var normalizedTarget = NormalizeTarget(kind, target);
        var csvArguments = string.Equals(kind, "url", StringComparison.OrdinalIgnoreCase)
            ? arguments.Where(argument => string.Equals(argument.InputType, "csv", StringComparison.OrdinalIgnoreCase))
                .Select(argument => argument.Name).ToArray()
            : [];
        var execution = csvArguments.Length > 0
            ? new CommandExecutionDefinition(CommandExecutionMode.TargetPerValue, csvArguments)
            : null;
        return new CommandActionDefinition("default", string.Empty, BuildUsage(commandName, arguments), normalizedTarget, arguments, execution)
        {
            ArgumentTemplates = targetArguments,
            WorkingDirectoryTemplate = workingDirectory
        };
    }

    private static string NormalizeTarget(string kind, string target)
        => string.Equals(kind, "url", StringComparison.OrdinalIgnoreCase)
            ? UrlBracePlaceholder.Replace(target, match => $"<{match.Groups[1].Value}>")
            : target;

    private static void ValidateTargetArguments(
        string kind,
        string target,
        IReadOnlyList<string> targetArguments,
        IReadOnlyList<CommandArgumentDefinition> arguments)
    {
        if (!string.Equals(kind, "shell", StringComparison.OrdinalIgnoreCase) && targetArguments.Count > 0)
        {
            throw new ArgumentException("--target-arg is supported only for shell commands.");
        }

        var names = arguments.Select(argument => argument.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var templates = string.Equals(kind, "shell", StringComparison.OrdinalIgnoreCase)
            ? targetArguments
            : [target];
        foreach (var template in templates)
        {
            var placeholderPattern = string.Equals(kind, "url", StringComparison.OrdinalIgnoreCase)
                ? new Regex("<([^<>]+)>")
                : new Regex("\\{([^{}]+)\\}");
            var unknown = placeholderPattern.Matches(template)
                .Select(match => match.Groups[1].Value)
                .FirstOrDefault(name => !names.Contains(name));
            if (unknown is not null)
            {
                throw new ArgumentException($"Template contains placeholder '{{{unknown}}}' without a declared argument.");
            }
        }
    }

    private static string BuildUsage(string commandName, IReadOnlyList<CommandArgumentDefinition> arguments)
        => string.Join(' ', new[] { commandName }.Concat(arguments.Select(argument =>
            argument.Optional ? $"[{argument.Name}]" : $"<{argument.Name}>")));

    private static CommandExecutionDefinition? CreateCsvExecution(IReadOnlyList<CommandArgumentDefinition> arguments)
    {
        var csvArguments = arguments
            .Where(argument => string.Equals(argument.InputType, "csv", StringComparison.OrdinalIgnoreCase))
            .Select(argument => argument.Name)
            .ToArray();
        return csvArguments.Length == 0
            ? null
            : new CommandExecutionDefinition(CommandExecutionMode.TargetPerValue, csvArguments);
    }

    private static IReadOnlyList<string> NormalizeListValues(IEnumerable<string> values, string? defaultValue)
    {
        var normalized = values.Select(value => value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedDefault = normalized.FirstOrDefault(value => string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase));
        if (normalized.Length == 0 || selectedDefault is null)
        {
            throw new ArgumentException("A list requires at least one value and its default must be one of those values.");
        }

        return normalized.Where(value => !string.Equals(value, selectedDefault, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Prepend(selectedDefault).ToArray();
    }

    private static (string Name, string? Namespace) ParseName(string qualifiedName)
    {
        var separator = qualifiedName.IndexOf(':');
        return separator < 0
            ? (qualifiedName, null)
            : (qualifiedName[(separator + 1)..], qualifiedName[..separator]);
    }

    private static CommandExecutionResult Success(string message) => new(true, message);
    private static CommandExecutionResult Value(string message, string emptyMessage = "")
        => new(true, string.IsNullOrEmpty(message) ? emptyMessage : message) { IsValueOutput = true };
}
