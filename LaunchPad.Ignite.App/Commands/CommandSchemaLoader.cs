using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed class CommandSchemaLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly CommandSchema _appSchema;
    private readonly string _userCommandsPath;
    private readonly string _defaultUserCommandsPath;
    private readonly string _migrationMarkerPath;
    private readonly HashSet<string> _packedCommandNames;

    public IReadOnlySet<string> UserCommandNames { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public CommandSchemaLoader(CommandSchema appSchema)
        : this(
            appSchema,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaunchPad.Ignite",
                "user-commands.json"))
    {
    }

    public CommandSchemaLoader(CommandSchema appSchema, IEnumerable<string> packedCommandNames)
        : this(
            appSchema,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaunchPad.Ignite",
                "user-commands.json"),
            Path.Combine(AppContext.BaseDirectory, "defaults", "user-commands.json"),
            packedCommandNames)
    {
    }

    public CommandSchemaLoader(CommandSchema appSchema, string userCommandsPath)
        : this(
            appSchema,
            userCommandsPath,
            Path.Combine(AppContext.BaseDirectory, "defaults", "user-commands.json"))
    {
    }

    public CommandSchemaLoader(CommandSchema appSchema, string userCommandsPath, string defaultUserCommandsPath)
        : this(appSchema, userCommandsPath, defaultUserCommandsPath, [])
    {
    }

    public CommandSchemaLoader(
        CommandSchema appSchema,
        string userCommandsPath,
        string defaultUserCommandsPath,
        IEnumerable<string> packedCommandNames)
    {
        _appSchema = appSchema;
        _userCommandsPath = userCommandsPath;
        _defaultUserCommandsPath = defaultUserCommandsPath;
        _migrationMarkerPath = userCommandsPath + ".legacy-migrated";
        _packedCommandNames = packedCommandNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public CommandSchema Load()
    {
        EnsureUserCommandsFileExists();
        MigrateLegacyCommandsOnce();
        var storedUserSchema = ReadSchema(_userCommandsPath);
        var userSchema = NormalizeCommandHandlers(storedUserSchema);
        if (storedUserSchema.Commands.Any(command => string.IsNullOrWhiteSpace(command.Handler)))
        {
            WriteSchema(userSchema);
        }
        RemovePackedCommands(userSchema);
        UserCommandNames = userSchema.Commands
            .Select(command => command.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Merge(_appSchema, userSchema);
    }

    public CommandSchema SetVariable(string name, string value)
    {
        EnsureUserCommandsFileExists();
        var variable = ParseVariable(name, value);
        var userSchema = ReadSchema(_userCommandsPath);
        var variables = userSchema.Variables
            .Where(existing => !string.Equals(existing.QualifiedName, variable.QualifiedName, StringComparison.OrdinalIgnoreCase))
            .Append(variable)
            .ToArray();
        WriteSchema(userSchema with { Variables = variables });
        return Load();
    }

    public CommandSchema UpdateUserList(CommandListDefinition originalList, CommandListDefinition updatedList)
    {
        EnsureUserCommandsFileExists();
        var userSchema = ReadSchema(_userCommandsPath);
        var listIndex = userSchema.Lists.ToList().FindIndex(list =>
            string.Equals(GetListKey(list), GetListKey(originalList), StringComparison.OrdinalIgnoreCase));
        if (listIndex < 0)
        {
            throw new InvalidOperationException($"List '{originalList.QualifiedName}' was not found in the user schema.");
        }

        var lists = userSchema.Lists.ToArray();
        lists[listIndex] = updatedList with { Name = userSchema.Lists[listIndex].Name, Namespace = userSchema.Lists[listIndex].Namespace };
        var updatedUserSchema = userSchema with { Lists = lists };
        ValidateAndWrite(updatedUserSchema);
        return Load();
    }

    public CommandSchema AddUserCommand(CommandDefinition command)
    {
        EnsureUserCommandsFileExists();
        var userSchema = ReadSchema(_userCommandsPath);
        EnsureAvailableCommandName(command.QualifiedName, userSchema);
        var updatedUserSchema = userSchema with { Commands = userSchema.Commands.Append(command).ToArray() };
        ValidateAndWrite(updatedUserSchema);
        return Load();
    }

    public CommandSchema RemoveUserCommand(string name)
    {
        EnsureUserCommandsFileExists();
        var qualifiedName = ParseQualifiedName(name);
        var userSchema = ReadSchema(_userCommandsPath);
        var commands = userSchema.Commands
            .Where(command => !string.Equals(command.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (commands.Length == userSchema.Commands.Count)
        {
            throw new InvalidOperationException($"Command '{qualifiedName}' was not found in the user schema.");
        }

        var updatedUserSchema = userSchema with { Commands = commands };
        ValidateAndWrite(updatedUserSchema);
        return Load();
    }

    public CommandSchema AddUserList(CommandListDefinition list)
    {
        EnsureUserCommandsFileExists();
        var userSchema = ReadSchema(_userCommandsPath);
        EnsureAvailableListName(list, userSchema);
        var updatedUserSchema = userSchema with { Lists = userSchema.Lists.Append(list).ToArray() };
        ValidateAndWrite(updatedUserSchema);
        return Load();
    }

    public CommandSchema UpdateUserList(CommandListDefinition updatedList)
    {
        EnsureUserCommandsFileExists();
        var userSchema = ReadSchema(_userCommandsPath);
        var listIndex = userSchema.Lists.ToList().FindIndex(list =>
            string.Equals(GetListKey(list), GetListKey(updatedList), StringComparison.OrdinalIgnoreCase));
        if (listIndex < 0)
        {
            throw new InvalidOperationException($"List '{updatedList.QualifiedName}' was not found in the user schema.");
        }

        var lists = userSchema.Lists.ToArray();
        lists[listIndex] = updatedList with { Name = userSchema.Lists[listIndex].Name, Namespace = userSchema.Lists[listIndex].Namespace };
        var updatedUserSchema = userSchema with { Lists = lists };
        ValidateAndWrite(updatedUserSchema);
        return Load();
    }

    public CommandSchema RemoveUserList(string name)
    {
        EnsureUserCommandsFileExists();
        var qualifiedName = ParseQualifiedName(name);
        var userSchema = ReadSchema(_userCommandsPath);
        var list = userSchema.Lists.FirstOrDefault(candidate =>
            string.Equals(candidate.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase));
        if (list is null)
        {
            throw new InvalidOperationException($"List '{qualifiedName}' was not found in the user schema.");
        }

        var references = _appSchema.Commands.Concat(userSchema.Commands)
            .SelectMany(command => command.Actions.SelectMany(action => action.Arguments
                .Where(argument => IsEnumReferenceTo(argument.Ref, list))
                .Select(argument => $"{command.QualifiedName} {action.Name} ({argument.Name})")))
            .ToArray();
        if (references.Length > 0)
        {
            throw new InvalidOperationException($"Cannot remove list '{qualifiedName}'; it is referenced by: {string.Join(", ", references)}.");
        }

        var updatedUserSchema = userSchema with
        {
            Lists = userSchema.Lists.Where(candidate => !string.Equals(GetListKey(candidate), GetListKey(list), StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        ValidateAndWrite(updatedUserSchema);
        return Load();
    }

    public CommandDefinition? FindUserCommand(string name)
    {
        var qualifiedName = ParseQualifiedName(name);
        return ReadSchema(_userCommandsPath).Commands.FirstOrDefault(command =>
            string.Equals(command.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase));
    }

    public CommandListDefinition? FindUserList(string name)
    {
        var qualifiedName = ParseQualifiedName(name);
        return ReadSchema(_userCommandsPath).Lists.FirstOrDefault(list =>
            string.Equals(list.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<CommandDefinition> GetUserCommands() => ReadSchema(_userCommandsPath).Commands;
    public IReadOnlyList<CommandListDefinition> GetUserLists() => ReadSchema(_userCommandsPath).Lists;

    public CommandSchema ReadUserSchemaForTransfer()
    {
        EnsureUserCommandsFileExists();
        return ReadSchema(_userCommandsPath);
    }

    public void ValidateUserSchemaForTransfer(
        CommandSchema userSchema,
        IEnumerable<string>? commandsMovingFromPacks = null)
    {
        var allowedPackCommandNames = (commandsMovingFromPacks ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appCommandConflict = _appSchema.Commands.FirstOrDefault(appCommand =>
            userSchema.Commands.Any(userCommand => string.Equals(
                userCommand.QualifiedName,
                appCommand.QualifiedName,
                StringComparison.OrdinalIgnoreCase))
            && !allowedPackCommandNames.Contains(appCommand.QualifiedName));
        if (appCommandConflict is not null)
        {
            throw new InvalidOperationException($"Command name '{appCommandConflict.QualifiedName}' is already in use.");
        }

        var merged = Merge(_appSchema, userSchema);
        var errors = new CommandSchemaValidator().Validate(merged, SupportedKinds(merged));
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }

    public void WriteUserSchemaForTransfer(
        CommandSchema userSchema,
        IEnumerable<string>? commandsMovingFromPacks = null)
    {
        ValidateUserSchemaForTransfer(userSchema, commandsMovingFromPacks);
        WriteSchema(userSchema);
    }

    public void RestoreUserSchemaForTransfer(CommandSchema userSchema)
        => WriteSchema(userSchema);

    private void ValidateAndWrite(CommandSchema userSchema)
    {
        var merged = Merge(_appSchema, userSchema);
        var errors = new CommandSchemaValidator().Validate(merged, SupportedKinds(merged));
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        WriteSchema(userSchema);
    }

    private void EnsureAvailableCommandName(string qualifiedName, CommandSchema userSchema)
    {
        if (Merge(_appSchema, userSchema).Commands.Any(command =>
            string.Equals(command.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Command name '{qualifiedName}' is already in use. Extension and pack commands cannot be overwritten.");
        }
    }

    private void EnsureAvailableListName(CommandListDefinition list, CommandSchema userSchema)
    {
        if (Merge(_appSchema, userSchema).Lists.Any(existing =>
            string.Equals(GetListKey(existing), GetListKey(list), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"List name '{list.QualifiedName}' is already in use. Extension and pack lists cannot be overwritten.");
        }
    }

    private static bool IsEnumReferenceTo(string? reference, CommandListDefinition list)
        => reference is not null && reference.StartsWith("enum:", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(reference[5..], list.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(reference[5..], list.QualifiedName, StringComparison.OrdinalIgnoreCase));

    private static string ParseQualifiedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Names must be non-empty and contain no whitespace.", nameof(name));
        }

        var separator = name.IndexOf(':');
        if (separator < 0) return name;
        if (separator == 0 || separator == name.Length - 1 || name.IndexOf(':', separator + 1) >= 0)
        {
            throw new ArgumentException("Namespaced names must use the form namespace:name.", nameof(name));
        }

        return name;
    }

    public CommandSchema UpdateUserCommand(CommandDefinition updatedCommand, string? originalQualifiedName = null)
    {
        EnsureUserCommandsFileExists();
        var userSchema = ReadSchema(_userCommandsPath);
        originalQualifiedName ??= updatedCommand.QualifiedName;
        var commandIndex = userSchema.Commands.ToList().FindIndex(command =>
            string.Equals(command.QualifiedName, originalQualifiedName, StringComparison.OrdinalIgnoreCase));
        if (commandIndex < 0 || _packedCommandNames.Contains(originalQualifiedName))
        {
            throw new InvalidOperationException($"Command '{originalQualifiedName}' is not owned by the user schema.");
        }

        var commands = userSchema.Commands.ToArray();
        commands[commandIndex] = updatedCommand with
        {
            Kind = commands[commandIndex].Kind,
            Handler = commands[commandIndex].ExecutionHandler
        };
        var updatedUserSchema = userSchema with { Commands = commands };
        var mergedSchema = Merge(_appSchema, updatedUserSchema);
        if (mergedSchema.Commands.Count(command => string.Equals(
            command.QualifiedName, updatedCommand.QualifiedName, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            throw new InvalidOperationException($"Command name '{updatedCommand.QualifiedName}' is already in use.");
        }
        var errors = new CommandSchemaValidator().Validate(mergedSchema, SupportedKinds(mergedSchema));
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        WriteSchema(updatedUserSchema);
        return Load();
    }

    public bool ContainsVariable(string name)
    {
        var variable = ParseVariable(name, null);
        return Load().Variables.Any(existing =>
            string.Equals(existing.QualifiedName, variable.QualifiedName, StringComparison.OrdinalIgnoreCase));
    }

    public bool RemoveVariable(string name, out CommandSchema schema)
    {
        EnsureUserCommandsFileExists();
        var variable = ParseVariable(name, null);
        var userSchema = ReadSchema(_userCommandsPath);
        var variables = userSchema.Variables
            .Where(existing => !string.Equals(existing.QualifiedName, variable.QualifiedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (variables.Length == userSchema.Variables.Count)
        {
            schema = Merge(_appSchema, userSchema);
            return false;
        }

        WriteSchema(userSchema with { Variables = variables });
        schema = Load();
        return true;
    }

    private void EnsureUserCommandsFileExists()
    {
        if (File.Exists(_userCommandsPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_userCommandsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_userCommandsPath, JsonSerializer.Serialize(new CommandSchema([], []), SerializerOptions));
    }

    private void MigrateLegacyCommandsOnce()
    {
        if (File.Exists(_migrationMarkerPath) || !File.Exists(_defaultUserCommandsPath))
        {
            return;
        }

        var userSchema = ReadSchema(_userCommandsPath);
        var defaultSchema = ReadSchema(_defaultUserCommandsPath);
        var userCommands = userSchema.Commands ?? [];
        var defaultCommands = defaultSchema.Commands ?? [];
        userCommands = userCommands.Where(command => !_packedCommandNames.Contains(command.QualifiedName)).ToArray();
        var userLists = userSchema.Lists ?? [];
        var defaultLists = defaultSchema.Lists ?? [];
        var userVariables = userSchema.Variables ?? [];
        var defaultVariables = defaultSchema.Variables ?? [];
        var userFunctions = userSchema.Functions ?? [];
        var defaultFunctions = defaultSchema.Functions ?? [];
        var commands = userCommands
            .Concat(defaultCommands.Where(defaultCommand => !_packedCommandNames.Contains(defaultCommand.QualifiedName)
                && !userCommands.Any(userCommand =>
                string.Equals(userCommand.QualifiedName, defaultCommand.QualifiedName, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var lists = userLists
            .Concat(defaultLists.Where(defaultList => !userLists.Any(userList =>
                string.Equals(GetListKey(userList), GetListKey(defaultList), StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var variables = userVariables
            .Concat(defaultVariables.Where(defaultVariable => !userVariables.Any(userVariable =>
                string.Equals(userVariable.QualifiedName, defaultVariable.QualifiedName, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var functions = userFunctions
            .Concat(defaultFunctions.Where(defaultFunction => !userFunctions.Any(userFunction =>
                string.Equals(userFunction.QualifiedName, defaultFunction.QualifiedName, StringComparison.OrdinalIgnoreCase))))
            .ToArray();

        WriteSchema(new CommandSchema(lists, commands)
        {
            Variables = variables,
            Functions = functions
        });
        File.WriteAllText(_migrationMarkerPath, string.Empty);
    }

    private void RemovePackedCommands(CommandSchema userSchema)
    {
        if (_packedCommandNames.Count == 0)
        {
            return;
        }

        var commands = (userSchema.Commands ?? [])
            .Where(command => !_packedCommandNames.Contains(command.QualifiedName))
            .ToArray();
        if (commands.Length != userSchema.Commands.Count)
        {
            WriteSchema(userSchema with { Commands = commands });
        }
    }

    private static CommandSchema ReadSchema(string path)
        => JsonSerializer.Deserialize<CommandSchema>(File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidDataException($"Command schema '{path}' is empty.");

    private void WriteSchema(CommandSchema schema)
    {
        var temporaryPath = _userCommandsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(schema, SerializerOptions));
            File.Move(temporaryPath, _userCommandsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IReadOnlyList<string> SupportedKinds(CommandSchema schema)
        => schema.Commands.Select(command => command.ExecutionHandler)
            .Concat(["url", "shell", "variable", "secret", "secretList", "theme", "exit", "profileBackup", "profileRestore", "snippet", "extension", "edit", "shortcut", "definitionManagement"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static CommandSchema NormalizeCommandHandlers(CommandSchema schema)
        => schema with { Commands = schema.Commands.Select(command => command.NormalizeLegacyHandler()).ToArray() };

    private static CommandVariableDefinition ParseVariable(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Any(char.IsWhiteSpace)
            || name.IndexOfAny(['/', '<', '>', '?', '&', '#']) >= 0)
        {
            throw new ArgumentException("Variable names must be non-empty and cannot contain whitespace or URL delimiters.", nameof(name));
        }

        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Variable values must be non-empty.", nameof(value));
        }

        var separator = name.IndexOf(':');
        if (separator < 0)
        {
            return new CommandVariableDefinition(name, value ?? string.Empty);
        }

        if (separator == 0 || separator == name.Length - 1 || name.IndexOf(':', separator + 1) >= 0)
        {
            throw new ArgumentException("Namespaced variable names must use the form namespace:name.", nameof(name));
        }

        return new CommandVariableDefinition(name[(separator + 1)..], value ?? string.Empty, name[..separator]);
    }

    private static CommandSchema Merge(CommandSchema appSchema, CommandSchema userSchema)
    {
        var userCommandNames = userSchema.Commands
            .Select(command => command.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commands = appSchema.Commands
            .Where(command => !userCommandNames.Contains(command.QualifiedName))
            .Concat(userSchema.Commands)
            .ToArray();

        var userListKeys = userSchema.Lists
            .Select(GetListKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lists = appSchema.Lists
            .Where(list => !userListKeys.Contains(GetListKey(list)))
            .Concat(userSchema.Lists)
            .ToArray();

        var userVariableNames = userSchema.Variables
            .Select(variable => variable.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variables = appSchema.Variables
            .Where(variable => !userVariableNames.Contains(variable.QualifiedName))
            .Concat(userSchema.Variables)
            .ToArray();

        var userFunctionNames = userSchema.Functions
            .Select(function => function.QualifiedName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var functions = appSchema.Functions
            .Where(function => !userFunctionNames.Contains(function.QualifiedName))
            .Concat(userSchema.Functions)
            .ToArray();
        var settings = appSchema.Settings
            .Concat(userSchema.Settings)
            .ToArray();

        return new CommandSchema(lists, commands)
        {
            Variables = variables,
            Functions = functions,
            Settings = settings
        };
    }

    private static string GetListKey(CommandListDefinition list)
        => $"{list.Type}:{list.Name}";
}
