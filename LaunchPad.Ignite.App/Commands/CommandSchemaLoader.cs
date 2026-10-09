using System;
using System.IO;
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

    public CommandSchemaLoader(CommandSchema appSchema)
        : this(
            appSchema,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaunchPad.Ignite",
                "user-commands.json"))
    {
    }

    public CommandSchemaLoader(CommandSchema appSchema, string userCommandsPath)
    {
        _appSchema = appSchema;
        _userCommandsPath = userCommandsPath;
    }

    public CommandSchema Load()
    {
        EnsureUserCommandsFileExists();
        var userSchema = ReadSchema(_userCommandsPath);
        return Merge(_appSchema, userSchema);
    }

    public CommandSchema AddVariable(string name, string value)
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

    private static CommandSchema ReadSchema(string path)
        => JsonSerializer.Deserialize<CommandSchema>(File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidDataException($"Command schema '{path}' is empty.");

    private void WriteSchema(CommandSchema schema)
        => File.WriteAllText(_userCommandsPath, JsonSerializer.Serialize(schema, SerializerOptions));

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

        return new CommandSchema(lists, commands)
        {
            Variables = variables,
            Functions = functions
        };
    }

    private static string GetListKey(CommandListDefinition list)
        => $"{list.Type}:{list.Name}";
}
