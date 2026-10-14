using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed record LoadedCommandPacks(
    CommandSchema Schema,
    IReadOnlyDictionary<string, string> CommandSources,
    IReadOnlyList<LoadedCommandPackInfo> Packs,
    IReadOnlySet<string> DisabledCommandNames,
    IReadOnlySet<string> PackedCommandNames)
{
    public CommandSchema FilterDisabledCommands(CommandSchema schema)
        => DisabledCommandNames.Count == 0
            ? schema
            : schema with
            {
                Commands = schema.Commands
                    .Where(command => !DisabledCommandNames.Contains(command.QualifiedName))
                    .ToArray()
            };
}

public static class CommandSourceLabels
{
    public static string GetLabel(
        CommandDefinition command,
        IReadOnlyDictionary<string, string>? commandSources,
        IReadOnlySet<string>? userCommandNames,
        IReadOnlyDictionary<string, string>? commandPackSources = null)
    {
        if (userCommandNames?.Contains(command.QualifiedName) == true)
        {
            return "User";
        }

        if (commandPackSources is not null && commandPackSources.TryGetValue(command.QualifiedName, out var packId))
        {
            return $"Pack [{packId}]";
        }

        if (commandSources is not null && commandSources.TryGetValue(command.QualifiedName, out var extensionId))
        {
            return $"Extension [{extensionId}]";
        }

        return "App";
    }
}

public sealed record LoadedCommandPackInfo(string Id, bool Enabled)
{
    public IReadOnlyList<string> Commands { get; init; } = [];
}

public sealed class CommandPackLoader
{
    private static readonly Regex IdPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex NamespacePattern = new("^[a-z][a-z0-9-]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> SupportedCommandKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "url",
        "shell",
        "shortcut"
    };
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IReadOnlyList<string> _roots;
    private readonly ExtensionStateStore? _stateStore;
    private readonly string _profilePackDirectory;

    public CommandPackLoader()
    {
        _profilePackDirectory = GetProfilePackDirectory();
        _roots = [_profilePackDirectory];
        _stateStore = new ExtensionStateStore();
    }

    public CommandPackLoader(ExtensionStateStore stateStore)
    {
        _profilePackDirectory = GetProfilePackDirectory();
        _roots = [_profilePackDirectory];
        _stateStore = stateStore;
    }

    public CommandPackLoader(IEnumerable<string> roots)
        : this(roots, null)
    {
    }

    public CommandPackLoader(IEnumerable<string> roots, ExtensionStateStore? stateStore)
    {
        _roots = roots.ToArray();
        _stateStore = stateStore;
        _profilePackDirectory = _roots.FirstOrDefault() ?? GetProfilePackDirectory();
    }

    public static string GetProfilePackDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "packs");

    public string GetInstalledPackPath(string id)
    {
        ValidateId(id, "pack ID");
        return Path.Combine(_profilePackDirectory, $"{id}.json");
    }

    public string InstallPack(string sourcePath, bool replaceExisting)
        => InstallPack(sourcePath, _profilePackDirectory, replaceExisting);

    public void UpdatePackCommand(string packId, string originalQualifiedName, CommandDefinition updatedCommand)
    {
        var path = GetInstalledPackPath(packId);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Command pack '{packId}' is not installed in the profile pack directory.");
        }

        var manifest = ReadManifest(path);
        var commandIndex = manifest.Schema.Commands.ToList().FindIndex(command =>
            string.Equals(
                (command with { Namespace = command.Namespace ?? manifest.Namespace }).QualifiedName,
                originalQualifiedName,
                StringComparison.OrdinalIgnoreCase));
        if (commandIndex < 0)
        {
            throw new InvalidOperationException($"Command '{originalQualifiedName}' is not defined by pack '{packId}'.");
        }

        var commands = manifest.Schema.Commands.ToArray();
        commands[commandIndex] = updatedCommand;
        var updatedManifest = manifest with { Schema = manifest.Schema with { Commands = commands } };
        ValidateManifest(updatedManifest, packId, path);
        foreach (var command in updatedManifest.Schema.Commands)
        {
            ValidateCommand(command with { Namespace = command.Namespace ?? updatedManifest.Namespace }, path);
        }

        if (commands.Select(command => (command with { Namespace = command.Namespace ?? updatedManifest.Namespace }).QualifiedName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != commands.Length)
        {
            throw new InvalidOperationException($"Command name '{updatedCommand.QualifiedName}' is already defined by pack '{packId}'.");
        }

        WriteManifestAtomically(path, updatedManifest);
    }

    public CommandSchema Load(CommandSchema? existingSchema = null)
        => LoadWithSources(existingSchema).Schema;

    public LoadedCommandPacks LoadWithSources(
        CommandSchema? existingSchema = null,
        IEnumerable<string>? existingComponentIds = null)
    {
        var manifestPaths = DiscoverManifestPaths();
        var existingIds = new HashSet<string>(existingComponentIds ?? [], StringComparer.OrdinalIgnoreCase);
        var duplicateId = manifestPaths.Keys.FirstOrDefault(existingIds.Contains);
        if (duplicateId is not null)
        {
            throw new InvalidDataException($"Command pack ID '{duplicateId}' conflicts with an extension ID.");
        }

        var disabledIds = _stateStore?.GetDisabledExtensionIds()
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packs = new List<LoadedCommandPackInfo>();
        var schema = existingSchema ?? new CommandSchema([], []);
        var lists = schema.Lists.ToList();
        var commands = schema.Commands.ToList();
        var variables = schema.Variables.ToList();
        var functions = schema.Functions.ToList();
        var settings = schema.Settings.ToList();
        var listNames = lists.Select(GetListKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commandNames = commands.Select(command => command.QualifiedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var variableNames = variables.Select(variable => variable.QualifiedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var functionNames = functions.Select(function => function.QualifiedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commandSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var disabledCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packedCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, manifestPath) in manifestPaths.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var manifest = ReadManifest(manifestPath);
            ValidateManifest(manifest, id, manifestPath);
            packs.Add(new LoadedCommandPackInfo(id, !disabledIds.Contains(id))
            {
                Commands = manifest.Schema.Commands
                    .Select(command => (command with { Namespace = command.Namespace ?? manifest.Namespace }).QualifiedName)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });

            foreach (var command in manifest.Schema.Commands)
            {
                var namespacedCommand = command with { Namespace = command.Namespace ?? manifest.Namespace };
                packedCommandNames.Add(namespacedCommand.QualifiedName);
                if (disabledIds.Contains(id))
                {
                    disabledCommandNames.Add(namespacedCommand.QualifiedName);
                }
            }

            if (disabledIds.Contains(id))
            {
                continue;
            }

            foreach (var list in manifest.Schema.Lists)
            {
                AddUnique(listNames, GetListKey(list), "list", manifestPath);
                lists.Add(list);
            }

            foreach (var command in manifest.Schema.Commands)
            {
                var namespacedCommand = command with { Namespace = command.Namespace ?? manifest.Namespace };
                ValidateCommand(namespacedCommand, manifestPath);
                AddUnique(commandNames, namespacedCommand.QualifiedName, "command", manifestPath);
                commandSources.Add(namespacedCommand.QualifiedName, manifest.Id);
                commands.Add(namespacedCommand);
            }

            foreach (var variable in manifest.Schema.Variables)
            {
                var namespacedVariable = variable with { Namespace = variable.Namespace ?? manifest.Namespace };
                ValidateQualifiedNamespace(namespacedVariable.Namespace, $"variable '{namespacedVariable.Name}'", manifestPath);
                AddUnique(variableNames, namespacedVariable.QualifiedName, "variable", manifestPath);
                variables.Add(namespacedVariable);
            }

            foreach (var function in manifest.Schema.Functions)
            {
                var namespacedFunction = function with { Namespace = function.Namespace ?? manifest.Namespace };
                ValidateFunction(namespacedFunction, manifestPath);
                AddUnique(functionNames, namespacedFunction.QualifiedName, "function", manifestPath);
                functions.Add(namespacedFunction);
            }
        }

        return new LoadedCommandPacks(new CommandSchema(lists, commands)
        {
            Variables = variables,
            Functions = functions,
            Settings = settings
        }, commandSources, packs.OrderBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase).ToArray(), disabledCommandNames, packedCommandNames);
    }

    private Dictionary<string, string> DiscoverManifestPaths()
    {
        var manifestPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                ValidateId(id, path);
                manifestPaths[id] = path;
            }
        }

        return manifestPaths;
    }

    private static CommandPackManifest ReadManifest(string path, bool requireMatchingFileId = true)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<CommandPackManifest>(File.ReadAllText(path), SerializerOptions)
                ?? throw new InvalidDataException($"Command pack manifest '{path}' is empty.");
            ValidateManifest(
                manifest,
                requireMatchingFileId ? Path.GetFileNameWithoutExtension(path) : manifest.Id,
                path);
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Command pack manifest '{path}' is invalid: {exception.Message}", exception);
        }
    }

    private static void WriteManifestAtomically(string path, CommandPackManifest manifest)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string ValidateManifestFile(string sourcePath)
    {
        var manifest = ReadManifest(sourcePath, requireMatchingFileId: false);
        var schema = manifest.Schema;
        foreach (var command in schema.Commands)
        {
            var namespacedCommand = command with { Namespace = command.Namespace ?? manifest.Namespace };
            ValidateCommand(namespacedCommand, sourcePath);
        }

        foreach (var variable in schema.Variables)
        {
            ValidateQualifiedNamespace(variable.Namespace ?? manifest.Namespace, $"variable '{variable.Name}'", sourcePath);
        }

        foreach (var function in schema.Functions)
        {
            ValidateFunction(function with { Namespace = function.Namespace ?? manifest.Namespace }, sourcePath);
        }

        return manifest.Id;
    }

    public static string InstallPack(
        string sourcePath,
        string profilePackDirectory,
        bool replaceExisting)
    {
        var id = ValidateManifestFile(sourcePath);
        Directory.CreateDirectory(profilePackDirectory);
        var destination = Path.Combine(profilePackDirectory, $"{id}.json");
        if (File.Exists(destination) && !replaceExisting)
        {
            throw new IOException($"Command pack '{id}' is already installed.");
        }

        var temporaryPath = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, destination, overwrite: replaceExisting);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return id;
    }

    private static void ValidateManifest(CommandPackManifest manifest, string fileId, string path)
    {
        ValidateId(manifest.Id, path);
        if (!string.Equals(manifest.Id, fileId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Command pack manifest '{path}' declares ID '{manifest.Id}', but its filename requires ID '{fileId}'.");
        }

        if (manifest.Schema is null
            || manifest.Schema.Lists is null
            || manifest.Schema.Commands is null
            || manifest.Schema.Variables is null
            || manifest.Schema.Functions is null)
        {
            throw new InvalidDataException($"Command pack manifest '{path}' must define a complete schema.");
        }

        if (manifest.Namespace is not null)
        {
            ValidateQualifiedNamespace(manifest.Namespace, $"pack '{manifest.Id}'", path);
        }
    }

    private static void ValidateCommand(CommandDefinition command, string path)
    {
        if (!string.IsNullOrEmpty(command.Namespace))
        {
            ValidateQualifiedNamespace(command.Namespace, $"command '{command.Name}'", path);
        }

        if (!SupportedCommandKinds.Contains(command.Kind))
        {
            throw new InvalidDataException(
                $"Command pack manifest '{path}' uses unsupported command kind '{command.Kind}' for command '{command.QualifiedName}'.");
        }
    }

    private static void ValidateFunction(CommandFunctionDefinition function, string path)
    {
        ValidateQualifiedNamespace(function.Namespace, $"function '{function.Name}'", path);
        if (!string.Equals(function.Kind, "httpGet", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(function.Name)
            || function.Name.Contains(':')
            || function.Parameters is null
            || function.Parameters.Any(parameter => parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
            || function.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != function.Parameters.Count
            || string.IsNullOrWhiteSpace(function.TargetTemplate)
            || function.TimeoutSeconds is < 1 or > 300
            || !string.IsNullOrWhiteSpace(function.ScriptPath)
            || !string.IsNullOrWhiteSpace(function.Interpreter))
        {
            throw new InvalidDataException(
                $"Command pack manifest '{path}' function '{function.QualifiedName}' must use the built-in httpGet kind and cannot reference scripts or executors.");
        }
    }

    private static void ValidateQualifiedNamespace(string? value, string subject, string path)
    {
        if (string.IsNullOrEmpty(value) || !NamespacePattern.IsMatch(value))
        {
            throw new InvalidDataException(
                $"Command pack manifest '{path}' {subject} must resolve to a namespace containing lowercase letters, digits, or hyphens and starting with a letter.");
        }
    }

    private static void ValidateId(string id, string path)
    {
        if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id))
        {
            throw new InvalidDataException(
                $"Command pack file '{path}' must use a lowercase alphanumeric ID with single hyphens between segments.");
        }
    }

    private static void AddUnique(ISet<string> values, string key, string type, string path)
    {
        if (!values.Add(key))
        {
            throw new InvalidDataException($"Command pack manifest '{path}' defines a duplicate {type} '{key}'.");
        }
    }

    private static string GetListKey(CommandListDefinition list) => $"{list.Type}:{list.Name}";

    private static string[] GetDefaultRoots()
        => [GetProfilePackDirectory()];
}
