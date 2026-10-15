using System;
using System.IO;
using System.Linq;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.ViewModels;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class CommandPackOwnershipTests
{
    [Fact]
    public void CommandSourceGroupsStartCollapsedAndCanExpand()
    {
        var source = new CommandSourceViewModel("sample-pack", "Pack: sample-pack", []);

        Assert.False(source.IsExpanded);

        source.IsExpanded = true;

        Assert.True(source.IsExpanded);
    }

    [Fact]
    public void UserCommandSourceIsNotMarkedAsPack()
    {
        var source = new CommandSourceViewModel(CommandOwnershipManager.UserOwnerId, "User commands", [], isExpanded: false);

        Assert.False(source.IsExpanded);
    }

    [Fact]
    public void DependencyResolverIncludesEnumListsAndRecursiveVariables()
    {
        var command = CreateCommand("open", "tools", new CommandActionDefinition(
            "default",
            "",
            "tools:open <target>",
            "https://example.test/$tools:base/$tools:token",
            [new CommandArgumentDefinition("target", Ref: "enum:targets")]));
        var lists = new[] { new CommandListDefinition("enum", "targets", ["one"], Namespace: "tools") };
        var variables = new[]
        {
            new CommandVariableDefinition("base", "api", "tools"),
            new CommandVariableDefinition("token", "$tools:base", "tools")
        };

        var dependencies = new CommandPackDependencyResolver().Resolve([command], lists, variables);

        Assert.Equal("tools:targets", Assert.Single(dependencies.Lists).QualifiedName);
        Assert.Equal(new[] { "tools:base", "tools:token" }, dependencies.Variables.Select(variable => variable.QualifiedName).ToArray());
        Assert.Empty(dependencies.UnresolvedListReferences);
        Assert.Empty(dependencies.UnresolvedVariableReferences);
    }

    [Fact]
    public void ExportPlanAppliesNamespaceAndIncludesDependencies()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packRoot = Path.Combine(directory.Path, "packs");
        var packLoader = new CommandPackLoader([packRoot], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        var command = CreateCommand("open", null, new CommandActionDefinition(
            "default",
            "",
            "open <target>",
            "https://example.test/$base",
            [new CommandArgumentDefinition("target", Ref: "enum:targets")]));
        userLoader.WriteUserSchemaForTransfer(new CommandSchema(
            [new CommandListDefinition("enum", "targets", ["one"])],
            [command])
        {
            Variables = [new CommandVariableDefinition("base", "api")]
        });
        var manager = new CommandOwnershipManager(
            userLoader,
            packLoader,
            stateStore,
            [],
            [],
            () => { });

        var plan = manager.CreateExportPlan(
            [new CommandOwnershipKey(CommandOwnershipManager.UserOwnerId, "open")],
            "sample-pack",
            "sample");

        Assert.Equal("sample:open", Assert.Single(plan.ExportedCommandNames));
        Assert.Null(Assert.Single(plan.Manifest.Schema.Commands).Namespace);
        Assert.Single(plan.Dependencies.Lists);
        Assert.Single(plan.Dependencies.Variables);
    }

    [Fact]
    public void ExportPlanRejectsCommandsThatCollideAfterNamespaceIsApplied()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packLoader = new CommandPackLoader([Path.Combine(directory.Path, "packs")], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        userLoader.WriteUserSchemaForTransfer(new CommandSchema([],
        [
            CreateCommand("status", "first", new CommandActionDefinition("default", "", "first:status", "https://example.test/1", [])),
            CreateCommand("status", "second", new CommandActionDefinition("default", "", "second:status", "https://example.test/2", []))
        ]));
        var manager = new CommandOwnershipManager(userLoader, packLoader, stateStore, [], [], () => { });

        var exception = Assert.Throws<InvalidDataException>(() => manager.CreateExportPlan(
        [
            new CommandOwnershipKey(CommandOwnershipManager.UserOwnerId, "first:status"),
            new CommandOwnershipKey(CommandOwnershipManager.UserOwnerId, "second:status")
        ], "sample-pack", "sample"));

        Assert.Contains("sample:status", exception.Message);
    }

    [Fact]
    public void InventoryIncludesUserCommandsAndEnabledPacksOnly()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packRoot = Path.Combine(directory.Path, "packs");
        var packLoader = new CommandPackLoader([packRoot], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        userLoader.WriteUserSchemaForTransfer(new CommandSchema([],
        [CreateCommand("personal", null, new CommandActionDefinition("default", "", "personal", "https://example.test", []))]));
        WritePack(packLoader, "enabled-pack", "enabled", "enabled-command");
        WritePack(packLoader, "disabled-pack", "disabled", "disabled-command");
        stateStore.SetEnabled("disabled-pack", false);
        var manager = new CommandOwnershipManager(
            userLoader,
            packLoader,
            stateStore,
            [
                new LoadedCommandPackInfo("enabled-pack", true) { Commands = ["enabled:enabled-command"] },
                new LoadedCommandPackInfo("disabled-pack", false) { Commands = ["disabled:disabled-command"] }
            ],
            [],
            () => { });

        var inventory = manager.GetEligibleCommands();

        Assert.Contains(inventory, item => item.QualifiedName == "personal" && item.IsUserCommand);
        Assert.Contains(inventory, item => item.QualifiedName == "enabled:enabled-command");
        Assert.DoesNotContain(inventory, item => item.QualifiedName == "disabled:disabled-command");
    }

    [Fact]
    public void MovingLastCommandOutOfPackDeletesThePack()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packLoader = new CommandPackLoader([Path.Combine(directory.Path, "packs")], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        WritePack(packLoader, "single-command", "single", "only");
        var manager = new CommandOwnershipManager(
            userLoader,
            packLoader,
            stateStore,
            [new LoadedCommandPackInfo("single-command", true) { Commands = ["single:only"] }],
            [],
            () => { });

        var result = manager.MoveCommands(
            [new CommandOwnershipKey("single-command", "single:only")],
            destinationPackId: null);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(packLoader.GetInstalledPackPath("single-command")));
        Assert.Contains(userLoader.ReadUserSchemaForTransfer().Commands, command => command.QualifiedName == "single:only");
    }

    [Fact]
    public void MovingUserCommandToNewPackCreatesPackAndRemovesUserCommand()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packLoader = new CommandPackLoader([Path.Combine(directory.Path, "packs")], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        userLoader.WriteUserSchemaForTransfer(new CommandSchema([],
        [CreateCommand("personal", null, new CommandActionDefinition("default", "", "personal", "https://example.test", []))]));
        var manager = new CommandOwnershipManager(userLoader, packLoader, stateStore, [], [], () => { });

        var result = manager.MoveCommandsToNewPack(
            [new CommandOwnershipKey(CommandOwnershipManager.UserOwnerId, "personal")],
            "personal-pack",
            "personal");

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(userLoader.ReadUserSchemaForTransfer().Commands);
        var pack = packLoader.ReadInstalledManifest("personal-pack");
        Assert.Equal("personal", pack.Namespace);
        Assert.Equal("personal", Assert.Single(pack.Schema.Commands).QualifiedName);
    }

    [Fact]
    public void MovingPackCommandToNewPackRemovesEmptySourcePack()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packLoader = new CommandPackLoader([Path.Combine(directory.Path, "packs")], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        WritePack(packLoader, "source-pack", "source", "command");
        var manager = new CommandOwnershipManager(
            userLoader,
            packLoader,
            stateStore,
            [new LoadedCommandPackInfo("source-pack", true) { Commands = ["source:command"] }],
            [],
            () => { });

        var result = manager.MoveCommandsToNewPack(
            [new CommandOwnershipKey("source-pack", "source:command")],
            "target-pack",
            "target");

        Assert.True(result.Succeeded, result.Message);
        Assert.False(File.Exists(packLoader.GetInstalledPackPath("source-pack")));
        var targetPack = packLoader.ReadInstalledManifest("target-pack");
        var command = Assert.Single(targetPack.Schema.Commands);
        Assert.Equal("target", targetPack.Namespace);
        Assert.Equal("target:command", (command with { Namespace = command.Namespace ?? targetPack.Namespace }).QualifiedName);
    }

    [Fact]
    public void FailedNewPackMoveRefreshRestoresUserCommandAndRemovesNewPack()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packLoader = new CommandPackLoader([Path.Combine(directory.Path, "packs")], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        userLoader.WriteUserSchemaForTransfer(new CommandSchema([],
        [CreateCommand("personal", null, new CommandActionDefinition("default", "", "personal", "https://example.test", []))]));
        var manager = new CommandOwnershipManager(
            userLoader,
            packLoader,
            stateStore,
            [],
            [],
            () => throw new IOException("runtime refresh failed"));

        var result = manager.MoveCommandsToNewPack(
            [new CommandOwnershipKey(CommandOwnershipManager.UserOwnerId, "personal")],
            "personal-pack",
            "personal");

        Assert.False(result.Succeeded);
        Assert.Contains("runtime refresh failed", result.Message);
        Assert.Contains(userLoader.ReadUserSchemaForTransfer().Commands, command => command.QualifiedName == "personal");
        Assert.False(File.Exists(packLoader.GetInstalledPackPath("personal-pack")));
    }

    [Fact]
    public void MovingToExistingPackIdIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "state.json"));
        var packLoader = new CommandPackLoader([Path.Combine(directory.Path, "packs")], stateStore);
        var userLoader = CreateUserLoader(directory.Path);
        userLoader.WriteUserSchemaForTransfer(new CommandSchema([],
        [CreateCommand("personal", null, new CommandActionDefinition("default", "", "personal", "https://example.test", []))]));
        WritePack(packLoader, "existing-pack", "existing", "other");
        var manager = new CommandOwnershipManager(userLoader, packLoader, stateStore, [], [], () => { });

        var preview = manager.PreviewMoveToNewPack(
            [new CommandOwnershipKey(CommandOwnershipManager.UserOwnerId, "personal")],
            "existing-pack",
            "personal");

        Assert.False(preview.CanCommit);
        Assert.Contains(preview.Conflicts, conflict => conflict.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    }

    private static CommandSchemaLoader CreateUserLoader(string directory)
        => new(new CommandSchema([], []),
            Path.Combine(directory, "user-commands.json"),
            Path.Combine(directory, "defaults.json"));

    private static CommandDefinition CreateCommand(
        string name,
        string? commandNamespace,
        CommandActionDefinition action)
        => new(name, "url", name, [action], commandNamespace);

    private static void WritePack(CommandPackLoader loader, string id, string commandNamespace, string commandName)
    {
        Directory.CreateDirectory(loader.GetInstalledPackPath(id) is { } path ? System.IO.Path.GetDirectoryName(path)! : throw new InvalidOperationException());
        loader.WriteInstalledManifest(new CommandPackManifest(id, commandNamespace, new CommandSchema([],
        [CreateCommand(commandName, null, new CommandActionDefinition(
            "default", "", $"{commandNamespace}:{commandName}", "https://example.test", []))])));
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
