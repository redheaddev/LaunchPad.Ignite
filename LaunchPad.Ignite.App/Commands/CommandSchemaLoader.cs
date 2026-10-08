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

        return new CommandSchema(lists, commands) { Variables = variables };
    }

    private static string GetListKey(CommandListDefinition list)
        => $"{list.Type}:{list.Name}";
}
