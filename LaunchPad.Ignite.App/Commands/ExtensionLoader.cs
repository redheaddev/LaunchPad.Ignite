using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed record LoadedExtensions(
    CommandSchema Schema,
    IReadOnlyDictionary<string, ICommandExecutor> Executors,
    IReadOnlyDictionary<string, ICommandFunctionExecutor> FunctionExecutors,
    IReadOnlyList<LoadedExtensionInfo> Extensions)
{
    public IReadOnlyList<ISecretEnvironmentHandoff> SecretEnvironmentHandoffs { get; init; } = [];

    public IReadOnlyList<ExtensionLoadFailure> Failures { get; init; } = [];

    public IReadOnlyDictionary<string, IAsyncCommandExecutor> AsyncExecutors { get; init; }
        = new Dictionary<string, IAsyncCommandExecutor>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> CommandSources { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void ConfigureCommandExecutors(
        IEnumerable<CommandVariableDefinition> variables,
        IReadOnlyDictionary<string, string> settingValues,
        Func<string, string, (bool Found, string Value)> tryGetSecret,
        Action<string> openUrl)
    {
        var settingsByNamespace = variables
            .GroupBy(variable => variable.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group.ToDictionary(
                    variable => variable.Name,
                    variable => variable.Value,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var profileValues = settingValues;
        var definitionsByNamespace = Schema.Settings
            .GroupBy(setting => setting.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var executor in Executors.Values.Cast<object>().Concat(AsyncExecutors.Values))
        {
            if (executor is not IExtensionCommandContextAware contextAware)
            {
                continue;
            }

            var extensionNamespace = contextAware.Namespace;
            var settings = settingsByNamespace.TryGetValue(extensionNamespace, out var namespaceSettings)
                ? namespaceSettings
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (definitionsByNamespace.TryGetValue(extensionNamespace, out var settingDefinitions))
            {
                var configuredSettings = settings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var setting in settingDefinitions)
                {
                    configuredSettings[setting.Name] = profileValues.GetValueOrDefault(setting.QualifiedName, setting.DefaultValue);
                }

                settings = configuredSettings;
            }
            contextAware.Configure(
                settings,
                name => tryGetSecret(extensionNamespace, name),
                openUrl);

        }
    }
}

public sealed record ExtensionLoadFailure(string Location, string Message);

public sealed record LoadedExtensionInfo(string Id, string Version, bool Enabled)
{
    public string Namespace { get; init; } = Id;

    public string? LoadError { get; init; }
}

public sealed class ExtensionLoader
{
    private const string ManifestFileName = "extension.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IReadOnlyList<string> _extensionRoots;
    private readonly ExtensionStateStore? _stateStore;

    public ExtensionLoader()
        : this(
            GetDefaultExtensionRoots(),
            new ExtensionStateStore())
    {
    }

    public ExtensionLoader(IEnumerable<string> extensionRoots)
        : this(extensionRoots, null)
    {
    }

    public ExtensionLoader(ExtensionStateStore stateStore)
        : this(GetDefaultExtensionRoots(), stateStore)
    {
    }

    public ExtensionLoader(IEnumerable<string> extensionRoots, ExtensionStateStore? stateStore)
    {
        _extensionRoots = extensionRoots.ToArray();
        _stateStore = stateStore;
    }

    public LoadedExtensions Load()
    {
        var manifests = new Dictionary<string, (ExtensionManifest Manifest, string Directory)>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<ExtensionLoadFailure>();
        foreach (var root in _extensionRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> extensionDirectories;
            try
            {
                extensionDirectories = Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failures.Add(new ExtensionLoadFailure(root, exception.Message));
                continue;
            }

            foreach (var extensionDirectory in extensionDirectories)
            {
                var manifestPath = Path.Combine(extensionDirectory, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                try
                {
                    var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), SerializerOptions)
                        ?? throw new InvalidDataException($"Extension manifest '{manifestPath}' is empty.");
                    if (string.IsNullOrWhiteSpace(manifest.Id)
                        || string.IsNullOrWhiteSpace(manifest.Name)
                        || string.IsNullOrWhiteSpace(manifest.Namespace))
                    {
                        throw new InvalidDataException($"Extension manifest '{manifestPath}' must define an id, name, and namespace.");
                    }

                    manifests[manifest.Id] = (manifest, extensionDirectory);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures.Add(new ExtensionLoadFailure(extensionDirectory, GetFailureMessage(exception)));
                }
            }
        }

        var disabledIds = _stateStore?.GetDisabledExtensionIds()
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensionInfo = manifests.Values
            .Select(entry => new LoadedExtensionInfo(
                entry.Manifest.Id,
                string.IsNullOrWhiteSpace(entry.Manifest.Version) ? "unknown" : entry.Manifest.Version,
                !disabledIds.Contains(entry.Manifest.Id))
            {
                Namespace = entry.Manifest.Namespace
            })
            .OrderBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var enabledManifests = manifests.Values
            .Where(entry => !disabledIds.Contains(entry.Manifest.Id))
            .ToArray();

        var lists = new List<CommandListDefinition>();
        var commands = new List<CommandDefinition>();
        var variables = new List<CommandVariableDefinition>();
        var functions = new List<CommandFunctionDefinition>();
        var settings = new List<CommandSettingDefinition>();
        var commandSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var executors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase);
        var asyncExecutors = new Dictionary<string, IAsyncCommandExecutor>(StringComparer.OrdinalIgnoreCase);
        var functionExecutors = new Dictionary<string, ICommandFunctionExecutor>(StringComparer.OrdinalIgnoreCase);
        var secretEnvironmentHandoffs = new List<ISecretEnvironmentHandoff>();
        var extensionLoadErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (manifest, extensionDirectory) in enabledManifests)
        {
            try
            {
                var extensionLists = (manifest.Schema.Lists ?? Array.Empty<CommandListDefinition>())
                    .Select(list => list with { Namespace = list.Namespace ?? manifest.Namespace })
                    .ToArray();
                var extensionCommands = (manifest.Schema.Commands ?? Array.Empty<CommandDefinition>()).Select(command => command with
                {
                    Namespace = command.Namespace ?? manifest.Namespace
                }).ToArray();
                var extensionVariables = (manifest.Schema.Variables ?? Array.Empty<CommandVariableDefinition>())
                    .Select(variable => variable with { Namespace = manifest.Namespace })
                    .ToArray();
                var extensionSettings = (manifest.Schema.Settings ?? Array.Empty<CommandSettingDefinition>())
                    .Select(setting => setting with { Namespace = manifest.Namespace })
                    .ToArray();
                var extensionFunctions = (manifest.Schema.Functions ?? Array.Empty<CommandFunctionDefinition>())
                    .Select(function => LoadFunction(function, manifest, extensionDirectory))
                    .ToArray();
                var extensionExecutors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase);
                var extensionAsyncExecutors = new Dictionary<string, IAsyncCommandExecutor>(StringComparer.OrdinalIgnoreCase);
                var extensionFunctionExecutors = new Dictionary<string, ICommandFunctionExecutor>(StringComparer.OrdinalIgnoreCase);
                var extensionSecretHandoffs = new List<ISecretEnvironmentHandoff>();
                var registeredFunctionNames = LoadExecutors(
                    manifest,
                    extensionDirectory,
                    extensionExecutors,
                    extensionAsyncExecutors,
                    extensionFunctionExecutors,
                    extensionSecretHandoffs);
                var extensionFunctionNameSet = registeredFunctionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var function in extensionFunctions)
                {
                    var hasExtensionExecutor = extensionFunctionNameSet.Contains(function.QualifiedName);
                    if (function.Kind is null && !hasExtensionExecutor)
                    {
                        throw new InvalidDataException($"Function '{function.QualifiedName}' has no matching extension function executor.");
                    }

                    if (function.Kind is not null && hasExtensionExecutor)
                    {
                        throw new InvalidDataException($"Function '{function.QualifiedName}' is registered both by kind and by an extension executor.");
                    }
                }

                foreach (var functionName in registeredFunctionNames)
                {
                    if (!extensionFunctions.Any(function =>
                        string.Equals(function.QualifiedName, functionName, StringComparison.OrdinalIgnoreCase)
                        && function.Kind is null))
                    {
                        throw new InvalidDataException(
                            $"Extension '{manifest.Id}' function executor '{functionName}' has no matching function definition in its manifest.");
                    }
                }

                var candidateSchema = new CommandSchema(
                    lists.Concat(extensionLists).ToArray(),
                    commands.Concat(extensionCommands).ToArray())
                {
                    Variables = variables.Concat(extensionVariables).ToArray(),
                    Functions = functions.Concat(extensionFunctions).ToArray(),
                    Settings = settings.Concat(extensionSettings).ToArray()
                };
                var supportedKinds = executors.Keys
                    .Concat(asyncExecutors.Keys)
                    .Concat(extensionExecutors.Keys)
                    .Concat(extensionAsyncExecutors.Keys);
                var schemaErrors = new CommandSchemaValidator().Validate(candidateSchema, supportedKinds);
                if (schemaErrors.Count > 0)
                {
                    throw new InvalidDataException(string.Join(Environment.NewLine, schemaErrors));
                }

                EnsureNoDuplicates(extensionCommands.Select(command => command.QualifiedName), commandSources.Keys, "command");
                EnsureNoDuplicates(extensionExecutors.Keys.Concat(extensionAsyncExecutors.Keys), executors.Keys.Concat(asyncExecutors.Keys), "command executor");
                EnsureNoDuplicates(extensionFunctionExecutors.Keys, functionExecutors.Keys, "function executor");

                lists.AddRange(extensionLists);
                commands.AddRange(extensionCommands);
                variables.AddRange(extensionVariables);
                settings.AddRange(extensionSettings);
                functions.AddRange(extensionFunctions);
                foreach (var command in extensionCommands)
                {
                    commandSources.Add(command.QualifiedName, manifest.Id);
                }

                foreach (var executor in extensionExecutors)
                {
                    executors.Add(executor.Key, executor.Value);
                }

                foreach (var executor in extensionAsyncExecutors)
                {
                    asyncExecutors.Add(executor.Key, executor.Value);
                }

                foreach (var executor in extensionFunctionExecutors)
                {
                    functionExecutors.Add(executor.Key, executor.Value);
                }

                secretEnvironmentHandoffs.AddRange(extensionSecretHandoffs);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                var failureMessage = GetFailureMessage(exception);
                extensionLoadErrors[manifest.Id] = failureMessage;
                failures.Add(new ExtensionLoadFailure(manifest.Id, failureMessage));
            }

        }

        extensionInfo = extensionInfo
            .Select(extension => extensionLoadErrors.TryGetValue(extension.Id, out var error)
                ? extension with { LoadError = error }
                : extension)
            .ToArray();

        return new LoadedExtensions(new CommandSchema(lists, commands)
        {
            Variables = variables,
            Functions = functions,
            Settings = settings
        }, executors, functionExecutors, extensionInfo)
        {
            CommandSources = commandSources,
            AsyncExecutors = asyncExecutors,
            SecretEnvironmentHandoffs = secretEnvironmentHandoffs,
            Failures = failures
        };
    }

    private static string GetFailureMessage(Exception exception)
        => exception is FileNotFoundException { FileName: not null } fileNotFound
            ? $"{fileNotFound.Message} ({fileNotFound.FileName})"
            : exception.Message;

    private static void EnsureNoDuplicates(IEnumerable<string> candidates, IEnumerable<string> existing, string itemType)
    {
        var existingNames = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicate = candidates.FirstOrDefault(name => !existingNames.Add(name));
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Duplicate {itemType} '{duplicate}' is already registered.");
        }
    }

    private static string[] GetDefaultExtensionRoots()
        =>
        [
            Path.Combine(AppContext.BaseDirectory, "extensions"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaunchPad.Ignite",
                "extensions")
        ];

    private static IEnumerable<object> ExecutorsForNamespace(
        string kind,
        IReadOnlyDictionary<string, ICommandExecutor> executors,
        IReadOnlyDictionary<string, IAsyncCommandExecutor> asyncExecutors)
    {
        if (executors.TryGetValue(kind, out var executor))
        {
            yield return executor;
        }

        if (asyncExecutors.TryGetValue(kind, out var asyncExecutor))
        {
            yield return asyncExecutor;
        }
    }

    private static CommandFunctionDefinition LoadFunction(
        CommandFunctionDefinition function,
        ExtensionManifest manifest,
        string extensionDirectory)
    {
        if (string.IsNullOrWhiteSpace(function.Name)
            || (function.Kind is not null && string.IsNullOrWhiteSpace(function.Kind))
            || function.Parameters is null
            || function.Parameters.Any(parameter => parameter is null || string.IsNullOrWhiteSpace(parameter.Name))
            || function.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != function.Parameters.Count
            || function.TimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidDataException($"Function in extension '{manifest.Id}' has invalid name, kind, parameters, or timeout.");
        }

        var optionalParameterFound = false;
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Optional)
            {
                optionalParameterFound = true;
            }
            else if (optionalParameterFound)
            {
                throw new InvalidDataException(
                    $"Function '{function.Name}' must declare required parameters before optional parameters.");
            }
        }

        var qualifiedFunction = function with { Namespace = function.Namespace ?? manifest.Namespace };
        if (function.Kind is null)
        {
            if (string.IsNullOrWhiteSpace(manifest.Assembly))
            {
                throw new InvalidDataException($"Function '{qualifiedFunction.QualifiedName}' requires an extension assembly executor.");
            }

            return qualifiedFunction;
        }

        if (string.Equals(function.Kind, "httpGet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(function.TargetTemplate))
            {
                throw new InvalidDataException($"REST function '{qualifiedFunction.QualifiedName}' must define a targetTemplate.");
            }

            return qualifiedFunction;
        }

        if (!string.Equals(function.Kind, "script", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Function '{qualifiedFunction.QualifiedName}' has unsupported kind '{function.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(function.Interpreter) || string.IsNullOrWhiteSpace(function.ScriptPath)
            || Path.IsPathRooted(function.ScriptPath))
        {
            throw new InvalidDataException($"Script function '{qualifiedFunction.QualifiedName}' must define an interpreter and a relative scriptPath.");
        }

        var extensionRoot = Path.GetFullPath(extensionDirectory) + Path.DirectorySeparatorChar;
        var scriptPath = Path.GetFullPath(Path.Combine(extensionDirectory, function.ScriptPath));
        if (!scriptPath.StartsWith(extensionRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Script for function '{qualifiedFunction.QualifiedName}' must be inside its extension directory.");
        }

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Script for function '{qualifiedFunction.QualifiedName}' was not found.", scriptPath);
        }

        return qualifiedFunction with { ScriptPath = scriptPath };
    }

    private static IReadOnlyList<string> LoadExecutors(
        ExtensionManifest manifest,
        string extensionDirectory,
        IDictionary<string, ICommandExecutor> executors,
        IDictionary<string, IAsyncCommandExecutor> asyncExecutors,
        IDictionary<string, ICommandFunctionExecutor> functionExecutors,
        ICollection<ISecretEnvironmentHandoff> secretEnvironmentHandoffs)
    {
        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            return [];
        }

        var fullDirectory = Path.GetFullPath(extensionDirectory) + Path.DirectorySeparatorChar;
        var assemblyPath = Path.GetFullPath(Path.Combine(extensionDirectory, manifest.Assembly));
        if (!assemblyPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Extension '{manifest.Id}' assembly must be inside its extension directory.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Assembly for extension '{manifest.Id}' was not found.", assemblyPath);
        }

        var loadContext = new ExtensionLoadContext(assemblyPath);
        using var assemblyStream = File.OpenRead(assemblyPath);
        var assembly = loadContext.LoadFromStream(assemblyStream);
        var executorTypes = assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && (typeof(ICommandExecutor).IsAssignableFrom(type)
                    || typeof(IAsyncCommandExecutor).IsAssignableFrom(type)
                    || typeof(IExtensionFunctionExecutor).IsAssignableFrom(type)
                    || typeof(ISecretEnvironmentHandoff).IsAssignableFrom(type)))
            .ToArray();
        if (executorTypes.Length == 0)
        {
            throw new InvalidDataException($"Extension '{manifest.Id}' assembly does not contain any supported executors or handoff capabilities.");
        }

        var registeredFunctionNames = new List<string>();
        foreach (var executorType in executorTypes)
        {
            var instance = Activator.CreateInstance(executorType)
                ?? throw new InvalidDataException($"Extension '{manifest.Id}' executor '{executorType.FullName}' could not be created.");
            if (instance is ICommandExecutor commandExecutor
                && (string.IsNullOrWhiteSpace(commandExecutor.Kind)
                    || !executors.TryAdd(commandExecutor.Kind, commandExecutor)))
            {
                throw new InvalidDataException($"Extension '{manifest.Id}' has an invalid or duplicate command executor '{executorType.FullName}'.");
            }

            if (instance is IAsyncCommandExecutor asyncCommandExecutor
                && (string.IsNullOrWhiteSpace(asyncCommandExecutor.Kind)
                    || executors.ContainsKey(asyncCommandExecutor.Kind)
                    || !asyncExecutors.TryAdd(asyncCommandExecutor.Kind, asyncCommandExecutor)))
            {
                throw new InvalidDataException($"Extension '{manifest.Id}' has an invalid or duplicate command executor '{executorType.FullName}'.");
            }

            if (instance is IExtensionFunctionExecutor functionExecutor)
            {
                if (string.IsNullOrWhiteSpace(functionExecutor.FunctionName)
                    || functionExecutor.FunctionName.Contains(':'))
                {
                    throw new InvalidDataException(
                        $"Extension '{manifest.Id}' function executor '{executorType.FullName}' must provide an unqualified function name.");
                }

                var functionQualifiedName = string.IsNullOrEmpty(manifest.Namespace)
                    ? functionExecutor.FunctionName
                    : $"{manifest.Namespace}:{functionExecutor.FunctionName}";
                if (!functionExecutors.TryAdd(functionQualifiedName, functionExecutor))
                {
                    throw new InvalidDataException(
                        $"Extension '{manifest.Id}' has a duplicate function executor '{functionQualifiedName}'.");
                }

                registeredFunctionNames.Add(functionQualifiedName);
            }

            if (instance is ISecretEnvironmentHandoff secretEnvironmentHandoff)
            {
                secretEnvironmentHandoff.ConfigureExtensionRoot(extensionDirectory);
                secretEnvironmentHandoffs.Add(secretEnvironmentHandoff);
            }
        }

        return registeredFunctionNames;
    }

    private sealed class ExtensionLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _coreAssemblyName = typeof(ICommandExecutor).Assembly.GetName().Name!;

        public ExtensionLoadContext(string assemblyPath)
            : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, _coreAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
        }
    }
}
