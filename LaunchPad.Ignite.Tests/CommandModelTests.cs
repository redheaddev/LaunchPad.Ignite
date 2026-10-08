using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
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
    public void VariableCommandAddsBareAndNamespacedVariablesAndUpdatesExpansion()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath);
        var expander = new CommandTextVariableExpander(loader.Load().Variables);
        var model = new VariableCommandModel(loader, schema => expander.UpdateVariables(schema.Variables));
        var parser = CreateVariableParser();

        var bareResult = model.Execute(ParseVariableCommand(parser, "variable add region west"));
        var namespacedResult = model.Execute(ParseVariableCommand(parser, "variable add ado:orgUrl https://example.test"));

        Assert.True(bareResult.Succeeded);
        Assert.True(namespacedResult.Succeeded);
        Assert.Equal("west", expander.Expand("$region").ExpandedText);
        Assert.Equal("https://example.test", expander.Expand("$ado:orgUrl").ExpandedText);
        Assert.Equal(2, loader.Load().Variables.Count);
    }

    [Fact]
    public void VariableCommandRemovesPersistedVariableAndUpdatesExpansion()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath);
        var expander = new CommandTextVariableExpander(loader.Load().Variables);
        var model = new VariableCommandModel(loader, schema => expander.UpdateVariables(schema.Variables));
        var parser = CreateVariableParser();

        Assert.True(model.Execute(ParseVariableCommand(parser, "variable add team:repo https://example.test/repo")).Succeeded);
        var result = model.Execute(ParseVariableCommand(parser, "variable remove team:repo"));

        Assert.True(result.Succeeded);
        Assert.Empty(loader.Load().Variables);
        Assert.Equal("$team:repo", expander.Expand("$team:repo").ExpandedText);
        Assert.Equal(["team:repo"], expander.Expand("$team:repo").UnresolvedVariables);
    }

    [Fact]
    public void VariableCommandRejectsMalformedNamespacedVariableNames()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath);
        var model = new VariableCommandModel(loader, _ => { });

        var result = model.Execute(ParseVariableCommand(CreateVariableParser(), "variable add team:one:repo value"));

        Assert.False(result.Succeeded);
        Assert.Empty(loader.Load().Variables);
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
            ])
        {
            Variables = [new CommandVariableDefinition("orgUrl", "https://app.test", "ado")]
        };
        File.WriteAllText(userPath, """
            {
              "lists": [{ "type": "enum", "name": "project", "values": ["user-value"] }],
              "variables": [{ "name": "orgUrl", "namespace": "ado", "value": "https://user.test" }],
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
        Assert.Equal("https://user.test", Assert.Single(schema.Variables).Value);
    }

    private static CommandLineParser CreateVariableParser()
    {
        var schema = new CommandSchema([], [VariableCommandModel.Definition]);
        var catalog = new CommandCatalog(schema, ["variable"]);
        return new CommandLineParser(catalog, new CommandSchemaValueProvider(schema));
    }

    private static ParsedCommand ParseVariableCommand(CommandLineParser parser, string input)
    {
        var result = parser.Parse(input);
        Assert.Equal(CommandParseStatus.Valid, result.Status);
        return result.ParsedCommand!;
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

    [Fact]
    public void UrlCommandModelOpensDirectHttpsUrl()
    {
        var opener = new RecordingUrlTargetOpener();
        var model = new UrlCommandModel(opener);

        var handled = model.TryExecuteDirectUrl("https://dev.azure.com/saifcorporation", out var result);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.Equal("Opened URL.", result.Message);
        Assert.Equal("https://dev.azure.com/saifcorporation", Assert.Single(opener.Targets).Value);
    }

    [Fact]
    public void UrlCommandModelDoesNotHandleNonHttpDirectText()
    {
        var opener = new RecordingUrlTargetOpener();
        var model = new UrlCommandModel(opener);

        var handled = model.TryExecuteDirectUrl("mailto:user@example.com", out _);

        Assert.False(handled);
        Assert.Empty(opener.Targets);
    }

    [Fact]
    public void ShellCommandModelPassesQuotedAndHostileValuesAsSeparateArguments()
    {
        var action = new CommandActionDefinition(
            "default",
            "",
            "",
            "editor.exe",
            [new CommandArgumentDefinition("file"), new CommandArgumentDefinition("option", Optional: true)]);
        var command = new CommandDefinition("editor", "shell", "", [action]);
        var schema = new CommandSchema([], [command]);
        var catalog = new CommandCatalog(schema, ["shell"]);
        var parser = new CommandLineParser(catalog, new CommandSchemaValueProvider(schema));
        var parseResult = parser.Parse("editor \"C:\\work area\\file.txt\" \"--name value & whoami\"");
        var starter = new RecordingProcessStarter();
        var model = new ShellCommandModel(starter);

        Assert.Equal(CommandParseStatus.Valid, parseResult.Status);
        var executionResult = model.Execute(parseResult.ParsedCommand!);

        Assert.True(executionResult.Succeeded);
        Assert.Equal("editor.exe", starter.StartInfo!.FileName);
        Assert.False(starter.StartInfo.UseShellExecute);
        Assert.Equal(
            ["C:\\work area\\file.txt", "--name value & whoami"],
            starter.StartInfo.ArgumentList);
    }

    [Fact]
    public void ShellCommandModelReportsProcessStartErrors()
    {
        var action = new CommandActionDefinition("default", "", "", "missing.exe", []);
        var command = new CommandDefinition("missing", "shell", "", [action]);
        var starter = new RecordingProcessStarter(new InvalidOperationException("not found"));
        var model = new ShellCommandModel(starter);

        var result = model.Execute(new ParsedCommand(command, action, new Dictionary<string, string>()));

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void ShellCommandModelExpandsArgumentTemplatesAsSeparateArguments()
    {
        var action = new CommandActionDefinition(
            "default",
            "",
            "",
            "explorer.exe",
            [
                new CommandArgumentDefinition("server"),
                new CommandArgumentDefinition("agent"),
                new CommandArgumentDefinition("work", Optional: true)
            ])
        {
            ArgumentTemplates = [@"\\tfsbld{server}prod\agents\a{agent}\_work\{work}"]
        };
        var command = new CommandDefinition("tfsbld", "shell", "", [action]);
        var schema = new CommandSchema([], [command]);
        var catalog = new CommandCatalog(schema, ["shell"]);
        var parser = new CommandLineParser(catalog, new CommandSchemaValueProvider(schema));
        var parseResult = parser.Parse("tfsbld 12 3");
        var starter = new RecordingProcessStarter();
        var model = new ShellCommandModel(starter);

        Assert.Equal(CommandParseStatus.Valid, parseResult.Status);
        var result = model.Execute(parseResult.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal([@"\\tfsbld12prod\agents\a3\_work\"], starter.StartInfo!.ArgumentList);
    }

    [Fact]
    public void CommandCatalogRejectsUnknownArgumentTemplatePlaceholders()
    {
        var action = new CommandActionDefinition("default", "", "", "tool.exe", [])
        {
            ArgumentTemplates = ["{missing}"]
        };
        var schema = new CommandSchema([], [new CommandDefinition("tool", "shell", "", [action])]);

        var exception = Assert.Throws<ArgumentException>(() => new CommandCatalog(schema, ["shell"]));

        Assert.Contains("unknown placeholder", exception.Message);
    }

    private sealed class RecordingUrlTargetOpener : IUrlTargetOpener
    {
        public List<UrlTarget> Targets { get; } = [];

        public void Open(IEnumerable<UrlTarget> targets)
            => Targets.AddRange(targets);
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        private readonly Exception? _exception;

        public RecordingProcessStarter(Exception? exception = null)
        {
            _exception = exception;
        }

        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            if (_exception is not null)
            {
                throw _exception;
            }
        }
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
