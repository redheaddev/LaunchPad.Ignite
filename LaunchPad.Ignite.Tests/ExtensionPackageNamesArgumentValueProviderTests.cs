using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ExtensionPackageNamesArgumentValueProviderTests
{
    [Fact]
    public void RequiredInstallArgumentSuggestsPublishedPackageNames()
    {
        using var directory = new TemporaryDirectory();
        var packageDirectory = Path.Combine(directory.Path, "packages");
        Directory.CreateDirectory(packageDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "media-1.0.0.zip"), string.Empty);
        File.WriteAllText(Path.Combine(packageDirectory, "ado-1.0.1.zip"), string.Empty);
        var schema = new CommandSchema([], [ExtensionCommandModel.Definition]);
        var packageManager = new ExtensionPackageManager(
            Path.Combine(directory.Path, "extensions"),
            packageDirectory);
        var valueProvider = new CommandSchemaValueProvider(
            schema,
            new ExtensionPackageNamesArgumentValueProvider(packageManager));
        var catalog = new CommandCatalog(schema, ["extension"]);
        var session = new CommandCompletionSession(catalog, valueProvider);
        var parser = new CommandLineParser(catalog, valueProvider);

        var state = session.UpdateInputText("extension install ");

        Assert.True(state.IsChecklistActive);
        Assert.Equal(["ado-1.0.1", "media-1.0.0"], state.Items.Select(item => item.Value));
        state = session.HandleKey(CommandInputKey.Tab);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("extension install ado-1.0.1,media-1.0.0", state.InputText);
        Assert.Equal(CommandParseStatus.Incomplete, parser.Parse("extension install").Status);
        Assert.Equal(CommandParseStatus.Valid, parser.Parse(state.InputText).Status);
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
