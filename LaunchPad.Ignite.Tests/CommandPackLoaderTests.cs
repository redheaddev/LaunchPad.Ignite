using System;
using System.IO;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class CommandPackLoaderTests
{
    [Fact]
    public void UpdatesInstalledPackCommandIdentityAndDescription()
    {
        using var directory = new TemporaryDirectory();
        WritePack(directory.Path, "tools", "tools", "tools", "status", "Before");
        var loader = new CommandPackLoader([directory.Path]);
        var original = Assert.Single(loader.Load().Commands);

        loader.UpdatePackCommand("tools", original.QualifiedName, original with
        {
            Name = "health",
            Namespace = "infra",
            Description = "After"
        });

        var updated = Assert.Single(loader.Load().Commands);
        Assert.Equal("infra:health", updated.QualifiedName);
        Assert.Equal("After", updated.Description);
    }

    [Fact]
    public void ImportPackOverwritesSamePackCommandAndRetainsOtherCommandsButSkipsExternalDuplicates()
    {
        using var directory = new TemporaryDirectory();
        var profilePacks = Path.Combine(directory.Path, "profile", "packs");
        var incomingPacks = Path.Combine(directory.Path, "incoming");
        WritePack(profilePacks, "tools", "tools", "tools", "status", "Old status");
        var loader = new CommandPackLoader([profilePacks]);
        var installed = loader.ReadInstalledManifest("tools");
        loader.WriteInstalledManifest(installed with
        {
            Schema = installed.Schema with
            {
                Commands = installed.Schema.Commands.Append(new CommandDefinition(
                    "logs", "url", "Retained logs",
                    [new CommandActionDefinition("default", "", "tools:logs", "https://example.test/logs", [])], "tools")).ToArray()
            }
        });
        WriteManifest(incomingPacks, "tools.json", """
            {
              "id": "tools",
              "namespace": "tools",
              "schema": {
                "lists": [],
                "commands": [
                  { "name": "status", "namespace": "tools", "kind": "url", "description": "Updated status", "actions": [{ "name": "default", "usage": "tools:status", "targetTemplate": "https://example.test/status", "arguments": [] }] },
                  { "name": "external", "namespace": "tools", "kind": "url", "description": "External conflict", "actions": [{ "name": "default", "usage": "tools:external", "targetTemplate": "https://example.test/external", "arguments": [] }] }
                ]
              }
            }
            """);

        var result = loader.ImportPack(
            Path.Combine(incomingPacks, "tools.json"),
            ["tools:external"],
            replaceExisting: true,
            overwriteExistingCommands: true);
        var manifest = loader.ReadInstalledManifest("tools");

        Assert.Equal("Updated status", Assert.Single(manifest.Schema.Commands, command => command.Name == "status").Description);
        Assert.Contains(manifest.Schema.Commands, command => command.Name == "logs" && command.Description == "Retained logs");
        Assert.Contains("tools:external", result.SkippedCommands);
    }

    [Fact]
    public void PreviewPackReturnsValidatedCommandNamesAndExistingOwners()
    {
        using var directory = new TemporaryDirectory();
        WritePack(directory.Path, "tools", "tools", "tools", "status", "Status");

        var preview = CommandPackLoader.PreviewPack(
            Path.Combine(directory.Path, "tools.json"),
            name => name == "tools:status" ? "Pack: existing-tools" : null);

        Assert.Equal("tools", preview.Id);
        Assert.Equal("tools", preview.Namespace);
        var command = Assert.Single(preview.Commands);
        Assert.Equal("tools:status", command.Command.QualifiedName);
        Assert.Equal("Pack: existing-tools", command.ExistingOwner);
    }

    [Fact]
    public void LoadsBricotCommandPackWithServerPickerAndNamespacedShortcuts()
    {
        var packPath = Path.Combine(AppContext.BaseDirectory, "CommandPacks", "bricot.json");
        using var directory = new TemporaryDirectory();
        File.Copy(packPath, Path.Combine(directory.Path, "bricot.json"));

        var schema = new CommandPackLoader([directory.Path]).Load();

        Assert.Equal(15, schema.Commands.Count);
        Assert.Contains(schema.Commands, command => command.QualifiedName == "pl:arch");
        Assert.Contains(schema.Commands, command => command.QualifiedName == "gh:token");
        var sft = Assert.Single(schema.Commands, command => command.Name == "sft");
        Assert.Equal("enum:sft-server", sft.Actions[0].Arguments[0].Ref);
        Assert.Equal(["gwappnp11", "gwappsit01cc01"], Assert.Single(schema.Lists, list => list.Name == "sft-server").Values);
    }

    [Fact]
    public void LoadsCommandsWithDefaultAndCommandSpecificNamespaces()
    {
        using var directory = new TemporaryDirectory();
        WriteManifest(directory.Path, "tools.json", """
            {
              "id": "tools",
              "namespace": "tools",
              "schema": {
                "lists": [],
                "variables": [{ "name": "host", "value": "https://example.test" }],
                "commands": [
                  { "name": "status", "kind": "url", "description": "Default namespace", "actions": [{ "name": "default", "usage": "tools:status", "targetTemplate": "https://example.test/status", "arguments": [] }] },
                  { "name": "log", "namespace": "git", "kind": "url", "description": "Explicit namespace", "actions": [{ "name": "default", "usage": "git:log", "targetTemplate": "https://example.test/log", "arguments": [] }] }
                ],
                "functions": []
              }
            }
            """);

        var schema = new CommandPackLoader([directory.Path]).Load();

        Assert.Contains(schema.Commands, command => command.QualifiedName == "tools:status");
        Assert.Contains(schema.Commands, command => command.QualifiedName == "git:log");
        Assert.Equal("tools:host", Assert.Single(schema.Variables).QualifiedName);
    }

    [Fact]
    public void PreservesExtensionSettingsWhenLoadingCommandPacks()
    {
        using var directory = new TemporaryDirectory();
        var setting = new CommandSettingDefinition(
            "orgUrl", "Organization URL", "ADO organization", "https://dev.azure.com/example", Namespace: "ado");
        var existingSchema = new CommandSchema([], []) { Settings = [setting] };

        var loaded = new CommandPackLoader([directory.Path]).Load(existingSchema);

        Assert.Equal(setting, Assert.Single(loaded.Settings));
    }

    [Fact]
    public void UserPackOverridesBundledPackWithSameId()
    {
        using var bundledDirectory = new TemporaryDirectory();
        using var userDirectory = new TemporaryDirectory();
        WritePack(bundledDirectory.Path, "sample", "sample", "sample", "same", "Bundled");
        WritePack(Path.Combine(userDirectory.Path, "group"), "sample", "sample", "sample", "same", "User");
        WritePack(userDirectory.Path, "extra", "extra", "extra", "only-user", "Additional");

        var schema = new CommandPackLoader([bundledDirectory.Path, userDirectory.Path]).Load();

        Assert.Equal(2, schema.Commands.Count);
        Assert.Equal("User", Assert.Single(schema.Commands, command => command.Name == "same").Description);
        Assert.Contains(schema.Commands, command => command.QualifiedName == "extra:only-user");
    }

    [Fact]
    public void LoadsAndManagesPackFromNestedDirectoryByItsId()
    {
        using var directory = new TemporaryDirectory();
        var packRoot = directory.Path;
        WritePack(Path.Combine(packRoot, "env"), "tools", "tools", "tools", "status", "Before");
        var loader = new CommandPackLoader([packRoot]);

        var original = Assert.Single(loader.Load().Commands);
        Assert.Equal(Path.Combine(packRoot, "env", "tools.json"), loader.GetInstalledPackPath("tools"));
        Assert.Equal("tools", loader.ReadInstalledManifest("tools").Id);
        Assert.Equal("env", Assert.Single(loader.LoadWithSources().Packs).Group);

        loader.UpdatePackCommand("tools", original.QualifiedName, original with { Description = "After" });

        Assert.Equal("After", Assert.Single(loader.ReadInstalledManifest("tools").Schema.Commands).Description);
        loader.DeleteInstalledPack("tools");
        Assert.Empty(loader.Load().Commands);
    }

    [Fact]
    public void RejectsDuplicatePackIdsInDifferentSubdirectoriesOfOneRoot()
    {
        using var directory = new TemporaryDirectory();
        WritePack(Path.Combine(directory.Path, "first"), "tools", "tools", "tools", "first", "First");
        WritePack(Path.Combine(directory.Path, "second"), "tools", "tools", "tools", "second", "Second");

        var exception = Assert.Throws<InvalidDataException>(() => new CommandPackLoader([directory.Path]).Load());

        Assert.Contains("defined more than once", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsPackManifestsNestedBeyondOneGroupDirectory()
    {
        using var directory = new TemporaryDirectory();
        WritePack(Path.Combine(directory.Path, "env", "production"), "tools", "tools", "tools", "status", "Status");

        var exception = Assert.Throws<InvalidDataException>(() => new CommandPackLoader([directory.Path]).Load());

        Assert.Contains("only at the root or one directory level", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllowsMultiplePacksToShareNamespaceWhenCommandNamesDoNotCollide()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        WritePack(firstDirectory.Path, "git-core", "git-core", "git", "status", "Status");
        WritePack(secondDirectory.Path, "git-extra", "git-extra", "git", "log", "Log");

        var schema = new CommandPackLoader([firstDirectory.Path, secondDirectory.Path]).Load();

        Assert.Contains(schema.Commands, command => command.QualifiedName == "git:status");
        Assert.Contains(schema.Commands, command => command.QualifiedName == "git:log");
    }

    [Fact]
    public void RejectsDuplicateQualifiedCommandsAcrossPacks()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        WritePack(firstDirectory.Path, "first", "first", "git", "status", "First");
        WritePack(secondDirectory.Path, "second", "second", "git", "status", "Second");

        var exception = Assert.Throws<InvalidDataException>(() =>
            new CommandPackLoader([firstDirectory.Path, secondDirectory.Path]).Load());

        Assert.Contains("duplicate command 'git:status'", exception.Message);
    }

    [Fact]
    public void RejectsCommandCollisionWithExistingExtensionSchema()
    {
        using var directory = new TemporaryDirectory();
        WritePack(directory.Path, "git-tools", "git-tools", "git", "status", "Pack status");
        var existingCommand = new CommandDefinition(
            "status",
            "url",
            "Extension status",
            [new CommandActionDefinition("default", "", "git:status", "https://example.test", [])],
            "git");
        var existingSchema = new CommandSchema([], [existingCommand]);

        var exception = Assert.Throws<InvalidDataException>(() =>
            new CommandPackLoader([directory.Path]).Load(existingSchema));

        Assert.Contains("duplicate command 'git:status'", exception.Message);
    }

    [Fact]
    public void RejectsManifestIdThatDoesNotMatchFilename()
    {
        using var directory = new TemporaryDirectory();
        WritePack(directory.Path, "file-id", "manifest-id", "manifest-id", "status", "Status");

        var exception = Assert.Throws<InvalidDataException>(() => new CommandPackLoader([directory.Path]).Load());

        Assert.Contains("filename requires ID 'file-id'", exception.Message);
    }

    [Fact]
    public void AllowsGlobalCommandsAndTracksPackId()
    {
        using var directory = new TemporaryDirectory();
        WriteManifest(directory.Path, "tools.json", """
            {
              "id": "tools",
              "schema": {
                "lists": [],
                "commands": [{ "name": "status", "kind": "url", "description": "Status", "actions": [{ "name": "default", "usage": "status", "targetTemplate": "https://example.test", "arguments": [] }] }]
              }
            }
            """);

        var loaded = new CommandPackLoader([directory.Path]).LoadWithSources();

        Assert.Equal("status", Assert.Single(loaded.Schema.Commands).QualifiedName);
        Assert.Equal("tools", loaded.CommandSources["status"]);
    }

    [Fact]
    public void DisabledPackRemainsInInventoryButDoesNotContributeSchema()
    {
        using var directory = new TemporaryDirectory();
        var packsDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "packs")).FullName;
        WritePack(packsDirectory, "tools", "tools", "tools", "status", "Status");
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        stateStore.SetEnabled("tools", enabled: false);

        var loaded = new CommandPackLoader([packsDirectory], stateStore).LoadWithSources();

        Assert.Empty(loaded.Schema.Commands);
        Assert.Empty(loaded.CommandSources);
        var pack = Assert.Single(loaded.Packs);
        Assert.Equal("tools", pack.Id);
        Assert.False(pack.Enabled);
        Assert.Equal(["tools:status"], pack.Commands);
        Assert.Contains("tools:status", loaded.DisabledCommandNames);
        Assert.Contains("tools:status", loaded.PackedCommandNames);

        var migratedCommand = new CommandDefinition(
            "status",
            "url",
            "Persisted copy",
            [new CommandActionDefinition("default", "", "tools:status", "https://example.test", [])],
            "tools");
        var filteredSchema = loaded.FilterDisabledCommands(new CommandSchema([], [migratedCommand]));
        Assert.Empty(filteredSchema.Commands);
    }

    [Fact]
    public void RejectsCommandPackIdThatConflictsWithExtensionId()
    {
        using var directory = new TemporaryDirectory();
        WritePack(directory.Path, "tools", "tools", "tools", "status", "Status");

        var exception = Assert.Throws<InvalidDataException>(() =>
            new CommandPackLoader([directory.Path]).LoadWithSources(existingComponentIds: ["tools"]));

        Assert.Contains("conflicts with an extension ID", exception.Message);
    }

    [Fact]
    public void InstallsValidatedPackIntoProfileDirectory()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        var installDirectory = Path.Combine(directory.Path, "profile", "packs");
        WriteManifest(directory.Path, "download.json", """
            {
              "id": "tools",
              "namespace": "tools",
              "schema": {
                "lists": [],
                "variables": [],
                "commands": [{ "name": "status", "kind": "url", "description": "Status", "actions": [{ "name": "default", "usage": "tools:status", "targetTemplate": "https://example.test", "arguments": [] }] }],
                "functions": []
              }
            }
            """);

        var id = CommandPackLoader.InstallPack(sourcePath, installDirectory, replaceExisting: false);
        var loaded = new CommandPackLoader([installDirectory]).LoadWithSources();

        Assert.Equal("tools", id);
        Assert.True(File.Exists(Path.Combine(installDirectory, "tools.json")));
        Assert.Equal("tools:status", Assert.Single(loaded.Schema.Commands).QualifiedName);
    }

    [Fact]
    public void PackReplacementRequiresExplicitApproval()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        var installDirectory = Path.Combine(directory.Path, "profile", "packs");
        WritePack(installDirectory, "tools", "tools", "tools", "old", "Old");
        WritePack(directory.Path, "replacement", "tools", "tools", "new", "New");
        File.Copy(Path.Combine(directory.Path, "replacement.json"), sourcePath, overwrite: true);
        var existingContents = File.ReadAllText(Path.Combine(installDirectory, "tools.json"));

        Assert.Throws<IOException>(() => CommandPackLoader.InstallPack(sourcePath, installDirectory, replaceExisting: false));

        Assert.Equal(existingContents, File.ReadAllText(Path.Combine(installDirectory, "tools.json")));
    }

    [Fact]
    public void ReplacesNestedPackInItsExistingDirectory()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "replacement.json");
        var installDirectory = Path.Combine(directory.Path, "profile", "packs");
        WritePack(Path.Combine(installDirectory, "env"), "tools", "tools", "tools", "old", "Old");
        WritePack(directory.Path, "replacement", "tools", "tools", "new", "New");

        CommandPackLoader.InstallPack(sourcePath, installDirectory, replaceExisting: true);

        Assert.False(File.Exists(Path.Combine(installDirectory, "tools.json")));
        Assert.Equal("New", Assert.Single(new CommandPackLoader([installDirectory]).Load().Commands).Description);
    }

    [Fact]
    public void LoadsAnAuthoredPackFromItsInstalledProfileDirectory()
    {
        using var directory = new TemporaryDirectory();
        WritePack(directory.Path, "andmil", "andmil", null, "adusers", "Launch adusers");

        var loaded = new CommandPackLoader([directory.Path]).LoadWithSources();

        Assert.Equal("andmil:adusers", Assert.Single(loaded.Schema.Commands).QualifiedName);
        Assert.Equal("andmil", loaded.CommandSources["andmil:adusers"]);
    }

    [Fact]
    public void RejectsCustomCommandKinds()
    {
        using var directory = new TemporaryDirectory();
        WriteManifest(directory.Path, "tools.json", """
            {
              "id": "tools",
              "namespace": "tools",
              "schema": {
                "lists": [],
                "commands": [{ "name": "custom", "kind": "plugin", "description": "Custom", "actions": [{ "name": "default", "usage": "tools:custom", "targetTemplate": "custom", "arguments": [] }] }]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(() => new CommandPackLoader([directory.Path]).Load());

        Assert.Contains("unsupported command kind 'system' or handler 'plugin'", exception.Message);
    }

    [Fact]
    public void RejectsScriptFunctions()
    {
        using var directory = new TemporaryDirectory();
        WriteManifest(directory.Path, "tools.json", """
            {
              "id": "tools",
              "namespace": "tools",
              "schema": {
                "lists": [],
                "commands": [],
                "functions": [{ "name": "lookup", "kind": "script", "parameters": [], "interpreter": "pwsh", "scriptPath": "lookup.ps1" }]
              }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(() => new CommandPackLoader([directory.Path]).Load());

        Assert.Contains("built-in httpGet kind", exception.Message);
    }

    [Fact]
    public void RejectsUnknownManifestFieldsInsteadOfIgnoringPotentialCodeReferences()
    {
        using var directory = new TemporaryDirectory();
        WriteManifest(directory.Path, "tools.json", """
            {
              "id": "tools",
              "namespace": "tools",
              "assembly": "plugin.dll",
              "schema": { "lists": [], "commands": [] }
            }
            """);

        var exception = Assert.Throws<InvalidDataException>(() => new CommandPackLoader([directory.Path]).Load());

        Assert.Contains("is invalid", exception.Message);
    }

    private static void WritePack(
        string root,
        string fileId,
        string manifestId,
        string? packNamespace,
        string commandName,
        string description)
    {
        Directory.CreateDirectory(root);
        var namespaceProperty = packNamespace is null ? string.Empty : $"\"namespace\": \"{packNamespace}\",";
        var commandNamespace = packNamespace ?? manifestId;
        var json = $$"""
            {
              "id": "{{manifestId}}",
              {{namespaceProperty}}
              "schema": {
                "lists": [],
                "commands": [{ "name": "{{commandName}}", "namespace": "{{commandNamespace}}", "kind": "url", "description": "{{description}}", "actions": [{ "name": "default", "usage": "{{commandNamespace}}:{{commandName}}", "targetTemplate": "https://example.test", "arguments": [] }] }]
              }
            }
            """;
        WriteManifest(root, $"{fileId}.json", json);
    }

    private static void WriteManifest(string root, string fileName, string content)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, fileName), content);
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
