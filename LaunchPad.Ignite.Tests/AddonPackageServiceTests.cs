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
    public void PreviewListsExistingItemsAndIncomingCommandOwners()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore,
            getCommandOwner: name => name == "sample:existing" ? "Pack: current-pack" : null);
        packLoader.WriteInstalledManifest(CreatePack("sample-pack", "existing-pack-command"));
        snippetStore.Save("sample", "powershell", [], [], "Write-Output 'existing'");
        var archivePath = CreateAddonWithCommands(directory.Path, "sample-pack", "sample", [
            new CommandDefinition("existing", "url", "Existing command", [
                new CommandActionDefinition("default", "", "sample:existing", "https://example.test/existing", [])], "sample"),
            new CommandDefinition("new", "url", "New command", [
                new CommandActionDefinition("default", "", "sample:new", "https://example.test/new", [])], "sample")
        ]);

        var preview = service.Preview(archivePath);
        var pack = Assert.Single(preview.Items, item => item.Category == "Command pack");
        var existingCommand = Assert.Single(pack.Commands!, item => item.Name == "sample:existing");
        var newCommand = Assert.Single(pack.Commands!, item => item.Name == "sample:new");

        Assert.True(pack.AlreadyExists);
        Assert.Equal("1.2.3", pack.Version);
        Assert.Equal(0, pack.ImportCommandCount);
        Assert.Equal(2, pack.TotalCommandCount);
        Assert.True(existingCommand.AlreadyExists);
        Assert.Equal("Pack: current-pack", existingCommand.ExistingOwner);
        Assert.False(newCommand.AlreadyExists);
        Assert.Equal(0, existingCommand.ImportCommandCount);
        Assert.Equal(1, newCommand.ImportCommandCount);
        Assert.Contains(preview.Items, item => item.Category == "Snippet" && item.Name == "sample" && item.AlreadyExists);
    }

    [Fact]
    public void PreviewIncludesExtensionVersionAndCommandCounts()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out _, out _, getCommandOwner: name => name == "sample-extension:owned" ? "Pack: other-pack" : null);
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample", "sample-extension");

        var preview = service.Preview(archivePath);

        var extension = Assert.Single(preview.Items, item => item.Category == "Extension");
        Assert.Equal("1.0.0", extension.Version);
        Assert.Equal(1, extension.ImportCommandCount);
        Assert.Equal(2, extension.TotalCommandCount);
    }

    [Fact]
    public void PreviewExtensionExcludesCommandsOwnedByOtherPacks()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out _, out _, getCommandOwner: name =>
            name == "sample-extension:owned" ? "Pack: other-pack" : null);
        var archivePath = CreateAddonWithCommands(directory.Path, "sample-pack", "sample", [], "sample-extension");

        var preview = service.Preview(archivePath);

        var extension = Assert.Single(preview.Items, item => item.Category == "Extension");
        Assert.Equal(1, extension.ImportCommandCount);
        Assert.Equal(2, extension.TotalCommandCount);
        Assert.Contains(extension.Commands!, command => command.Name == "sample-extension:owned" && command.ExistingOwner == "Pack: other-pack");
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
    public void CreatePackageUsesProfileRelativePathsAndCanBePreviewedAndInstalled()
    {
        using var directory = new TemporaryDirectory();
        var profile = Path.Combine(directory.Path, "profile");
        var packs = Path.Combine(profile, "packs");
        var extensionRoot = Path.Combine(profile, "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot, Path.Combine(directory.Path, "packages"));
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore, packageManager);
        var extensionPackagePath = CreateExtensionPackage(directory.Path, "sample-extension");
        packageManager.Install(extensionPackagePath);
        Directory.CreateDirectory(Path.Combine(packs, "group"));
        var groupedPackPath = Path.Combine(packs, "group", "sample-pack.json");
        File.WriteAllText(groupedPackPath, JsonSerializer.Serialize(CreatePack("sample-pack", "sample")));
        snippetStore.Save("sample", "powershell", [], [], "Write-Output 'sample'");
        var archivePath = Path.Combine(directory.Path, "created-addon.zip");

        Assert.Equal("group", Assert.Single(service.GetAvailablePacks()).Group);

        service.CreatePackage(
            archivePath,
            "sample-addon",
            "Sample Add-on",
            "1.2.3",
            ["sample-pack"],
            ["sample"],
            ["sample-extension"]);

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var paths = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("addon.json", paths);
            Assert.Contains("packs/group/sample-pack.json", paths);
            Assert.Contains("snippets/sample/metadata.json", paths);
            Assert.Contains("snippets/sample/script.ps1", paths);
            Assert.Contains("extensions/sample-extension/extension.json", paths);
            Assert.DoesNotContain(paths, path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        var preview = service.Preview(archivePath);
        Assert.Equal(["sample-pack"], preview.PackIds);
        Assert.Equal(["sample"], preview.SnippetNames);
        Assert.Equal(["sample-extension"], preview.ExtensionIds);

        var targetProfile = Path.Combine(directory.Path, "target-profile");
        var targetManager = new ExtensionPackageManager(Path.Combine(targetProfile, "extensions"), Path.Combine(directory.Path, "target-packages"));
        var targetService = new AddonPackageService(
            new CommandPackLoader([Path.Combine(targetProfile, "packs")]),
            new SnippetStore(Path.Combine(targetProfile, "snippets")),
            targetManager);
        targetService.Install(archivePath, targetService.Preview(archivePath), replaceExisting: false);

        Assert.True(File.Exists(Path.Combine(targetProfile, "packs", "group", "sample-pack.json")));
        Assert.True(File.Exists(Path.Combine(targetProfile, "snippets", "sample", "metadata.json")));
        Assert.True(File.Exists(Path.Combine(targetProfile, "extensions", "sample-extension", "extension.json")));
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
    public void InstallSkipExistingSkipsConflictingPackButInstallsNonConflictingSnippet()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateService(directory.Path, out var packLoader, out var snippetStore);
        packLoader.WriteInstalledManifest(CreatePack("sample-pack", "existing"));
        var archivePath = CreateAddon(directory.Path, "sample-pack", "sample");
        var preview = service.Preview(archivePath);

        var result = service.Install(archivePath, preview, ImportConflictPolicy.SkipExisting);

        Assert.Equal("existing", Assert.Single(packLoader.ReadInstalledManifest("sample-pack").Schema.Commands).Name);
        Assert.Equal("Write-Output 'sample'", snippetStore.Find("sample")!.Script);
        Assert.Contains("Command pack: sample-pack", result.SkippedItems);
        Assert.Empty(result.SkippedItems.Where(item => item.StartsWith("Snippet:", StringComparison.Ordinal)));
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
        ExtensionPackageManager? extensionPackageManager = null,
        Func<string, string?>? getCommandOwner = null)
    {
        var profile = Path.Combine(root, "profile");
        var packs = Path.Combine(profile, "packs");
        Directory.CreateDirectory(packs);
        var stateStore = new ExtensionStateStore(Path.Combine(profile, "state.json"));
        packLoader = new CommandPackLoader([packs], stateStore);
        snippetStore = new SnippetStore(Path.Combine(profile, "snippets"));
        return new AddonPackageService(packLoader, snippetStore, extensionPackageManager, getCommandOwner);
    }

    private static string CreateAddon(string root, string packId, string snippetName, string? extensionId = null)
        => CreateAddonWithCommands(root, packId, snippetName, [CreatePack(packId, "new").Schema.Commands[0]], extensionId);

    private static string CreateAddonWithCommands(
        string root,
        string packId,
        string snippetName,
        IReadOnlyList<CommandDefinition> commands,
        string? extensionId = null)
    {
        var archivePath = Path.Combine(root, "sample-addon.zip");
        using var archive = new ZipArchive(File.Open(archivePath, FileMode.Create), ZipArchiveMode.Create);
        WriteEntry(archive, "addon.json", JsonSerializer.Serialize(new AddonManifest(
            "sample-addon", "Sample Add-on", "1.0.0", [$"packs/{packId}.json"], [snippetName],
            extensionId is null ? [] : [$"extensions/{extensionId}.zip"])));
        WriteEntry(archive, $"packs/{packId}.json", JsonSerializer.Serialize(new CommandPackManifest(
            packId,
            packId,
            new CommandSchema([], commands),
            Version: "1.2.3")));
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
            schema = new
            {
                lists = Array.Empty<object>(),
                commands = new[]
                {
                    new
                    {
                        name = "owned",
                        @namespace = extensionId,
                        kind = "url",
                        description = "Owned command",
                        actions = new[] { new { name = "default", usage = $"{extensionId}:owned", targetTemplate = "https://example.test", arguments = Array.Empty<object>() } }
                    },
                    new
                    {
                        name = "new",
                        @namespace = extensionId,
                        kind = "url",
                        description = "New command",
                        actions = new[] { new { name = "default", usage = $"{extensionId}:new", targetTemplate = "https://example.test", arguments = Array.Empty<object>() } }
                    }
                },
                functions = Array.Empty<object>()
            }
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
