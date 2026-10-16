using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;

namespace LaunchPad.Ignite.App.Services;

public sealed record AddonManifest(
    string Id,
    string Name,
    string Version,
    IReadOnlyList<string> Packs,
    IReadOnlyList<string> Snippets,
    IReadOnlyList<string>? Extensions = null);

public sealed record AddonImportPreview(
    AddonManifest Manifest,
    IReadOnlyList<string> PackIds,
    IReadOnlyList<string> SnippetNames,
    IReadOnlyList<string> ExtensionIds,
    IReadOnlyList<string> Conflicts)
{
    public IReadOnlyList<AddonPreviewItem> Items { get; init; } = [];
}

public sealed record AddonPreviewItem(
    string Category,
    string Name,
    bool AlreadyExists,
    string? ExistingOwner = null,
    IReadOnlyList<AddonPreviewItem>? Commands = null,
    string? Version = null,
    int? ImportCommandCount = null,
    int? TotalCommandCount = null);

public enum ImportConflictPolicy
{
    SkipExisting,
    OverwriteExisting
}

public sealed record AddonInstallResult(IReadOnlyList<string> SkippedItems);

public sealed record AddonPackInfo(string Id, string? Group);

public sealed class AddonPackageService
{
    private const string ManifestFileName = "addon.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly CommandPackLoader _packLoader;
    private readonly SnippetStore _snippetStore;
    private readonly ExtensionPackageManager _extensionPackageManager;
    private readonly Func<string, string?>? _getCommandOwner;

    public IReadOnlyList<AddonPackInfo> GetAvailablePacks()
    {
        var directory = _packLoader.ProfilePackDirectory;
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path =>
            {
                CommandPackLoader.ValidateManifestFile(path);
                var manifest = JsonSerializer.Deserialize<CommandPackManifest>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException($"Command pack manifest '{path}' is invalid.");
                var relativeDirectory = Path.GetDirectoryName(Path.GetRelativePath(directory, path));
                var group = string.IsNullOrEmpty(relativeDirectory) ? null : relativeDirectory;
                return new AddonPackInfo(manifest.Id, group);
            })
            .OrderBy(pack => pack.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<SnippetDefinition> GetAvailableSnippets()
        => _snippetStore.GetAll();

    public IReadOnlyList<(string Id, string Directory)> GetAvailableExtensions()
        => _extensionPackageManager.GetInstalledUserExtensions();

    public void CreatePackage(
        string path,
        string id,
        string name,
        string version,
        IEnumerable<string> packIds,
        IEnumerable<string> snippetNames,
        IEnumerable<string> extensionIds)
    {
        if (string.IsNullOrWhiteSpace(id)
            || !id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            || string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Add-on ID, name, and version are required; the ID may contain only letters, numbers, '-' and '_'.");
        }

        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
        {
            throw new ArgumentException("Add-on version must use major.minor.patch format.", nameof(version));
        }

        var packsById = GetAvailablePacks().ToDictionary(pack => pack.Id, StringComparer.OrdinalIgnoreCase);
        var snippetsByName = _snippetStore.GetAll().ToDictionary(snippet => snippet.Metadata.Name, StringComparer.OrdinalIgnoreCase);
        var extensionsById = _extensionPackageManager.GetInstalledUserExtensions()
            .ToDictionary(extension => extension.Id, StringComparer.OrdinalIgnoreCase);
        var selectedPacks = packIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedSnippets = snippetNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedExtensions = extensionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selectedPacks.Length + selectedSnippets.Length + selectedExtensions.Length == 0)
        {
            throw new ArgumentException("Select at least one pack, snippet, or extension.");
        }

        var missingPacks = selectedPacks.Where(id => !packsById.ContainsKey(id)).ToArray();
        var missingSnippets = selectedSnippets.Where(name => !snippetsByName.ContainsKey(name)).ToArray();
        var missingExtensions = selectedExtensions.Where(id => !extensionsById.ContainsKey(id)).ToArray();
        if (missingPacks.Length + missingSnippets.Length + missingExtensions.Length > 0)
        {
            throw new InvalidOperationException("One or more selected add-on components are no longer available.");
        }

        var profileDirectory = Directory.GetParent(_packLoader.ProfilePackDirectory)?.FullName
            ?? throw new InvalidOperationException("The profile pack directory must have a parent profile directory.");
        var packPaths = selectedPacks.Select(packId => Path.GetRelativePath(
            profileDirectory,
            _packLoader.GetInstalledPackPath(packId)).Replace('\\', '/')).ToArray();
        var manifest = new AddonManifest(
            id.Trim(),
            name.Trim(),
            version.Trim(),
            packPaths,
            selectedSnippets,
            selectedExtensions.Select(extensionId => $"extensions/{extensionId}/extension.json").ToArray());
        ValidateManifest(manifest);

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, ManifestFileName, JsonSerializer.Serialize(manifest, JsonOptions));
        foreach (var packPath in packPaths)
        {
            var sourcePath = Path.Combine(profileDirectory, packPath.Replace('/', Path.DirectorySeparatorChar));
            WriteEntry(archive, packPath, File.ReadAllBytes(sourcePath));
        }

        foreach (var snippetName in selectedSnippets)
        {
            var snippet = snippetsByName[snippetName];
            WriteEntry(archive, $"snippets/{snippetName}/metadata.json", JsonSerializer.Serialize(snippet.Metadata, JsonOptions));
            var scriptName = string.Equals(snippet.Metadata.Type, "powershell", StringComparison.OrdinalIgnoreCase)
                ? "script.ps1"
                : "script.sh";
            WriteEntry(archive, $"snippets/{snippetName}/{scriptName}", snippet.Script);
        }

        foreach (var extensionId in selectedExtensions)
        {
            var sourceDirectory = extensionsById[extensionId].Directory;
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath).Replace('\\', '/');
                WriteEntry(archive, $"extensions/{extensionId}/{relativePath}", File.ReadAllBytes(sourcePath));
            }
    }
    }

    private static string WriteTemporaryPack(string stagingDirectory, string packId, byte[] content)
    {
        var path = Path.Combine(stagingDirectory, $"{packId}-previous.json");
        File.WriteAllBytes(path, content);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[] content)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(content);
    }

    private static void CreateExtensionArchive(
        IEnumerable<KeyValuePair<string, ZipArchiveEntry>> entries,
        string prefix,
        string destinationPath)
    {
        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var relativePath = item.Key[prefix.Length..];
            WriteEntry(archive, relativePath, ReadBytes(item.Value));
        }
    }

    public AddonPackageService(
        CommandPackLoader packLoader,
        SnippetStore snippetStore,
        ExtensionPackageManager? extensionPackageManager = null,
        Func<string, string?>? getCommandOwner = null)
    {
        _packLoader = packLoader;
        _snippetStore = snippetStore;
        _extensionPackageManager = extensionPackageManager ?? new ExtensionPackageManager();
        _getCommandOwner = getCommandOwner;
    }

    public AddonImportPreview Preview(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = GetFileEntries(archive);
        if (!entries.TryGetValue(ManifestFileName, out var manifestEntry))
        {
            throw new InvalidDataException($"Add-on archive must contain '{ManifestFileName}' at its root.");
        }

        var manifest = JsonSerializer.Deserialize<AddonManifest>(ReadText(manifestEntry), JsonOptions)
            ?? throw new InvalidDataException("Add-on manifest is empty or invalid.");
        ValidateManifest(manifest);

        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestFileName };
        var packIds = new List<string>();
        foreach (var packPath in manifest.Packs)
        {
            var entryPath = NormalizePackagePath(packPath);
            if (!entryPath.StartsWith("packs/", StringComparison.OrdinalIgnoreCase)
                || !entryPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || !entries.TryGetValue(entryPath, out var packEntry))
            {
                throw new InvalidDataException($"Add-on pack file '{packPath}' is missing or is outside the packs directory.");
            }

            var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllBytes(temporaryPath, ReadBytes(packEntry));
                var packId = CommandPackLoader.ValidateManifestFile(temporaryPath);
                if (!string.Equals(Path.GetFileNameWithoutExtension(entryPath), packId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Pack entry '{entryPath}' must match its manifest ID '{packId}'.");
                }

                packIds.Add(packId);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            expectedFiles.Add(entryPath);
        }

        var snippetNames = new List<string>();
        foreach (var snippetName in manifest.Snippets)
        {
            SnippetStore.ValidateSnippetName(snippetName);
            var metadataPath = $"snippets/{snippetName}/metadata.json";
            if (!entries.TryGetValue(metadataPath, out var metadataEntry))
            {
                throw new InvalidDataException($"Add-on snippet '{snippetName}' is missing '{metadataPath}'.");
            }

            var metadata = JsonSerializer.Deserialize<SnippetMetadata>(ReadText(metadataEntry), JsonOptions)
                ?? throw new InvalidDataException($"Snippet metadata for '{snippetName}' is empty or invalid.");
            if (!string.Equals(metadata.Name, snippetName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Snippet directory '{snippetName}' does not match metadata name '{metadata.Name}'.");
            }

            var scriptFileName = string.Equals(metadata.Type, "powershell", StringComparison.OrdinalIgnoreCase)
                ? "script.ps1"
                : string.Equals(metadata.Type, "shell", StringComparison.OrdinalIgnoreCase)
                    ? "script.sh"
                    : throw new InvalidDataException($"Snippet '{snippetName}' has unsupported type '{metadata.Type}'.");
            var scriptPath = $"snippets/{snippetName}/{scriptFileName}";
            if (!entries.TryGetValue(scriptPath, out var scriptEntry))
            {
                throw new InvalidDataException($"Add-on snippet '{snippetName}' is missing '{scriptPath}'.");
            }

            _snippetStore.Validate(new SnippetDefinition(metadata, ReadText(scriptEntry)));
            expectedFiles.Add(metadataPath);
            expectedFiles.Add(scriptPath);
            snippetNames.Add(snippetName);
        }

        var extensionIds = new List<string>();
        var extensionManifests = new Dictionary<string, ExtensionManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var extensionPath in manifest.Extensions ?? [])
        {
            var entryPath = NormalizePackagePath(extensionPath);
            if (!entryPath.StartsWith("extensions/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Add-on extension '{extensionPath}' is outside the extensions directory.");
            }

            var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
            try
            {
                string extensionId;
                ExtensionManifest extensionManifest;
                if (entryPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && entries.TryGetValue(entryPath, out var extensionEntry))
                {
                    File.WriteAllBytes(temporaryPath, ReadBytes(extensionEntry));
                    extensionId = _extensionPackageManager.ValidatePackage(temporaryPath);
                    using (var extensionArchive = ZipFile.OpenRead(temporaryPath))
                    {
                        var packageManifestEntry = extensionArchive.GetEntry("extension.json")
                            ?? throw new InvalidDataException($"Extension package '{extensionId}' is missing its manifest.");
                        extensionManifest = JsonSerializer.Deserialize<ExtensionManifest>(ReadText(packageManifestEntry), JsonOptions)
                            ?? throw new InvalidDataException($"Extension manifest for '{extensionId}' is invalid.");
                    }
                    if (!string.Equals(Path.GetFileNameWithoutExtension(entryPath), extensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Extension entry '{entryPath}' must match its manifest ID '{extensionId}'.");
                    }

                    expectedFiles.Add(entryPath);
                }
                else
                {
                    var segments = entryPath.Split('/');
                    if (segments.Length != 3 || !string.Equals(segments[2], "extension.json", StringComparison.OrdinalIgnoreCase)
                        || !entries.ContainsKey(entryPath))
                    {
                        throw new InvalidDataException($"Add-on extension '{extensionPath}' must reference extensions/<id>/extension.json.");
                    }

                    var extensionPrefix = $"extensions/{segments[1]}/";
                    var extensionEntries = entries.Where(pair => pair.Key.StartsWith(extensionPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
                    CreateExtensionArchive(extensionEntries, extensionPrefix, temporaryPath);
                    extensionId = _extensionPackageManager.ValidatePackage(temporaryPath);
                    extensionManifest = JsonSerializer.Deserialize<ExtensionManifest>(ReadText(entries[entryPath]), JsonOptions)
                        ?? throw new InvalidDataException($"Extension manifest for '{extensionId}' is invalid.");
                    if (!string.Equals(segments[1], extensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Extension directory '{segments[1]}' must match its manifest ID '{extensionId}'.");
                    }

                    foreach (var item in extensionEntries)
                    {
                        expectedFiles.Add(item.Key);
                    }
                }

                extensionIds.Add(extensionId);
                extensionManifests.Add(extensionId, extensionManifest);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

        }

        var unreferenced = entries.Keys.Where(entry => !expectedFiles.Contains(entry)).ToArray();
        if (unreferenced.Length > 0)
        {
            throw new InvalidDataException($"Add-on archive contains unreferenced file(s): {string.Join(", ", unreferenced)}.");
        }

        if (packIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != packIds.Count
            || snippetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != snippetNames.Count
            || extensionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != extensionIds.Count)
        {
            throw new InvalidDataException("Add-on manifest contains duplicate pack IDs, snippet names, or extension IDs.");
        }

        var packItems = new List<AddonPreviewItem>();
        foreach (var packPath in manifest.Packs)
        {
            var normalized = NormalizePackagePath(packPath);
            var packId = Path.GetFileNameWithoutExtension(normalized);
            var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllBytes(temporaryPath, ReadBytes(entries[normalized]));
                var incomingPack = JsonSerializer.Deserialize<CommandPackManifest>(File.ReadAllText(temporaryPath), JsonOptions)
                    ?? throw new InvalidDataException($"Command pack '{packId}' is invalid.");
                var commands = incomingPack.Schema.Commands
                    .Select(command => command with { Namespace = command.Namespace ?? incomingPack.Namespace })
                    .Select(command =>
                    {
                        var owner = _getCommandOwner?.Invoke(command.QualifiedName);
                        return new AddonPreviewItem(
                            "Command",
                            command.QualifiedName,
                            owner is not null,
                            owner,
                            ImportCommandCount: owner is null ? 1 : 0,
                            TotalCommandCount: 1);
                    })
                    .ToArray();
                var installed = File.Exists(_packLoader.GetInstalledPackPath(packId));
                packItems.Add(new AddonPreviewItem(
                    "Command pack",
                    packId,
                    installed,
                    installed ? "Installed command pack" : null,
                    commands,
                    incomingPack.Version,
                    installed ? 0 : commands.Count(command => !command.AlreadyExists),
                    commands.Length));
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        var snippetItems = snippetNames
            .Select(name => new AddonPreviewItem("Snippet", name, _snippetStore.Find(name) is not null,
                _snippetStore.Find(name) is null ? null : "Installed snippet"))
            .ToArray();
        var extensionItems = extensionIds
            .Select(id =>
            {
                var extension = extensionManifests[id];
                var installed = _extensionPackageManager.IsUserExtensionInstalled(id);
                var commands = extension.Schema.Commands
                    .Select(command => command with { Namespace = command.Namespace ?? extension.Namespace })
                    .Select(command =>
                    {
                        var owner = _getCommandOwner?.Invoke(command.QualifiedName);
                        return new AddonPreviewItem("Command", command.QualifiedName, owner is not null, owner,
                            ImportCommandCount: owner is null ? 1 : 0, TotalCommandCount: 1);
                    })
                    .ToArray();
                return new AddonPreviewItem(
                    "Extension",
                    id,
                    installed,
                    installed ? "Installed extension" : null,
                    commands,
                    Version: extension.Version,
                    ImportCommandCount: installed ? 0 : commands.Count(command => !command.AlreadyExists),
                    TotalCommandCount: commands.Length);
            })
            .ToArray();

        var conflicts = packIds.Where(id => File.Exists(_packLoader.GetInstalledPackPath(id)))
            .Select(id => $"Command pack: {id}")
            .Concat(snippetNames.Where(name => _snippetStore.Find(name) is not null)
                .Select(name => $"Snippet: {name}"))
            .Concat(extensionIds.Where(_extensionPackageManager.IsUserExtensionInstalled)
                .Select(id => $"Extension: {id}"))
            .ToArray();
        return new AddonImportPreview(manifest, packIds, snippetNames, extensionIds, conflicts)
        {
            Items = packItems.Concat(snippetItems).Concat(extensionItems).ToArray()
        };
    }

    public void Install(
        string archivePath,
        AddonImportPreview preview,
        bool replaceExisting,
        Action? runtimeRefresh = null)
    {
        if (preview.Conflicts.Count > 0 && !replaceExisting)
        {
            throw new InvalidOperationException("Replacing existing add-on content requires confirmation.");
        }

        Install(archivePath, preview, ImportConflictPolicy.OverwriteExisting, runtimeRefresh);
    }

    public AddonInstallResult Install(
        string archivePath,
        AddonImportPreview preview,
        ImportConflictPolicy conflictPolicy,
        Action? runtimeRefresh = null)
    {
        var previousPacks = new Dictionary<string, (string Path, byte[] Content)>(StringComparer.OrdinalIgnoreCase);
        var previousSnippets = new Dictionary<string, SnippetDefinition?>(StringComparer.OrdinalIgnoreCase);
        var installedPackIds = new List<string>();
        var installedSnippetNames = new List<string>();
        var skippedItems = new List<string>();
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = GetFileEntries(archive);
        var currentPreview = Preview(archivePath);
        if (!string.Equals(
                JsonSerializer.Serialize(preview.Manifest, JsonOptions),
                JsonSerializer.Serialize(currentPreview.Manifest, JsonOptions),
                StringComparison.Ordinal)
            || !preview.PackIds.SequenceEqual(currentPreview.PackIds, StringComparer.OrdinalIgnoreCase)
            || !preview.SnippetNames.SequenceEqual(currentPreview.SnippetNames, StringComparer.OrdinalIgnoreCase)
            || !preview.ExtensionIds.SequenceEqual(currentPreview.ExtensionIds, StringComparer.OrdinalIgnoreCase)
            || !preview.Conflicts.SequenceEqual(currentPreview.Conflicts, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The add-on contents or installed conflicts changed after preview. Preview it again before importing.");
        }

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"launchpad-addon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            foreach (var packPath in preview.Manifest.Packs)
            {
                var normalized = NormalizePackagePath(packPath);
                var packId = Path.GetFileNameWithoutExtension(normalized);
                var destinationPath = _packLoader.GetInstalledPackPath(packId);
                if (File.Exists(destinationPath) && conflictPolicy == ImportConflictPolicy.SkipExisting)
                {
                    skippedItems.Add($"Command pack: {packId}");
                    continue;
                }

                if (File.Exists(destinationPath))
                {
                    previousPacks[packId] = (Path.GetRelativePath(
                        Directory.GetParent(_packLoader.ProfilePackDirectory)!.FullName,
                        destinationPath).Replace('\\', '/'), File.ReadAllBytes(destinationPath));
                }

                var stagedPath = Path.Combine(stagingDirectory, $"{packId}.json");
                File.WriteAllBytes(stagedPath, ReadBytes(entries[normalized]));
                installedPackIds.Add(packId);
                _packLoader.InstallPackAtProfilePath(stagedPath, normalized, replaceExisting: File.Exists(destinationPath));
            }

            foreach (var snippetName in preview.Manifest.Snippets)
            {
                if (_snippetStore.Find(snippetName) is not null && conflictPolicy == ImportConflictPolicy.SkipExisting)
                {
                    skippedItems.Add($"Snippet: {snippetName}");
                    continue;
                }

                var metadataPath = $"snippets/{snippetName}/metadata.json";
                var metadata = JsonSerializer.Deserialize<SnippetMetadata>(ReadText(entries[metadataPath]), JsonOptions)
                    ?? throw new InvalidDataException($"Snippet metadata for '{snippetName}' is invalid.");
                var scriptFileName = string.Equals(metadata.Type, "powershell", StringComparison.OrdinalIgnoreCase)
                    ? "script.ps1"
                    : "script.sh";
                var script = ReadText(entries[$"snippets/{snippetName}/{scriptFileName}"]);
                previousSnippets[snippetName] = _snippetStore.Find(snippetName);
                installedSnippetNames.Add(snippetName);
                _snippetStore.Save(metadata.Name, metadata.Type, metadata.Arguments, metadata.AllowedSecrets, script);
            }

            var extensionPackages = new List<string>();
            foreach (var extensionPath in preview.Manifest.Extensions ?? [])
            {
                var normalized = NormalizePackagePath(extensionPath);
                var segments = normalized.Split('/');
                var extensionId = normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileNameWithoutExtension(normalized)
                    : segments[1];
                if (_extensionPackageManager.IsUserExtensionInstalled(extensionId)
                    && conflictPolicy == ImportConflictPolicy.SkipExisting)
                {
                    skippedItems.Add($"Extension: {extensionId}");
                    continue;
                }

                string packagePath;
                if (normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    packagePath = Path.Combine(stagingDirectory, Path.GetFileName(normalized));
                    File.WriteAllBytes(packagePath, ReadBytes(entries[normalized]));
                }
                else
                {
                    if (segments.Length != 3 || !string.Equals(segments[2], "extension.json", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Add-on extension path '{normalized}' is invalid.");
                    }

                    var packageExtensionId = segments[1];
                    packagePath = Path.Combine(stagingDirectory, $"{packageExtensionId}.zip");
                    var prefix = $"extensions/{packageExtensionId}/";
                    CreateExtensionArchive(entries.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)), prefix, packagePath);
                }

                extensionPackages.Add(packagePath);
            }

            if (extensionPackages.Count > 0)
            {
                _extensionPackageManager.InstallPackages(extensionPackages, _ => runtimeRefresh?.Invoke());
            }
            else
            {
                runtimeRefresh?.Invoke();
            }

            return new AddonInstallResult(skippedItems);
        }
        catch (Exception installException)
        {
            var rollbackFailures = new List<string>();
            foreach (var snippetName in installedSnippetNames.AsEnumerable().Reverse())
            {
                try
                {
                    var previous = previousSnippets[snippetName];
                    if (previous is null)
                    {
                        _snippetStore.Remove(snippetName);
                    }
                    else
                    {
                        _snippetStore.Save(previous.Metadata.Name, previous.Metadata.Type, previous.Metadata.Arguments, previous.Metadata.AllowedSecrets, previous.Script);
                    }
                }
                catch (Exception exception)
                {
                    rollbackFailures.Add($"Snippet '{snippetName}': {exception.Message}");
                }
            }

            foreach (var packId in installedPackIds.AsEnumerable().Reverse())
            {
                try
                {
                    var destinationPath = _packLoader.GetInstalledPackPath(packId);
                    if (previousPacks.TryGetValue(packId, out var originalBytes))
                    {
                        _packLoader.InstallPackAtProfilePath(
                            WriteTemporaryPack(stagingDirectory, packId, originalBytes.Content),
                            originalBytes.Path,
                            replaceExisting: true);
                    }
                    else
                    {
                        _packLoader.DeleteInstalledPack(packId);
                    }
                }
                catch (Exception exception)
                {
                    rollbackFailures.Add($"Pack '{packId}': {exception.Message}");
                }
            }

            if (installedPackIds.Count > 0 || installedSnippetNames.Count > 0 || preview.ExtensionIds.Count > 0)
            {
                try
                {
                    runtimeRefresh?.Invoke();
                }
                catch (Exception exception)
                {
                    rollbackFailures.Add($"Runtime refresh: {exception.Message}");
                }
            }

            var rollbackMessage = rollbackFailures.Count == 0
                ? string.Empty
                : $" Rollback issues: {string.Join("; ", rollbackFailures)}";
            throw new InvalidOperationException($"Add-on installation failed: {installException.Message}{rollbackMessage}", installException);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static Dictionary<string, ZipArchiveEntry> GetFileEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var path = NormalizePackagePath(entry.FullName);
            if (!entries.TryAdd(path, entry))
            {
                throw new InvalidDataException($"Add-on archive contains duplicate path '{path}'.");
            }
        }

        return entries;
    }

    private static string NormalizePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"Add-on archive path '{path}' is invalid.");
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(segment => segment is "" or "." or "..")
            || normalized.Contains(':'))
        {
            throw new InvalidDataException($"Add-on archive path '{path}' is invalid.");
        }

        return normalized;
    }

    private static void ValidateManifest(AddonManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)
            || !manifest.Id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            || string.IsNullOrWhiteSpace(manifest.Name)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || manifest.Packs is null
            || manifest.Snippets is null
            || (manifest.Extensions ?? []).Count == 0 && manifest.Packs.Count == 0 && manifest.Snippets.Count == 0)
        {
            throw new InvalidDataException("Add-on manifest must define an ID, name, version, and at least one pack, snippet, or extension.");
        }

        if (manifest.Extensions?.Any(path => !path.StartsWith("extensions/", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new InvalidDataException("Add-on extension paths must be relative to the profile extensions directory.");
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
