using System.IO.Compression;
using System.Text.Json;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class AddonPackageServiceTests
{
    [Fact]
    public void PreviewValidatesBundleAndReportsExistingContentConflicts()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore);
        Directory.CreateDirectory(packLoader.GetInstalledPackPath("sample-pack") is { } packPath
            ? Path.GetDirectoryName(packPath)!
            : throw new InvalidOperationException());
        packLoader.WriteInstalledManifest(CreatePack("sample-pack", "existing"));
        snippetStore.Save("sample", "powershell", [], [], "Write-Output 'existing'");
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample");

        var preview = service.Preview(archivePath);

        Assert.Equal(["sample-pack"], preview.PackIds);
        Assert.Equal(["sample"], preview.SnippetNames);
        Assert.Equal(2, preview.Conflicts.Count);
    }

    [Fact]
    public void PreviewValidatesExtensionPackageAndReportsExistingExtensionConflict()
    {
        using var directory = new TemporaryDirectory();
        var extensionRoot = Path.Combine(directory.Path, "profile", "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot, Path.Combine(directory.Path, "packages"));
        var service = CreateService(directory.Path, out _, out _, packageManager);
        packageManager.Install(CreateExtensionPackage(directory.Path, "sample-extension"));
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample", "sample-extension");

        var preview = service.Preview(archivePath);

        Assert.Equal(["sample-extension"], preview.ExtensionIds);
        Assert.Contains("Extension: sample-extension", preview.Conflicts);
    }

    [Fact]
    public void InstallInstallsExtensionPackage()
    {
        using var directory = new TemporaryDirectory();
        var extensionRoot = Path.Combine(directory.Path, "profile", "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot, Path.Combine(directory.Path, "packages"));
        var service = CreateService(directory.Path, out _, out _, packageManager);
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample", "sample-extension");
        var preview = service.Preview(archivePath);

        service.Install(archivePath, preview, replaceExisting: false);

        Assert.True(File.Exists(Path.Combine(extensionRoot, "sample-extension", "extension.json")));
        Assert.Equal("sample-extension", Assert.Single(new ExtensionLoader([extensionRoot]).Load().Extensions).Id);
    }

    [Fact]
    public void InstallRollsBackPackAndExtensionWhenRuntimeRefreshFails()
    {
        using var directory = new TemporaryDirectory();
        var extensionRoot = Path.Combine(directory.Path, "profile", "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot, Path.Combine(directory.Path, "packages"));
        var service = CreateService(directory.Path, out var packLoader, out _, packageManager);
        Directory.CreateDirectory(Path.GetDirectoryName(packLoader.GetInstalledPackPath("sample-pack"))!);
        packLoader.WriteInstalledManifest(CreatePack("sample-pack", "old"));
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample", "sample-extension");
        var preview = service.Preview(archivePath);

        Assert.Throws<InvalidOperationException>(() => service.Install(
            archivePath,
            preview,
            replaceExisting: true,
            () => throw new IOException("refresh failed")));

        Assert.Equal("old", packLoader.ReadInstalledManifest("sample-pack").Schema.Commands[0].Name);
        Assert.False(Directory.Exists(Path.Combine(extensionRoot, "sample-extension")));
    }

    [Fact]
    public void InstallInstallsPackAndSnippet()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore);
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample");
        Directory.CreateDirectory(Path.GetDirectoryName(packLoader.GetInstalledPackPath("sample-pack"))!);
        var preview = service.Preview(archivePath);

        service.Install(archivePath, preview, replaceExisting: false);

        Assert.True(File.Exists(packLoader.GetInstalledPackPath("sample-pack")));
        Assert.Equal("Write-Output 'sample'", snippetStore.Find("sample")!.Script);
    }

    [Fact]
    public void InstallRequiresReplacementConfirmationAndPreservesContentWhenDeclined()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore);
        Directory.CreateDirectory(Path.GetDirectoryName(packLoader.GetInstalledPackPath("sample-pack"))!);
        packLoader.WriteInstalledManifest(CreatePack("sample-pack", "old"));
        snippetStore.Save("sample", "powershell", [], [], "Write-Output 'old'");
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample");
        var preview = service.Preview(archivePath);

        Assert.Throws<InvalidOperationException>(() => service.Install(archivePath, preview, replaceExisting: false));
        Assert.Equal("old", packLoader.ReadInstalledManifest("sample-pack").Schema.Commands[0].Name);
        Assert.Equal("Write-Output 'old'", snippetStore.Find("sample")!.Script);
    }

    [Fact]
    public void InstallRollsBackPackAndSnippetWhenRuntimeRefreshFails()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore);
        Directory.CreateDirectory(Path.GetDirectoryName(packLoader.GetInstalledPackPath("sample-pack"))!);
        packLoader.WriteInstalledManifest(CreatePack("sample-pack", "old"));
        snippetStore.Save("sample", "powershell", [], [], "Write-Output 'old'");
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample");
        var preview = service.Preview(archivePath);
        Assert.Equal(2, preview.Conflicts.Count);

        var exception = Assert.Throws<InvalidOperationException>(() => service.Install(
            archivePath,
            preview,
            replaceExisting: true,
            () => throw new IOException("refresh failed")));

        Assert.Contains("refresh failed", exception.Message);
        Assert.Equal("old", packLoader.ReadInstalledManifest("sample-pack").Schema.Commands[0].Name);
        Assert.Equal("Write-Output 'old'", snippetStore.Find("sample")!.Script);
    }

    [Fact]
    public void PreviewRejectsArchivePathTraversal()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out _, out _);
        var archivePath = Path.Combine(directory.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../addon.json").Open());
            writer.Write("{}");
        }

        Assert.Throws<InvalidDataException>(() => service.Preview(archivePath));
    }

    private static AddonPackageService CreateService(
        string root,
        out CommandPackLoader packLoader,
        out SnippetStore snippetStore,
        ExtensionPackageManager? extensionPackageManager = null)
    {
        var profile = Path.Combine(root, "profile");
        var packs = Path.Combine(profile, "packs");
        Directory.CreateDirectory(packs);
        var stateStore = new ExtensionStateStore(Path.Combine(profile, "state.json"));
        packLoader = new CommandPackLoader([packs], stateStore);
        snippetStore = new SnippetStore(Path.Combine(profile, "snippets"));
        return new AddonPackageService(packLoader, snippetStore, extensionPackageManager);
    }

    private static string CreateAddon(string root, string packId, string snippetName, string? extensionId = null)
    {
        var archivePath = Path.Combine(root, "sample-addon.zip");
        using var archive = new ZipArchive(File.Open(archivePath, FileMode.Create), ZipArchiveMode.Create);
        WriteEntry(archive, "addon.json", JsonSerializer.Serialize(new AddonManifest(
            "sample-addon", "Sample Add-on", "1.0.0", [$"packs/{packId}.json"], [snippetName],
            extensionId is null ? [] : [$"extensions/{extensionId}.zip"])));
        WriteEntry(archive, $"packs/{packId}.json", JsonSerializer.Serialize(CreatePack(packId, "new")));
        WriteEntry(archive, $"snippets/{snippetName}/metadata.json", JsonSerializer.Serialize(new SnippetMetadata(
            Guid.NewGuid().ToString("N"), snippetName, "powershell", [], [])));
        WriteEntry(archive, $"snippets/{snippetName}/script.ps1", "Write-Output 'sample'");
        if (extensionId is not null)
        {
            var extensionPackage = CreateExtensionPackage(root, extensionId);
            var entry = archive.CreateEntry($"extensions/{extensionId}.zip");
            using var source = File.OpenRead(extensionPackage);
            using var destination = entry.Open();
            source.CopyTo(destination);
        }

        return archivePath;
    }

    private static string CreateExtensionPackage(string root, string extensionId)
    {
        var packagePath = Path.Combine(root, $"{extensionId}-package.zip");
        using var archive = new ZipArchive(File.Open(packagePath, FileMode.Create), ZipArchiveMode.Create);
        WriteEntry(archive, "extension.json", JsonSerializer.Serialize(new
        {
            id = extensionId,
            version = "1.0.0",
            name = "Sample Extension",
            @namespace = extensionId,
            schema = new { lists = Array.Empty<object>(), commands = Array.Empty<object>(), functions = Array.Empty<object>() }
        }));
        return packagePath;
    }

    private static CommandPackManifest CreatePack(string id, string commandName)
        => new(id, null, new CommandSchema([],
        [new CommandDefinition(commandName, "url", commandName,
        [new CommandActionDefinition("default", "", commandName, "https://example.test", [])]) ]));

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(text);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
