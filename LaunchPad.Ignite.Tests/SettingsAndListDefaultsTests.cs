using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class SettingsAndListDefaultsTests
{
    [Fact]
    public void ProfileSettingsStore_PersistsValuesAndUsesDefinitionDefault()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new ProfileSettingsStore(path);
        var setting = new CommandSettingDefinition("orgUrl", "Organization URL", "ADO organization", "https://example.test", Namespace: "ado");

        Assert.Equal("https://example.test", store.GetValue(setting.QualifiedName, setting.DefaultValue));
        store.SetValue(setting.QualifiedName, "https://custom.test");

        var reloaded = new ProfileSettingsStore(path);
        Assert.Equal("https://custom.test", reloaded.GetValue(setting.QualifiedName, setting.DefaultValue));
    }

    [Fact]
    public void EnumDefault_IsFirstSuggestionWithoutRemovingOtherValues()
    {
        var schema = new CommandSchema(
            [new CommandListDefinition("enum", "project", ["alpha", "architecture", "beta"], "architecture")],
            []);

        var values = new CommandSchemaValueProvider(schema).GetValues("enum:project");

        Assert.Equal(["architecture", "alpha", "beta"], values);
    }

    [Fact]
    public void EnumDefault_DoesNotFillInTheRequiredArgument()
    {
        var schema = new CommandSchema(
            [new CommandListDefinition("enum", "project", ["alpha", "architecture", "beta"], "architecture")],
            [new CommandDefinition("tool", "url", "", [new CommandActionDefinition(
                "default", "", "tool <enum:project>", "https://example.test", [new CommandArgumentDefinition("project", Ref: "enum:project")])])]);
        var parser = new LaunchPad.Ignite.Core.Parsing.CommandLineParser(
            new LaunchPad.Ignite.Core.Schema.CommandCatalog(schema),
            new CommandSchemaValueProvider(schema));

        var result = parser.Parse("tool");

        Assert.Equal(LaunchPad.Ignite.Core.Parsing.CommandParseStatus.Incomplete, result.Status);
    }

    [Fact]
    public void ApplicationSettingsLoader_ReadsJsonDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "app-settings.json");
        File.WriteAllText(path, "{\"settings\":[{\"name\":\"home\",\"label\":\"Home\",\"description\":\"Home directory\",\"defaultValue\":\"C:\\\\Users\",\"type\":\"directory\"}]}");

        var settings = new ApplicationSettingsLoader(path).Load();

        Assert.Single(settings);
        Assert.Equal("home", settings[0].QualifiedName);
        Assert.Equal("directory", settings[0].Type);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
