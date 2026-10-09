using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LaunchPad.Ignite.App.Commands;

public sealed class ExtensionPackageManager
{
    private const string ManifestFileName = "extension.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _extensionRoot;

    public ExtensionPackageManager()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "extensions"))
    {
    }

    public ExtensionPackageManager(string extensionRoot)
    {
        _extensionRoot = Path.GetFullPath(extensionRoot);
    }

    public string Install(string packagePath, Action<string>? runtimeRefresh = null)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException("Extension package was not found.", packagePath);
        }

        var transactionRoot = Path.Combine(Path.GetDirectoryName(_extensionRoot)!, ".extension-transactions");
        Directory.CreateDirectory(transactionRoot);
        var transactionDirectory = Path.Combine(transactionRoot, Guid.NewGuid().ToString("N"));
        var stagedExtensionDirectory = Path.Combine(transactionDirectory, "package");
        Directory.CreateDirectory(stagedExtensionDirectory);

        string? backupDirectory = null;
        string? installedDirectory = null;
        try
        {
            ExtractPackage(packagePath, stagedExtensionDirectory);
            var manifestPath = Path.Combine(stagedExtensionDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException($"Extension package must contain '{ManifestFileName}' at its root.");
            }

            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), SerializerOptions)
                ?? throw new InvalidDataException("Extension package manifest is empty.");
            ValidateManifestId(manifest.Id);
            _ = new ExtensionLoader([transactionDirectory]).Load();

            Directory.CreateDirectory(_extensionRoot);
            installedDirectory = Path.Combine(_extensionRoot, manifest.Id);
            if (Directory.Exists(installedDirectory)
                && (File.GetAttributes(installedDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Extension install directory '{installedDirectory}' cannot be a link.");
            }

            if (Directory.Exists(installedDirectory))
            {
                backupDirectory = Path.Combine(transactionDirectory, "previous");
                Directory.Move(installedDirectory, backupDirectory);
            }

            Directory.Move(stagedExtensionDirectory, installedDirectory);
            runtimeRefresh?.Invoke(manifest.Id);
            return manifest.Id;
        }
        catch
        {
            if (installedDirectory is not null && Directory.Exists(installedDirectory))
            {
                Directory.Delete(installedDirectory, recursive: true);
            }

            if (backupDirectory is not null && Directory.Exists(backupDirectory) && installedDirectory is not null)
            {
                Directory.Move(backupDirectory, installedDirectory);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(transactionDirectory))
            {
                Directory.Delete(transactionDirectory, recursive: true);
            }
        }
    }

    public void Uninstall(string extensionId, Action<string>? runtimeRefresh = null)
        => Uninstall([extensionId], ids => runtimeRefresh?.Invoke(ids[0]));

    public void Uninstall(IEnumerable<string> extensionIds, Action<IReadOnlyList<string>>? runtimeRefresh = null)
    {
        var ids = extensionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0)
        {
            throw new ArgumentException("At least one extension ID is required.", nameof(extensionIds));
        }

        var installedDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            ValidateManifestId(id);
            var installedDirectory = FindUserExtensionDirectory(id);
            if (installedDirectory is null)
            {
                if (IsBundledExtension(id))
                {
                    throw new InvalidOperationException(
                        $"Extension '{id}' is bundled with the application and cannot be uninstalled.");
                }

                throw new DirectoryNotFoundException($"User-installed extension '{id}' was not found.");
            }

            if ((File.GetAttributes(installedDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Extension install directory '{installedDirectory}' cannot be a link.");
            }

            var manifestPath = Path.Combine(installedDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidDataException($"User-installed extension '{id}' has no manifest.");
            }

            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), SerializerOptions)
                ?? throw new InvalidDataException($"User-installed extension '{id}' has an empty manifest.");
            if (!string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Extension directory '{id}' does not match its manifest ID '{manifest.Id}'.");
            }

            installedDirectories.Add(id, installedDirectory);
        }

        var backupNames = installedDirectories.ToDictionary(
            entry => entry.Key,
            entry => Path.GetFileName(entry.Value),
            StringComparer.OrdinalIgnoreCase);

        var transactionRoot = Path.Combine(Path.GetDirectoryName(_extensionRoot)!, ".extension-transactions");
        Directory.CreateDirectory(transactionRoot);
        var transactionDirectory = Path.Combine(transactionRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transactionDirectory);
        var backupDirectory = Path.Combine(transactionDirectory, "previous");
        Directory.CreateDirectory(backupDirectory);
        try
        {
            foreach (var id in ids)
            {
                Directory.Move(installedDirectories[id], Path.Combine(backupDirectory, backupNames[id]));
            }

            runtimeRefresh?.Invoke(ids);
        }
        catch
        {
            foreach (var id in ids)
            {
                var backupPath = Path.Combine(backupDirectory, backupNames[id]);
                var installedPath = installedDirectories[id];
                if (Directory.Exists(backupPath) && !Directory.Exists(installedPath))
                {
                    Directory.Move(backupPath, installedPath);
                }
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(transactionDirectory))
            {
                Directory.Delete(transactionDirectory, recursive: true);
            }
        }
    }

    private string? FindUserExtensionDirectory(string extensionId)
    {
        if (!Directory.Exists(_extensionRoot))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(_extensionRoot))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), SerializerOptions);
            if (manifest is not null && string.Equals(manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return null;
    }

    private static bool IsBundledExtension(string extensionId)
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "extensions");
        if (!Directory.Exists(bundledRoot))
        {
            return false;
        }

        foreach (var directory in Directory.EnumerateDirectories(bundledRoot))
        {
            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), SerializerOptions);
            if (manifest is not null && string.Equals(manifest.Id, extensionId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void ExtractPackage(string packagePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var entryPath = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(entryPath)
                || entryPath.StartsWith("/", StringComparison.Ordinal)
                || entryPath.Contains(':')
                || entryPath.Split('/').Any(segment => segment is ".." or "."))
            {
                throw new InvalidDataException($"Extension package contains an invalid path '{entry.FullName}'.");
            }

            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
            {
                throw new InvalidDataException($"Extension package contains a link '{entry.FullName}'.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Extension package path '{entry.FullName}' escapes its installation directory.");
            }

            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var uniquePath = isDirectory ? targetPath.TrimEnd(Path.DirectorySeparatorChar) : targetPath;
            if (!extractedPaths.Add(uniquePath))
            {
                throw new InvalidDataException($"Extension package contains duplicate path '{entry.FullName}'.");
            }

            if (isDirectory)
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var input = entry.Open();
            using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void ValidateManifestId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id[0] is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            || id.Any(character => !(character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.'
                or '_'
                or '-')))
        {
            throw new InvalidDataException("Extension package manifest must define an id containing only letters, numbers, '.', '_' or '-'.");
        }
    }
}
