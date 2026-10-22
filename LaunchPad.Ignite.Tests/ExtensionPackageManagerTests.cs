using System.IO.Compression;
using System.Text.Json.Nodes;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ExtensionPackageManagerTests
{
    [Fact]
    public void ValidatePackageReturnsExtensionId()
    {
        using var directory = new TemporaryDirectory();
        var packagePath = CreatePackage(directory.Path);
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"));

        var extensionId = packageManager.ValidatePackage(packagePath);

        Assert.Equal("sample", extensionId);
    }

    [Fact]
    public void InstallsAndUninstallsZipPackage()
    {
        using var directory = new TemporaryDirectory();
        var packagePath = CreatePackage(directory.Path);
        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot);
        var refreshedIds = new List<string>();

        var installedId = packageManager.Install(packagePath, refreshedIds.Add);

        Assert.Equal("sample", installedId);
        Assert.Equal(["sample"], refreshedIds);
        Assert.True(File.Exists(Path.Combine(extensionRoot, "sample", "extension.json")));
        Assert.Equal("sample", Assert.Single(new ExtensionLoader([extensionRoot]).Load().Extensions).Id);

        packageManager.Uninstall(installedId, refreshedIds.Add);

        Assert.Equal(["sample", "sample"], refreshedIds);
        Assert.False(Directory.Exists(Path.Combine(extensionRoot, "sample")));
    }

    [Fact]
    public void InstallRejectsArchivePathTraversal()
    {
        using var directory = new TemporaryDirectory();
        var packagePath = Path.Combine(directory.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open());
            writer.Write("outside");
        }

        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot);

        Assert.Throws<InvalidDataException>(() => packageManager.Install(packagePath));
        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
        Assert.False(Directory.Exists(extensionRoot));
    }

    [Fact]
    public void FailedInstallRefreshRestoresPreviouslyInstalledPackage()
    {
        using var directory = new TemporaryDirectory();
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"));
        var originalPackage = CreatePackage(directory.Path, "original");
        var replacementPackage = CreatePackage(directory.Path, "replacement");
        packageManager.Install(originalPackage);

        Assert.Throws<InvalidOperationException>(() => packageManager.Install(
            replacementPackage,
            _ => throw new InvalidOperationException("runtime reload failed")));

        var manifest = File.ReadAllText(Path.Combine(directory.Path, "extensions", "sample", "extension.json"));
        Assert.Contains("original", manifest);
    }

    [Fact]
    public void InstallPackagesInstallsAllPackagesAndRefreshesOnce()
    {
        using var directory = new TemporaryDirectory();
        var packageDirectory = Path.Combine(directory.Path, "packages");
        Directory.CreateDirectory(packageDirectory);
        File.Copy(CreatePackage(directory.Path, id: "sample"), Path.Combine(packageDirectory, "sample-1.0.0.zip"));
        File.Copy(CreatePackage(directory.Path, id: "other"), Path.Combine(packageDirectory, "other-1.0.0.zip"));
        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot, packageDirectory);
        IReadOnlyList<string>? refreshedIds = null;

        var installedIds = packageManager.InstallPackages(
            ["sample-1.0.0", "other-1.0.0"],
            ids => refreshedIds = ids);

        Assert.Equal(["sample", "other"], installedIds);
        Assert.Equal(["sample", "other"], refreshedIds);
        Assert.True(File.Exists(Path.Combine(extensionRoot, "sample", "extension.json")));
        Assert.True(File.Exists(Path.Combine(extensionRoot, "other", "extension.json")));
    }

    [Fact]
    public void FailedBatchInstallRefreshRestoresAllPreviouslyInstalledPackages()
    {
        using var directory = new TemporaryDirectory();
        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot);
        packageManager.Install(CreatePackage(directory.Path, marker: "original-sample", id: "sample"));
        packageManager.Install(CreatePackage(directory.Path, marker: "original-other", id: "other"));
        var replacementSample = CreatePackage(directory.Path, marker: "replacement-sample", id: "sample");
        var replacementOther = CreatePackage(directory.Path, marker: "replacement-other", id: "other");

        Assert.Throws<InvalidOperationException>(() => packageManager.InstallPackages(
            [replacementSample, replacementOther],
            _ => throw new InvalidOperationException("runtime reload failed")));

        Assert.Contains("original-sample", File.ReadAllText(Path.Combine(extensionRoot, "sample", "extension.json")));
        Assert.Contains("original-other", File.ReadAllText(Path.Combine(extensionRoot, "other", "extension.json")));
    }

    [Fact]
    public void FailedUninstallRefreshRestoresExtensionDirectory()
    {
        using var directory = new TemporaryDirectory();
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"));
        var packagePath = CreatePackage(directory.Path);
        packageManager.Install(packagePath);

        Assert.Throws<InvalidOperationException>(() => packageManager.Uninstall(
            "sample",
            _ => throw new InvalidOperationException("runtime reload failed")));

        Assert.True(File.Exists(Path.Combine(directory.Path, "extensions", "sample", "extension.json")));
    }

    [Fact]
    public void UninstallFindsUserExtensionByManifestIdWhenDirectoryNameDiffers()
    {
        using var directory = new TemporaryDirectory();
        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var differentlyNamedDirectory = Path.Combine(extensionRoot, "ado-package-1.0");
        Directory.CreateDirectory(differentlyNamedDirectory);
        File.WriteAllText(Path.Combine(differentlyNamedDirectory, "extension.json"), """
            {
              "id": "ado",
              "name": "Azure DevOps",
              "namespace": "ado",
              "schema": { "lists": [], "commands": [] }
            }
            """);
        var packageManager = new ExtensionPackageManager(extensionRoot);

        packageManager.Uninstall("ado");

        Assert.False(Directory.Exists(differentlyNamedDirectory));
    }

    [Fact]
    public void ExtensionCommandInstallsPackageEvenWhenNoExtensionsAreInstalled()
    {
        using var directory = new TemporaryDirectory();
        var packagePath = CreatePackage(directory.Path);
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"));
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider([])));
        var refreshCount = 0;
        var model = new ExtensionCommandModel(stateStore, [], () => refreshCount++, packageManager);

        var parsed = parser.Parse($"extension install {packagePath}");
        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = model.Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("Extension 'sample' installed.", result.Message);
        Assert.Equal(1, refreshCount);
        Assert.True(stateStore.IsEnabled("sample"));
    }

    [Fact]
    public void ExtensionCommandInstallsMultipleSelectedPackages()
    {
        using var directory = new TemporaryDirectory();
        var packageDirectory = Path.Combine(directory.Path, "packages");
        Directory.CreateDirectory(packageDirectory);
        File.Copy(CreatePackage(directory.Path, id: "sample"), Path.Combine(packageDirectory, "sample-1.0.0.zip"));
        File.Copy(CreatePackage(directory.Path, id: "other"), Path.Combine(packageDirectory, "other-1.0.0.zip"));
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"), packageDirectory);
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider([])));
        var refreshCount = 0;
        var model = new ExtensionCommandModel(stateStore, [], () => refreshCount++, packageManager);

        var parsed = parser.Parse("extension install sample-1.0.0,other-1.0.0");
        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = model.Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("Extensions 'sample', 'other' installed.", result.Message);
        Assert.Equal(1, refreshCount);
        Assert.True(stateStore.IsEnabled("sample"));
        Assert.True(stateStore.IsEnabled("other"));
    }

    [Fact]
    public void ExtensionInstallWithoutArgumentListsAvailablePackages()
    {
        using var directory = new TemporaryDirectory();
        var packageDirectory = Path.Combine(directory.Path, "packages");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "media-1.0.0.zip"), string.Empty);
        File.WriteAllText(Path.Combine(packageDirectory, "ado-1.0.1.zip"), string.Empty);
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider([])));
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            packageManager: new ExtensionPackageManager(
                Path.Combine(directory.Path, "extensions"),
                packageDirectory));

        var parsed = parser.Parse("extension install");
        Assert.Equal(CommandParseStatus.Incomplete, parsed.Status);
        var result = model.ListAvailablePackages();

        Assert.True(result.Succeeded);
        Assert.True(result.IsValueOutput);
        Assert.Equal(
            "Available extension packages:" + Environment.NewLine + "  ado-1.0.1" + Environment.NewLine + "  media-1.0.0",
            result.Message);
    }

    [Fact]
    public void ExtensionInstallWithoutArgumentReportsWhenNoPackagesAreAvailable()
    {
        using var directory = new TemporaryDirectory();
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider([])));
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            packageManager: new ExtensionPackageManager(
                Path.Combine(directory.Path, "extensions"),
                Path.Combine(directory.Path, "packages")));

        var parsed = parser.Parse("extension install");
        Assert.Equal(CommandParseStatus.Incomplete, parsed.Status);
        var result = model.ListAvailablePackages();

        Assert.True(result.Succeeded);
        Assert.True(result.IsValueOutput);
        Assert.Equal("No extension packages are available.", result.Message);
    }

    [Fact]
    public void ExtensionCommandInstallsPackageByIdFromPackageInbox()
    {
        using var directory = new TemporaryDirectory();
        var packageDirectory = Path.Combine(directory.Path, "packages");
        Directory.CreateDirectory(packageDirectory);
        File.Copy(
            CreatePackage(directory.Path, marker: "older", version: "1.0.9"),
            Path.Combine(packageDirectory, "sample-1.0.9.zip"));
        File.Copy(
            CreatePackage(directory.Path, marker: "newer", version: "1.0.10"),
            Path.Combine(packageDirectory, "sample-1.0.10.zip"));
        var extensionRoot = Path.Combine(directory.Path, "extensions");
        var packageManager = new ExtensionPackageManager(extensionRoot, packageDirectory);
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider([])));
        var model = new ExtensionCommandModel(stateStore, [], packageManager: packageManager);

        var parsed = parser.Parse("extension install sample");
        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = model.Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("Extension 'sample' installed.", result.Message);
        var installedManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(extensionRoot, "sample", "extension.json")))!;
        Assert.Equal("1.0.10", installedManifest["version"]!.GetValue<string>());
        Assert.Equal("newer", installedManifest["marker"]!.GetValue<string>());
    }

    [Fact]
    public void ExtensionCommandUninstallRemovesDisabledState()
    {
        using var directory = new TemporaryDirectory();
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"));
        packageManager.Install(CreatePackage(directory.Path));
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        stateStore.SetEnabled("sample", enabled: false);
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider(["sample"])));
        var refreshCount = 0;
        var model = new ExtensionCommandModel(stateStore, ["sample"], () => refreshCount++, packageManager);

        var parsed = parser.Parse("extension uninstall sample");
        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = model.Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.True(stateStore.IsEnabled("sample"));
        Assert.Equal(1, refreshCount);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "extensions", "sample")));
    }

    [Fact]
    public void ExtensionCommandUninstallsMultipleSelectedExtensionsTogether()
    {
        using var directory = new TemporaryDirectory();
        var packageManager = new ExtensionPackageManager(Path.Combine(directory.Path, "extensions"));
        packageManager.Install(CreatePackage(directory.Path));
        packageManager.Install(CreatePackage(directory.Path, id: "other"));
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        stateStore.SetEnabled(["sample", "other"], enabled: false);
        var ids = new[] { "sample", "other" };
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider(ids)));
        var refreshCount = 0;
        var model = new ExtensionCommandModel(stateStore, ids, () => refreshCount++, packageManager);

        var parsed = parser.Parse("extension uninstall sample,other");
        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = model.Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("Extensions 'sample', 'other' uninstalled.", result.Message);
        Assert.Equal(1, refreshCount);
        Assert.True(stateStore.IsEnabled("sample"));
        Assert.True(stateStore.IsEnabled("other"));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "extensions", "sample")));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "extensions", "other")));
    }

    private static string CreatePackage(
        string directory,
        string? marker = null,
        string id = "sample",
        string version = "1.0.0")
    {
        var packagePath = Path.Combine(directory, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("extension.json").Open());
        var manifest = JsonNode.Parse("""
            {
              "id": "sample",
              "name": "Sample extension",
              "namespace": "sample",
              "version": "1.0.0",
              "schema": { "lists": [], "commands": [] }
            }
            """)!.AsObject();
        manifest["id"] = id;
        manifest["version"] = version;
        if (marker is not null)
        {
            manifest["marker"] = marker;
        }

        writer.Write(manifest.ToJsonString());
        return packagePath;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
            => Directory.Delete(Path, recursive: true);
    }
}
