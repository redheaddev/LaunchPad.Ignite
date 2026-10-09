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
        var (parser, _) = CreateParser(extensionIds);
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
        var (parser, _) = CreateParser(extensionIds);
        var model = new ExtensionCommandModel(
            stateStore,
            extensionIds,
            () => throw new InvalidOperationException("runtime reload failed"));

        var result = model.Execute(parser.Parse("extension disable weather,ado").ParsedCommand!);

        Assert.False(result.Succeeded);
        Assert.Contains("runtime reload failed", result.Message);
        Assert.True(stateStore.IsEnabled("weather"));
        Assert.False(stateStore.IsEnabled("ado"));
    }

    [Fact]
    public void ExtensionIdSuggestionsUseTheDiscoveredIdList()
    {
        var extensionIds = new[] { "weather", "ado" };
        var (parser, schema) = CreateParser(extensionIds);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema, ["extension"]),
            new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider(extensionIds)));

        var state = session.UpdateInputText("extension disable ");

        Assert.True(state.IsChecklistActive);
        Assert.Equal(extensionIds, state.Items.Select(item => item.Value));

        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("extension disable weather", state.InputText);
        Assert.Contains(state.Items, item => item.Value == "weather" && item.IsSelected);

        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("extension disable weather,ado", state.InputText);
        Assert.All(state.Items, item => Assert.True(item.IsSelected));
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

    private static (CommandLineParser Parser, CommandSchema Schema) CreateParser(IEnumerable<string> extensionIds)
    {
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var valueProvider = new CommandSchemaValueProvider(schema, new ExtensionIdsArgumentValueProvider(extensionIds));
        return (
            new CommandLineParser(new CommandCatalog(schema, ["extension"]), valueProvider),
            schema);
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
