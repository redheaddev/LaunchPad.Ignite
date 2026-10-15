using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ExtensionCommandModelTests
{
    [Fact]
    public void EnableAndDisablePersistExtensionState()
    {
        using var directory = new TemporaryDirectory();
        var statePath = Path.Combine(directory.Path, "extension-state.json");
        var stateStore = new ExtensionStateStore(statePath);
        var extensionIds = new[] { "weather", "ado" };
        var (parser, _) = CreateParser(extensionIds, stateStore: stateStore);
        var refreshCount = 0;
        var model = new ExtensionCommandModel(stateStore, extensionIds, () => refreshCount++);

        var disableResult = parser.Parse("extension disable weather,ado");
        Assert.Equal(CommandParseStatus.Valid, disableResult.Status);
        var executionResult = model.Execute(disableResult.ParsedCommand!);

        Assert.True(executionResult.Succeeded);
        Assert.Equal("Extensions 'weather', 'ado' disabled.", executionResult.Message);
        Assert.Equal(1, refreshCount);
        Assert.False(new ExtensionStateStore(statePath).IsEnabled("weather"));
        Assert.False(new ExtensionStateStore(statePath).IsEnabled("ado"));

        var enableResult = parser.Parse("extension enable weather,ado");
        Assert.Equal(CommandParseStatus.Valid, enableResult.Status);
        Assert.True(model.Execute(enableResult.ParsedCommand!).Succeeded);
        Assert.Equal(2, refreshCount);
        Assert.True(new ExtensionStateStore(statePath).IsEnabled("weather"));
        Assert.True(new ExtensionStateStore(statePath).IsEnabled("ado"));

        var unchangedResult = model.Execute(enableResult.ParsedCommand!);
        Assert.Equal("Extensions 'weather', 'ado' are already enabled.", unchangedResult.Message);
        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void FailedRuntimeRefreshRollsBackPersistedExtensionState()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        var extensionIds = new[] { "weather", "ado" };
        stateStore.SetEnabled("ado", enabled: false);
        var (parser, _) = CreateParser(extensionIds, stateStore: stateStore);
        var model = new ExtensionCommandModel(
            stateStore,
            extensionIds,
            () => throw new InvalidOperationException("runtime reload failed"));

        var result = model.Execute(parser.Parse("extension disable weather").ParsedCommand!);

        Assert.False(result.Succeeded);
        Assert.Contains("runtime reload failed", result.Message);
        Assert.True(stateStore.IsEnabled("weather"));
        Assert.False(stateStore.IsEnabled("ado"));
    }

    [Fact]
    public void EnableDisableSuggestionsIncludeExtensionsAndCommandPacks()
    {
        var extensionIds = new[] { "weather", "ado" };
        var (parser, schema) = CreateParser(extensionIds, ["user-commands"]);
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        stateStore.SetEnabled("weather", enabled: false);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new InstalledComponentIdsArgumentValueProvider(
                extensionIds.Append("user-commands"), stateStore)));

        var state = session.UpdateInputText("extension disable ");

        Assert.True(state.IsChecklistActive);
        Assert.Equal(["ado", "user-commands"], state.Items.Select(item => item.Value));

        state = session.UpdateInputText("extension enable ");
        Assert.True(state.IsChecklistActive);
        Assert.Equal(["weather"], state.Items.Select(item => item.Value));

        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("extension enable weather", state.InputText);
        Assert.Contains(state.Items, item => item.Value == "weather" && item.IsSelected);
    }

    [Fact]
    public void EnableAndDisableCommandPackUsesSharedPersistedState()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        var (parser, _) = CreateParser(["weather"], ["user-commands"], stateStore);
        var model = new ExtensionCommandModel(stateStore, ["weather"], commandPackIds: ["user-commands"]);

        var disable = parser.Parse("extension disable user-commands");
        Assert.Equal(CommandParseStatus.Valid, disable.Status);
        var result = model.Execute(disable.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal("Command pack 'user-commands' disabled.", result.Message);
        Assert.False(new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")).IsEnabled("user-commands"));
    }

    [Fact]
    public async Task PackImportInstallsAndRefreshesRuntime()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        WritePackManifest(sourcePath, "tools", "old", "Old command");
        var packsDirectory = Path.Combine(directory.Path, "profile", "packs");
        var refreshCount = 0;
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            () => refreshCount++,
            commandPackLoader: new CommandPackLoader([packsDirectory]));

        var result = await model.ExecuteAsync(CreatePackInstallCommand(sourcePath));

        Assert.True(result.Succeeded);
        Assert.Equal("Command pack 'tools' installed.", result.Message);
        Assert.True(File.Exists(Path.Combine(packsDirectory, "tools.json")));
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task PackImportSkipsDuplicateCommandsAndKeepsUniqueCommands()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        WritePackManifest(sourcePath, "tools", "new", "New command");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(sourcePath))!;
        manifest["schema"]!["commands"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
            { "name": "existing", "kind": "url", "description": "Duplicate command", "actions": [{ "name": "default", "usage": "tools:existing", "targetTemplate": "https://example.test", "arguments": [] }] }
            """));
        File.WriteAllText(sourcePath, manifest.ToJsonString());
        var packsDirectory = Path.Combine(directory.Path, "profile", "packs");
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            commandPackLoader: new CommandPackLoader([packsDirectory]),
            existingCommandNames: _ => new HashSet<string>(["tools:existing"], StringComparer.OrdinalIgnoreCase));

        var result = await model.ExecuteAsync(CreatePackInstallCommand(sourcePath));
        var loaded = new CommandPackLoader([packsDirectory]).Load();

        Assert.True(result.Succeeded);
        Assert.Contains("Skipped duplicate command(s): tools:existing", result.Message);
        Assert.Contains(loaded.Commands, command => command.QualifiedName == "tools:new");
        Assert.DoesNotContain(loaded.Commands, command => command.QualifiedName == "tools:existing");
    }

    [Fact]
    public async Task PackImportSucceedsWhenAllCommandsAreDuplicates()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        WritePackManifest(sourcePath, "tools", "existing", "Duplicate command");
        var packsDirectory = Path.Combine(directory.Path, "profile", "packs");
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            commandPackLoader: new CommandPackLoader([packsDirectory]),
            existingCommandNames: _ => new HashSet<string>(["tools:existing"], StringComparer.OrdinalIgnoreCase));

        var result = await model.ExecuteAsync(CreatePackInstallCommand(sourcePath));
        var loaded = new CommandPackLoader([packsDirectory]).Load();

        Assert.True(result.Succeeded);
        Assert.Contains("Skipped duplicate command(s): tools:existing", result.Message);
        Assert.Empty(loaded.Commands);
    }

    [Fact]
    public async Task PackImportAsksBeforeReplacementAndPreservesPackWhenDeclined()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        var packsDirectory = Path.Combine(directory.Path, "profile", "packs");
        Directory.CreateDirectory(packsDirectory);
        var installedPath = Path.Combine(packsDirectory, "tools.json");
        WritePackManifest(installedPath, "tools", "old", "Original command");
        WritePackManifest(sourcePath, "tools", "new", "Replacement command");
        var originalContents = File.ReadAllText(installedPath);
        string? promptedId = null;
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            commandPackLoader: new CommandPackLoader([packsDirectory]),
            confirmPackReplacement: id =>
            {
                promptedId = id;
                return Task.FromResult(false);
            });

        var result = await model.ExecuteAsync(CreatePackInstallCommand(sourcePath));

        Assert.False(result.Succeeded);
        Assert.Equal("tools", promptedId);
        Assert.Equal(originalContents, File.ReadAllText(installedPath));
    }

    [Fact]
    public async Task PackImportReplacesOnlyAfterConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "download.json");
        var packsDirectory = Path.Combine(directory.Path, "profile", "packs");
        Directory.CreateDirectory(packsDirectory);
        var installedPath = Path.Combine(packsDirectory, "tools.json");
        WritePackManifest(installedPath, "tools", "old", "Original command");
        WritePackManifest(sourcePath, "tools", "new", "Replacement command");
        var model = new ExtensionCommandModel(
            new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json")),
            [],
            commandPackLoader: new CommandPackLoader([packsDirectory]),
            confirmPackReplacement: _ => Task.FromResult(true));

        var result = await model.ExecuteAsync(CreatePackInstallCommand(sourcePath));
        var loaded = new CommandPackLoader([packsDirectory]).Load();

        Assert.True(result.Succeeded);
        Assert.Contains(loaded.Commands, command => command.Name == "new");
        Assert.DoesNotContain(loaded.Commands, command => command.Name == "old");
    }

    [Fact]
    public void UninstallSuggestionsRemainExtensionOnly()
    {
        var (parser, schema) = CreateParser(["weather"], ["user-commands"]);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new CompositeArgumentValueProvider(
                new ExtensionIdsArgumentValueProvider(["weather"]),
                new InstalledComponentIdsArgumentValueProvider(
                    ["weather", "user-commands"],
                    new ExtensionStateStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))))));

        var state = session.UpdateInputText("extension uninstall ");

        Assert.Equal(["weather"], state.Items.Select(item => item.Value));
    }

    [Fact]
    public void FormatsExtensionsWithVersionAndCurrentPersistedState()
    {
        using var directory = new TemporaryDirectory();
        var stateStore = new ExtensionStateStore(Path.Combine(directory.Path, "extension-state.json"));
        stateStore.SetEnabled("weather", enabled: false);

        var result = ExtensionCommandModel.FormatList(
        [
            new LoadedExtensionInfo("weather", "1.2.3", true),
            new LoadedExtensionInfo("ado", "unknown", true)
        ], stateStore);

        Assert.Equal("ado | unknown [enabled]" + Environment.NewLine + "weather | 1.2.3 [disabled]", result);
    }

    [Fact]
    public void FormatsExtensionsAndCommandPacksTogether()
    {
        var result = ExtensionCommandModel.FormatList(
            [new LoadedExtensionInfo("weather", "1.2.3", true)],
            [new LoadedCommandPackInfo("user-commands", false)]);

        Assert.Equal(
            "user-commands | manifest [pack] [disabled]" + Environment.NewLine +
            "weather | 1.2.3 [extension] [enabled]",
            result);
    }

    private static (CommandLineParser Parser, CommandSchema Schema) CreateParser(
        IEnumerable<string> extensionIds,
        IEnumerable<string>? commandPackIds = null,
        ExtensionStateStore? stateStore = null)
    {
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var valueProvider = new CommandSchemaValueProvider(
            schema,
            new CompositeArgumentValueProvider(
                new ExtensionIdsArgumentValueProvider(extensionIds),
                new InstalledComponentIdsArgumentValueProvider(
                    extensionIds.Concat(commandPackIds ?? []),
                    stateStore ?? new ExtensionStateStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))))));
        return (
            new CommandLineParser(new CommandCatalog(schema, ["extension"]), valueProvider),
            schema);
    }

    private static ParsedCommand CreatePackInstallCommand(string sourcePath)
        => new(
            ExtensionCommandModel.Definition,
            ExtensionCommandModel.Definition.Actions.Single(action => action.Name == "pack-install"),
            new Dictionary<string, string> { ["json-path"] = sourcePath });

    private static void WritePackManifest(string path, string id, string commandName, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            {
              "id": "{{id}}",
              "namespace": "{{id}}",
              "schema": {
                "lists": [],
                "variables": [],
                "commands": [{ "name": "{{commandName}}", "kind": "url", "description": "{{description}}", "actions": [{ "name": "default", "usage": "{{id}}:{{commandName}}", "targetTemplate": "https://example.test", "arguments": [] }] }],
                "functions": []
              }
            }
            """);
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
