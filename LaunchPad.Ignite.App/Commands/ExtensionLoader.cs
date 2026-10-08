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
    IReadOnlyDictionary<string, ICommandExecutor> Executors);

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

    public ExtensionLoader()
        : this(
        [
            Path.Combine(AppContext.BaseDirectory, "extensions"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaunchPad.Ignite",
                "extensions")
        ])
    {
    }

    public ExtensionLoader(IEnumerable<string> extensionRoots)
    {
        _extensionRoots = extensionRoots.ToArray();
    }

    public LoadedExtensions Load()
    {
        var manifests = new Dictionary<string, (ExtensionManifest Manifest, string Directory)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _extensionRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var extensionDirectory in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var manifestPath = Path.Combine(extensionDirectory, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

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
        }

        var lists = new List<CommandListDefinition>();
        var commands = new List<CommandDefinition>();
        var variables = new List<CommandVariableDefinition>();
        var executors = new Dictionary<string, ICommandExecutor>(StringComparer.OrdinalIgnoreCase);
        foreach (var (manifest, extensionDirectory) in manifests.Values)
        {
            lists.AddRange(manifest.Schema.Lists);
            commands.AddRange(manifest.Schema.Commands.Select(command => command with { Namespace = manifest.Namespace }));
            variables.AddRange(manifest.Schema.Variables.Select(variable => variable with { Namespace = manifest.Namespace }));
            LoadExecutors(manifest, extensionDirectory, executors);
        }

        return new LoadedExtensions(new CommandSchema(lists, commands) { Variables = variables }, executors);
    }

    private static void LoadExecutors(
        ExtensionManifest manifest,
        string extensionDirectory,
        IDictionary<string, ICommandExecutor> executors)
    {
        if (string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            return;
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
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(ICommandExecutor).IsAssignableFrom(type))
            .ToArray();
        if (executorTypes.Length == 0)
        {
            throw new InvalidDataException($"Extension '{manifest.Id}' assembly does not contain any command executors.");
        }

        foreach (var executorType in executorTypes)
        {
            if (Activator.CreateInstance(executorType) is not ICommandExecutor executor
                || string.IsNullOrWhiteSpace(executor.Kind)
                || !executors.TryAdd(executor.Kind, executor))
            {
                throw new InvalidDataException($"Extension '{manifest.Id}' has an invalid or duplicate command executor '{executorType.FullName}'.");
            }
        }
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
