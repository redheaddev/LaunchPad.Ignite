using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    IReadOnlyList<string> Conflicts);

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

    public AddonPackageService(
        CommandPackLoader packLoader,
        SnippetStore snippetStore,
        ExtensionPackageManager? extensionPackageManager = null)
    {
        _packLoader = packLoader;
        _snippetStore = snippetStore;
        _extensionPackageManager = extensionPackageManager ?? new ExtensionPackageManager();
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
        foreach (var extensionPath in manifest.Extensions ?? [])
        {
            var entryPath = NormalizePackagePath(extensionPath);
            if (!entryPath.StartsWith("extensions/", StringComparison.OrdinalIgnoreCase)
                || !entryPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || !entries.TryGetValue(entryPath, out var extensionEntry))
            {
                throw new InvalidDataException($"Add-on extension package '{extensionPath}' is missing or is outside the extensions directory.");
            }

            var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
            try
            {
                File.WriteAllBytes(temporaryPath, ReadBytes(extensionEntry));
                var extensionId = _extensionPackageManager.ValidatePackage(temporaryPath);
                if (!string.Equals(Path.GetFileNameWithoutExtension(entryPath), extensionId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Extension entry '{entryPath}' must match its manifest ID '{extensionId}'.");
                }

                extensionIds.Add(extensionId);
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

        var conflicts = packIds.Where(id => File.Exists(_packLoader.GetInstalledPackPath(id)))
            .Select(id => $"Command pack: {id}")
            .Concat(snippetNames.Where(name => _snippetStore.Find(name) is not null)
                .Select(name => $"Snippet: {name}"))
            .Concat(extensionIds.Where(_extensionPackageManager.IsUserExtensionInstalled)
                .Select(id => $"Extension: {id}"))
            .ToArray();
        return new AddonImportPreview(manifest, packIds, snippetNames, extensionIds, conflicts);
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

        var previousPacks = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var previousSnippets = new Dictionary<string, SnippetDefinition?>(StringComparer.OrdinalIgnoreCase);
        var installedPackIds = new List<string>();
        var installedSnippetNames = new List<string>();
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
                if (File.Exists(destinationPath))
                {
                    previousPacks[packId] = File.ReadAllBytes(destinationPath);
                }

                var stagedPath = Path.Combine(stagingDirectory, $"{packId}.json");
                File.WriteAllBytes(stagedPath, ReadBytes(entries[normalized]));
                installedPackIds.Add(packId);
                _packLoader.InstallPack(stagedPath, replaceExisting: File.Exists(destinationPath));
            }

            foreach (var snippetName in preview.Manifest.Snippets)
            {
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
                var packagePath = Path.Combine(stagingDirectory, Path.GetFileName(normalized));
                File.WriteAllBytes(packagePath, ReadBytes(entries[normalized]));
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
                        File.WriteAllBytes(destinationPath, originalBytes);
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
