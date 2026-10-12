using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace LaunchPad.Ignite.App.Services;

public sealed class ProfileBackupService
{
    private readonly string _profileDirectory;
    private readonly string _version;

    public ProfileBackupService(string? profileDirectory = null, string? version = null)
    {
        _profileDirectory = profileDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite");
        _version = version ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public string ProfileDirectory => _profileDirectory;

    public string BackupDirectory => Path.Combine(_profileDirectory, "backups");

    public string CreateBackup(string? name = null)
    {
        Directory.CreateDirectory(_profileDirectory);
        Directory.CreateDirectory(BackupDirectory);

        var safeName = SanitizeName(name);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff");
        var fileName = $"backup-{_version}-{timestamp[..8]}-{timestamp[9..]}{(safeName.Length == 0 ? string.Empty : $"-{safeName}")}.zip";
        var archivePath = Path.Combine(BackupDirectory, fileName);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(_profileDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !IsInBackupDirectory(path)))
        {
            var relativePath = Path.GetRelativePath(_profileDirectory, filePath);
            archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
        }

        return archivePath;
    }

    public IReadOnlyList<string> GetBackupFiles()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(BackupDirectory, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    public string RestoreBackup(string backupFile)
    {
        var fullBackupPath = Path.GetFullPath(backupFile);
        var fullBackupDirectory = Path.GetFullPath(BackupDirectory);
        if (!string.Equals(Path.GetDirectoryName(fullBackupPath), fullBackupDirectory, PathComparison)
            || !File.Exists(fullBackupPath))
        {
            throw new InvalidDataException("The selected backup file is not in the profile backups directory.");
        }

        using var archive = ZipFile.OpenRead(fullBackupPath);
        var entries = archive.Entries.Select(entry => (Entry: entry, Destination: GetSafeDestination(entry))).ToArray();
        var automaticBackupPath = CreateBackup($"before-restore-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");

        foreach (var (entry, destination) in entries)
        {
            if (destination is null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        return automaticBackupPath;
    }

    private string? GetSafeDestination(ZipArchiveEntry entry)
    {
        var normalizedEntryName = entry.FullName.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedEntryName)
            || normalizedEntryName.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new InvalidDataException($"Backup entry '{entry.FullName}' has an invalid path.");
        }

        if (normalizedEntryName.Length == 0)
        {
            return null;
        }

        var relativePath = normalizedEntryName.Replace('/', Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(_profileDirectory, relativePath));
        var profilePrefix = Path.GetFullPath(_profileDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(profilePrefix, PathComparison))
        {
            throw new InvalidDataException($"Backup entry '{entry.FullName}' escapes the profile directory.");
        }

        var relativeToProfile = Path.GetRelativePath(_profileDirectory, destination);
        if (string.Equals(relativeToProfile.Split(Path.DirectorySeparatorChar)[0], "backups", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\') ? null : destination;
    }

    private bool IsInBackupDirectory(string filePath)
    {
        var relativePath = Path.GetRelativePath(_profileDirectory, filePath);
        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
        return string.Equals(firstSegment, "backups", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name.Trim().Select(character =>
            invalidCharacters.Contains(character) || character is '/' or '\\' ? '-' : character).ToArray());
        return sanitized.Trim(' ', '.', '-');
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
