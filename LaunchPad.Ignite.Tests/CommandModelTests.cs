using System.Collections.Generic;
using System.IO;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class CommandModelTests
{
    [Fact]
    public void LoaderCreatesEmptyUserSchemaWhenMissing()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user", "user-commands.json");

        var schema = new CommandSchemaLoader(new CommandSchema([], []), userPath).Load();

        Assert.True(File.Exists(userPath));
        Assert.Empty(schema.Commands);
        Assert.Empty(schema.Lists);
    }

    [Fact]
    public void LoaderMergesUserDefinitionsOverAppDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var appSchema = new CommandSchema(
            [new CommandListDefinition("enum", "project", ["app-value"])],
            [
                new CommandDefinition("pipeline", "url", "app version", []),
                new CommandDefinition("app-only", "url", "app command", [])
            ]);
        File.WriteAllText(userPath, """
            {
              "lists": [{ "type": "enum", "name": "project", "values": ["user-value"] }],
              "commands": [
                { "name": "PIPELINE", "kind": "url", "description": "user version", "actions": [] },
                { "name": "user-only", "kind": "url", "description": "user command", "actions": [] }
              ]
            }
            """);

        var schema = new CommandSchemaLoader(appSchema, userPath).Load();

        Assert.Equal(3, schema.Commands.Count);
        Assert.Equal("user version", Assert.Single(schema.Commands, command => command.Name == "PIPELINE").Description);
        Assert.Contains(schema.Commands, command => command.Name == "app-only");
        Assert.Contains(schema.Commands, command => command.Name == "user-only");
        Assert.Equal("user-value", Assert.Single(schema.Lists).Values[0]);
    }

    [Fact]
    public void UrlCommandModelBuildsAndOpensTargetsFromParsedCommand()
    {
        var action = new CommandActionDefinition(
            "default",
            "",
            "",
            "https://example.test/<project>",
            [new CommandArgumentDefinition("project")]);
        var definition = new CommandDefinition("example", "url", "", [action]);
        var parsedCommand = new ParsedCommand(
            definition,
            action,
            new Dictionary<string, string> { ["project"] = "my project" });
        var opener = new RecordingUrlTargetOpener();
        var model = new UrlCommandModel(opener);

        var result = model.Execute(parsedCommand);

        Assert.True(result.Succeeded);
        Assert.Equal("Opened URL.", result.Message);
        Assert.Equal("https://example.test/my%20project", Assert.Single(opener.Targets).Value);
    }

    private sealed class RecordingUrlTargetOpener : IUrlTargetOpener
    {
        public List<UrlTarget> Targets { get; } = [];

        public void Open(IEnumerable<UrlTarget> targets)
            => Targets.AddRange(targets);
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
