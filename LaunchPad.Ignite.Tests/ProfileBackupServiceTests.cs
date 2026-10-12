using System.IO;
using System.IO.Compression;
using System.Linq;
using LaunchPad.Ignite.App.Services;
using Xunit;

namespace LaunchPad.Ignite.Tests;

public sealed class ProfileBackupServiceTests
{
    [Fact]
    public void CreatesVersionedBackupWithoutIncludingBackupDirectory()
    {
        using var directory = new TemporaryDirectory();
        var profileDirectory = Path.Combine(directory.Path, "profile");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(Path.Combine(profileDirectory, "settings.json"), "profile-settings");
        Directory.CreateDirectory(Path.Combine(profileDirectory, "backups"));
        File.WriteAllText(Path.Combine(profileDirectory, "backups", "old.zip"), "old-backup");
        var service = new ProfileBackupService(profileDirectory, "8.2.1");

        var backupPath = service.CreateBackup("my profile");

        Assert.Matches(@"^backup-8\.2\.1-\d{8}-\d{9}-my profile\.zip$", Path.GetFileName(backupPath));
        using var archive = ZipFile.OpenRead(backupPath);
        Assert.Equal(["settings.json"], archive.Entries.Select(entry => entry.FullName));
        Assert.Equal("profile-settings", ReadEntry(archive, "settings.json"));
    }

    [Fact]
    public void RestoreCreatesSafetyBackupBeforeOverwritingFiles()
    {
        using var directory = new TemporaryDirectory();
        var profileDirectory = Path.Combine(directory.Path, "profile");
        var backupDirectory = Path.Combine(profileDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);
        File.WriteAllText(Path.Combine(profileDirectory, "settings.json"), "current-settings");
        var service = new ProfileBackupService(profileDirectory, "8.2.1");
        var sourcePath = Path.Combine(directory.Path, "source.zip");
        using (var archive = ZipFile.Open(sourcePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("settings.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("restored-settings");
        }

        File.Copy(sourcePath, Path.Combine(backupDirectory, "restore.zip"));

        var safetyBackupPath = service.RestoreBackup(Path.Combine(backupDirectory, "restore.zip"));

        Assert.Equal("restored-settings", File.ReadAllText(Path.Combine(profileDirectory, "settings.json")));
        using var safetyBackup = ZipFile.OpenRead(safetyBackupPath);
        Assert.Equal("current-settings", ReadEntry(safetyBackup, "settings.json"));
        Assert.DoesNotContain(safetyBackup.Entries, entry => entry.FullName.StartsWith("backups/", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsBackupEntriesThatEscapeProfileDirectory()
    {
        using var directory = new TemporaryDirectory();
        var profileDirectory = Path.Combine(directory.Path, "profile");
        var backupDirectory = Path.Combine(profileDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);
        File.WriteAllText(Path.Combine(profileDirectory, "settings.json"), "unchanged");
        var service = new ProfileBackupService(profileDirectory, "8.2.1");
        var maliciousPath = Path.Combine(backupDirectory, "malicious.zip");
        using (var archive = ZipFile.Open(maliciousPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("../outside.txt");
            var entry = archive.CreateEntry("settings.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("overwritten");
        }

        Assert.Throws<InvalidDataException>(() => service.RestoreBackup(maliciousPath));
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(profileDirectory, "settings.json")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
    }

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return reader.ReadToEnd();
    }

    private sealed class TemporaryDirectory : System.IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
