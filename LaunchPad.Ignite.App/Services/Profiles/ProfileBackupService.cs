using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using LaunchPad.Ignite.App.Services.Secrets;

namespace LaunchPad.Ignite.App.Services.Profiles;

public sealed class ProfileBackupService
{
    private const string SecretBackupEntryName = "secrets.lpsecrets";
    private const int MaximumEncryptedSecretsFileSize = 10 * 1024 * 1024;
    private readonly string _profileDirectory;
    private readonly string _version;
    private readonly EncryptedSecretFileService _encryptedSecretFileService = new();

    public ProfileBackupService(string? profileDirectory = null, string? version = null)
    {
        _profileDirectory = profileDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite");
        _version = version ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public string ProfileDirectory => _profileDirectory;

    public string BackupDirectory => Path.Combine(_profileDirectory, "backups");

    public string CreateBackup(
        string? name = null,
        IReadOnlyDictionary<string, string>? secrets = null,
        string? secretsPassword = null)
    {
        Directory.CreateDirectory(_profileDirectory);
        Directory.CreateDirectory(BackupDirectory);

        var safeName = SanitizeName(name);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff");
        var fileName = $"backup-{_version}-{timestamp[..8]}-{timestamp[9..]}{(safeName.Length == 0 ? string.Empty : $"-{safeName}")}.zip";
        var archivePath = Path.Combine(BackupDirectory, fileName);
        byte[]? encryptedSecrets = null;
        if (secrets is not null)
        {
            encryptedSecrets = _encryptedSecretFileService.Encrypt(
                secrets,
                secretsPassword ?? throw new ArgumentNullException(nameof(secretsPassword)));
        }
        else if (secretsPassword is not null)
        {
            throw new ArgumentException("Secrets must be provided when a secrets password is specified.", nameof(secrets));
        }

        try
        {
            using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            foreach (var filePath in Directory.EnumerateFiles(_profileDirectory, "*", SearchOption.AllDirectories)
                         .Where(path => !IsInBackupDirectory(path) && !IsInstanceLockFile(path)))
            {
                var relativePath = Path.GetRelativePath(_profileDirectory, filePath);
                if (string.Equals(relativePath, SecretBackupEntryName, PathComparison))
                {
                    throw new InvalidDataException($"The profile uses the reserved '{SecretBackupEntryName}' file.");
                }

                archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
            }

            if (encryptedSecrets is not null)
            {
                var entry = archive.CreateEntry(SecretBackupEntryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(encryptedSecrets);
            }

            return archivePath;
        }
        finally
        {
            if (encryptedSecrets is not null)
            {
                CryptographicOperations.ZeroMemory(encryptedSecrets);
            }
        }
    }

    public bool BackupContainsSecrets(string backupFile)
    {
        var fullBackupPath = ValidateBackupPath(backupFile);
        using var archive = ZipFile.OpenRead(fullBackupPath);
        return GetEncryptedSecretsEntry(archive) is not null;
    }

    public IReadOnlyDictionary<string, string> ReadSecrets(string backupFile, string password)
    {
        var fullBackupPath = ValidateBackupPath(backupFile);
        using var archive = ZipFile.OpenRead(fullBackupPath);
        var entry = GetEncryptedSecretsEntry(archive)
            ?? throw new InvalidDataException("The selected backup does not contain encrypted secrets.");
        if (entry.Length == 0 || entry.Length > MaximumEncryptedSecretsFileSize)
        {
            throw new InvalidDataException("The encrypted secrets entry is empty or too large.");
        }

        using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream((int)entry.Length);
        entryStream.CopyTo(memoryStream);
        if (memoryStream.Length > MaximumEncryptedSecretsFileSize)
        {
            throw new InvalidDataException("The encrypted secrets entry is too large.");
        }

        var encryptedSecrets = memoryStream.ToArray();
        try
        {
            return _encryptedSecretFileService.Decrypt(encryptedSecrets, password);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedSecrets);
        }
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
        => RestoreBackup(backupFile, createSafetyBackup: true)!;

    public string? RestoreBackup(string backupFile, bool createSafetyBackup)
    {
        var fullBackupPath = ValidateBackupPath(backupFile);

        using var archive = ZipFile.OpenRead(fullBackupPath);
        var entries = archive.Entries.Select(entry => (Entry: entry, Destination: GetSafeDestination(entry))).ToArray();
        var automaticBackupPath = createSafetyBackup
            ? CreateBackup($"before-restore-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}")
            : null;

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

    private string ValidateBackupPath(string backupFile)
    {
        var fullBackupPath = Path.GetFullPath(backupFile);
        var fullBackupDirectory = Path.GetFullPath(BackupDirectory);
        if (!string.Equals(Path.GetDirectoryName(fullBackupPath), fullBackupDirectory, PathComparison)
            || !File.Exists(fullBackupPath))
        {
            throw new InvalidDataException("The selected backup file is not in the profile backups directory.");
        }

        return fullBackupPath;
    }

    private static ZipArchiveEntry? GetEncryptedSecretsEntry(ZipArchive archive)
    {
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.FullName.Replace('\\', '/'), SecretBackupEntryName, StringComparison.Ordinal))
            .ToArray();
        if (entries.Length > 1)
        {
            throw new InvalidDataException("The backup contains multiple encrypted secrets entries.");
        }

        return entries.SingleOrDefault();
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

        if (string.Equals(normalizedEntryName, "instance.lock", PathComparison))
        {
            return null;
        }

        if (string.Equals(normalizedEntryName, SecretBackupEntryName, PathComparison))
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

    private bool IsInstanceLockFile(string filePath)
        => string.Equals(Path.GetRelativePath(_profileDirectory, filePath), "instance.lock", PathComparison);

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
