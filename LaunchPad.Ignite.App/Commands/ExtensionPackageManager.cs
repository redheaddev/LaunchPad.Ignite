using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    private readonly string _packageDirectory;

    public static string DefaultPackageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaunchPad.Ignite",
        "packages");

    public ExtensionPackageManager()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "extensions"), DefaultPackageDirectory)
    {
    }

    public ExtensionPackageManager(string extensionRoot)
        : this(extensionRoot, DefaultPackageDirectory)
    {
    }

    public ExtensionPackageManager(string extensionRoot, string packageDirectory)
    {
        _extensionRoot = Path.GetFullPath(extensionRoot);
        _packageDirectory = Path.GetFullPath(packageDirectory);
    }

    public string Install(string packagePath, Action<string>? runtimeRefresh = null)
        => InstallPackages([packagePath], ids => runtimeRefresh?.Invoke(ids[0]))[0];

    public IReadOnlyList<string> InstallPackages(
        IEnumerable<string> packageReferences,
        Action<IReadOnlyList<string>>? runtimeRefresh = null)
    {
        var resolvedPackagePaths = packageReferences
            .Select(ResolvePackagePath)
            .ToArray();
        if (resolvedPackagePaths.Length == 0)
        {
            throw new ArgumentException("At least one extension package is required.", nameof(packageReferences));
        }

        foreach (var packagePath in resolvedPackagePaths)
        {
            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException("Extension package was not found.", packagePath);
            }
        }

        var transactionRoot = Path.Combine(Path.GetDirectoryName(_extensionRoot)!, ".extension-transactions");
        Directory.CreateDirectory(transactionRoot);
        var transactionDirectory = Path.Combine(transactionRoot, Guid.NewGuid().ToString("N"));
        var stagedRoot = Path.Combine(transactionDirectory, "staged");
        var backupRoot = Path.Combine(transactionDirectory, "previous");
        var stagedDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installedDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var backupDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var extensionIds = new List<string>();

        try
        {
            Directory.CreateDirectory(stagedRoot);
            for (var index = 0; index < resolvedPackagePaths.Length; index++)
            {
                var stagedDirectory = Path.Combine(stagedRoot, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(stagedDirectory);
                ExtractPackage(resolvedPackagePaths[index], stagedDirectory);
                var manifestPath = Path.Combine(stagedDirectory, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    throw new InvalidDataException($"Extension package must contain '{ManifestFileName}' at its root.");
                }

                var manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(manifestPath), SerializerOptions)
                    ?? throw new InvalidDataException($"Extension package manifest '{manifestPath}' is empty.");
                ValidateManifestId(manifest.Id);
                if (!stagedDirectories.TryAdd(manifest.Id, stagedDirectory))
                {
                    throw new InvalidDataException($"Multiple selected packages have the extension ID '{manifest.Id}'.");
                }

                extensionIds.Add(manifest.Id);
            }

            _ = new ExtensionLoader([stagedRoot]).Load();

            Directory.CreateDirectory(_extensionRoot);
            foreach (var extensionId in extensionIds)
            {
                var installedDirectory = Path.Combine(_extensionRoot, extensionId);
                installedDirectories.Add(extensionId, installedDirectory);
                if (Directory.Exists(installedDirectory))
                {
                    if ((File.GetAttributes(installedDirectory) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException($"Extension install directory '{installedDirectory}' cannot be a link.");
                    }

                    Directory.CreateDirectory(backupRoot);
                    var backupDirectory = Path.Combine(backupRoot, extensionId);
                    Directory.Move(installedDirectory, backupDirectory);
                    backupDirectories.Add(extensionId, backupDirectory);
                }

                Directory.Move(stagedDirectories[extensionId], installedDirectory);
            }

            runtimeRefresh?.Invoke(extensionIds);
            return extensionIds;
        }
        catch
        {
            foreach (var extensionId in extensionIds.AsEnumerable().Reverse())
            {
                if (installedDirectories.TryGetValue(extensionId, out var installedDirectory)
                    && Directory.Exists(installedDirectory))
                {
                    Directory.Delete(installedDirectory, recursive: true);
                }

                if (backupDirectories.TryGetValue(extensionId, out var backupDirectory)
                    && Directory.Exists(backupDirectory)
                    && installedDirectory is not null
                    && !Directory.Exists(installedDirectory))
                {
                    Directory.Move(backupDirectory, installedDirectory);
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

    public IReadOnlyList<string> GetAvailablePackageNames()
    {
        if (!Directory.Exists(_packageDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_packageDirectory, "*.zip")
            .Select(packagePath => Path.GetFileNameWithoutExtension(packagePath)!)
            .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private string ResolvePackagePath(string packageReference)
    {
        if (string.IsNullOrWhiteSpace(packageReference) || File.Exists(packageReference))
        {
            return packageReference;
        }

        if (Path.IsPathRooted(packageReference)
            || packageReference.Contains(Path.DirectorySeparatorChar)
            || packageReference.Contains(Path.AltDirectorySeparatorChar))
        {
            return packageReference;
        }

        var packageId = packageReference.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(packageReference)
            : packageReference;

        var versionedPackageName = Regex.Match(packageId, "^(?<id>.+)-(?<version>\\d+\\.\\d+\\.\\d+)$");
        if (versionedPackageName.Success)
        {
            var versionedPackageId = versionedPackageName.Groups["id"].Value;
            ValidateManifestId(versionedPackageId);
            return Path.Combine(_packageDirectory, $"{packageId}.zip");
        }

        ValidateManifestId(packageId);
        var latestVersionedPackage = FindLatestVersionedPackage(packageId);
        if (latestVersionedPackage is not null)
        {
            return latestVersionedPackage;
        }

        return Path.Combine(_packageDirectory, $"{packageId}.zip");
    }

    private string? FindLatestVersionedPackage(string packageId)
    {
        if (!Directory.Exists(_packageDirectory))
        {
            return null;
        }

        var prefix = $"{packageId}-";
        string? latestPackagePath = null;
        Version? latestVersion = null;
        foreach (var packagePath in Directory.EnumerateFiles(_packageDirectory, "*.zip"))
        {
            var fileName = Path.GetFileName(packagePath);
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionText = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - ".zip".Length);
            if (!Regex.IsMatch(versionText, "^\\d+\\.\\d+\\.\\d+$")
                || !Version.TryParse(versionText, out var version))
            {
                continue;
            }

            if (latestVersion is null || version.CompareTo(latestVersion) > 0)
            {
                latestVersion = version;
                latestPackagePath = packagePath;
            }
        }

        return latestPackagePath;
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
