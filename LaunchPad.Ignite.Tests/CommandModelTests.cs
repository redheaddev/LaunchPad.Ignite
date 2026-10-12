using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System;
using System.Threading.Tasks;
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
        Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
        File.WriteAllText(userPath + ".legacy-migrated", string.Empty);

        var schema = new CommandSchemaLoader(new CommandSchema([], []), userPath).Load();

        Assert.True(File.Exists(userPath));
        Assert.Empty(schema.Commands);
        Assert.Empty(schema.Lists);
    }

    [Fact]
    public void LoaderMigratesBundledCommandsIntoUserSchemaOnlyOnce()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var defaultsPath = Path.Combine(directory.Path, "defaults.json");
        File.WriteAllText(defaultsPath, """
            {
              "lists": [],
              "variables": [],
              "functions": [],
              "commands": [
                { "name": "legacy-one", "kind": "url", "description": "seed", "actions": [] },
                { "name": "shared", "kind": "url", "description": "seed", "actions": [] }
              ]
            }
            """);
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath, defaultsPath);

        var firstLoad = loader.Load();

        Assert.Equal(2, firstLoad.Commands.Count);
        Assert.Contains(firstLoad.Commands, command => command.Name == "legacy-one");
        Assert.Contains(firstLoad.Commands, command => command.Name == "shared" && command.Description == "seed");

        File.WriteAllText(userPath, """
            {
              "lists": [],
              "variables": [],
              "functions": [],
              "commands": [
                { "name": "shared", "kind": "url", "description": "user edit", "actions": [] }
              ]
            }
            """);
        var secondLoad = loader.Load();

        Assert.Single(secondLoad.Commands);
        Assert.Equal("user edit", Assert.Single(secondLoad.Commands).Description);
    }

    [Fact]
    public void LoaderAddsDefaultsToExistingUserFileWithoutOverwritingMatchingCommands()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var defaultsPath = Path.Combine(directory.Path, "defaults.json");
        File.WriteAllText(userPath, """
            {
              "lists": [],
              "variables": [],
              "functions": [],
              "commands": [
                { "name": "shared", "kind": "url", "description": "user version", "actions": [] }
              ]
            }
            """);
        File.WriteAllText(defaultsPath, """
            {
              "lists": [],
              "variables": [],
              "functions": [],
              "commands": [
                { "name": "legacy-one", "kind": "url", "description": "seed", "actions": [] },
                { "name": "shared", "kind": "url", "description": "seed", "actions": [] }
              ]
            }
            """);
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath, defaultsPath);

        var schema = loader.Load();

        Assert.Equal(2, schema.Commands.Count);
        Assert.Contains(schema.Commands, command => command.Name == "legacy-one");
        Assert.Contains(schema.Commands, command => command.Name == "shared" && command.Description == "user version");
    }

    [Fact]
    public void LoaderRemovesPackedCommandsFromUserFileAndPreservesOtherCommands()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var defaultsPath = Path.Combine(directory.Path, "defaults.json");
        File.WriteAllText(userPath, """
            {
              "lists": [], "variables": [], "functions": [],
              "commands": [
                { "name": "status", "namespace": "tools", "kind": "url", "description": "edited override", "actions": [] },
                { "name": "local", "kind": "url", "description": "keep me", "actions": [] }
              ]
            }
            """);
        File.WriteAllText(defaultsPath, """
            {
              "lists": [], "variables": [], "functions": [],
              "commands": [
                { "name": "status", "namespace": "tools", "kind": "url", "description": "bundled copy", "actions": [] },
                { "name": "legacy", "kind": "url", "description": "keep migration", "actions": [] }
              ]
            }
            """);
        var loader = new CommandSchemaLoader(
            new CommandSchema([], []),
            userPath,
            defaultsPath,
            ["tools:status"]);

        var loaded = loader.Load();
        var persisted = File.ReadAllText(userPath);

        Assert.DoesNotContain(loaded.Commands, command => command.QualifiedName == "tools:status");
        Assert.Contains(loaded.Commands, command => command.Name == "local");
        Assert.Contains(loaded.Commands, command => command.Name == "legacy");
        Assert.DoesNotContain("tools:status", persisted);
        Assert.Contains("local", persisted);
        Assert.Contains("legacy", persisted);
    }

    [Fact]
    public void UserCommandsRemainIndependentWhenNoPackCommandsAreProvided()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        File.WriteAllText(userPath, """
            {
              "lists": [], "variables": [], "functions": [],
              "commands": [
                { "name": "local", "kind": "url", "description": "Local command", "actions": [] }
              ]
            }
            """);

        var loaded = new CommandSchemaLoader(new CommandSchema([], []), userPath, Path.Combine(directory.Path, "missing-defaults.json"))
            .Load();

        Assert.Equal("local", Assert.Single(loaded.Commands).QualifiedName);
        Assert.Contains("local", File.ReadAllText(userPath));
    }

    [Fact]
    public async Task VariableCommandSetsBareAndNamespacedVariablesAndUpdatesExpansion()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath);
        var expander = new CommandTextVariableExpander(loader.Load().Variables);
        var confirmations = new List<string>();
        var model = new VariableCommandModel(
            loader,
            schema => expander.UpdateVariables(schema.Variables),
            name =>
            {
                confirmations.Add(name);
                return Task.FromResult(true);
            });
        var parser = CreateVariableParser();

        var bareResult = await model.ExecuteAsync(ParseVariableCommand(parser, "variable set region west"));
        var updatedBareResult = await model.ExecuteAsync(ParseVariableCommand(parser, "variable set region east"));
        var namespacedResult = await model.ExecuteAsync(ParseVariableCommand(parser, "variable set ado:orgUrl https://example.test"));

        Assert.True(bareResult.Succeeded);
        Assert.True(updatedBareResult.Succeeded);
        Assert.True(namespacedResult.Succeeded);
        Assert.Equal("east", expander.Expand("$region").ExpandedText);
        Assert.Equal("https://example.test", expander.Expand("$ado:orgUrl").ExpandedText);
        Assert.Equal(2, loader.Load().Variables.Count);
        Assert.Equal("east", Assert.Single(loader.Load().Variables, variable => variable.Name == "region").Value);
        Assert.Equal(["region"], confirmations);
    }

    [Fact]
    public async Task VariableCommandKeepsExistingValueWhenOverwriteIsCancelled()
    {
        using var directory = new TemporaryDirectory();
        var loader = new CommandSchemaLoader(new CommandSchema([], []), Path.Combine(directory.Path, "user-commands.json"));
        var parser = CreateVariableParser();
        var original = new VariableCommandModel(loader, _ => { });
        Assert.True(original.Execute(ParseVariableCommand(parser, "variable set region west")).Succeeded);

        var confirmations = 0;
        var model = new VariableCommandModel(loader, _ => { }, _ =>
        {
            confirmations++;
            return Task.FromResult(false);
        });
        var result = await model.ExecuteAsync(ParseVariableCommand(parser, "variable set region east"));

        Assert.False(result.Succeeded);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, confirmations);
        Assert.Equal("west", Assert.Single(loader.Load().Variables).Value);
    }

    [Fact]
    public async Task VariableCommandAddsNewVariableWithoutAskingForConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var loader = new CommandSchemaLoader(new CommandSchema([], []), Path.Combine(directory.Path, "user-commands.json"));
        var confirmations = 0;
        var model = new VariableCommandModel(loader, _ => { }, _ =>
        {
            confirmations++;
            return Task.FromResult(true);
        });

        var result = await model.ExecuteAsync(ParseVariableCommand(CreateVariableParser(), "variable set region west"));

        Assert.True(result.Succeeded);
        Assert.Equal(0, confirmations);
        Assert.Equal("west", Assert.Single(loader.Load().Variables).Value);
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

        Assert.True(model.Execute(ParseVariableCommand(parser, "variable set team:repo https://example.test/repo")).Succeeded);
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

        var result = model.Execute(ParseVariableCommand(CreateVariableParser(), "variable set team:one:repo value"));

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
        File.WriteAllText(userPath + ".legacy-migrated", string.Empty);
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

    [Fact]
    public void LoaderPreservesExtensionVariablesWhenUserVariableIsAdded()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var appSchema = new CommandSchema([], [])
        {
            Variables = [new CommandVariableDefinition("orgUrl", "https://dev.azure.com/example", "ado")]
        };
        File.WriteAllText(userPath + ".legacy-migrated", string.Empty);
        var loader = new CommandSchemaLoader(appSchema, userPath);

        var updatedSchema = loader.SetVariable("team", "alpha");

        Assert.Contains(updatedSchema.Variables, variable =>
            variable.QualifiedName == "ado:orgUrl" && variable.Value == "https://dev.azure.com/example");
        Assert.Contains(updatedSchema.Variables, variable => variable.QualifiedName == "team" && variable.Value == "alpha");
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
    public void UrlCommandModelForwardsNewWindowRequestForParsedCommand()
    {
        var action = new CommandActionDefinition("default", "", "", "https://example.test", []);
        var parsedCommand = new ParsedCommand(
            new CommandDefinition("example", "url", "", [action]),
            action,
            new Dictionary<string, string>());
        var opener = new RecordingUrlTargetOpener();
        var model = new UrlCommandModel(opener);

        var result = model.Execute(parsedCommand, newWindow: true);

        Assert.True(result.Succeeded);
        Assert.True(opener.NewWindowRequested);
        Assert.Equal("https://example.test/", Assert.Single(opener.Targets).Value);
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
    public void UrlCommandModelForwardsNewWindowRequestForDirectUrl()
    {
        var opener = new RecordingUrlTargetOpener();
        var model = new UrlCommandModel(opener);

        var handled = model.TryExecuteDirectUrl("https://example.test", out var result, newWindow: true);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.True(opener.NewWindowRequested);
    }

    [Theory]
    [InlineData("https://example.test --new-window", "https://example.test", true)]
    [InlineData("example --new-window argument", "example  argument", true)]
    [InlineData("example --new-windowish", "example --new-windowish", false)]
    public void TryRemoveNewWindowOptionOnlyRemovesTheOptionToken(string input, string expected, bool expectedRemoved)
    {
        var removed = UrlCommandModel.TryRemoveNewWindowOption(input, out var commandText);

        Assert.Equal(expectedRemoved, removed);
        Assert.Equal(expected, commandText);
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

    [Theory]
    [InlineData(CommandLaunchMode.Elevated, "runas")]
    [InlineData(CommandLaunchMode.DifferentUser, "runasuser")]
    public void ShellCommandModelUsesShellVerbForSpecialLaunchModes(CommandLaunchMode launchMode, string expectedVerb)
    {
        var action = new CommandActionDefinition(
            "default",
            "",
            "",
            "editor.exe",
            [new CommandArgumentDefinition("file"), new CommandArgumentDefinition("option", Optional: true)]);
        var command = new CommandDefinition("editor", "shell", "", [action]);
        var schema = new CommandSchema([], [command]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["shell"]),
            new CommandSchemaValueProvider(schema));
        var parseResult = parser.Parse("editor \"C:\\work area\\file.txt\" \"--name value & whoami\"");
        var starter = new RecordingProcessStarter();
        var model = new ShellCommandModel(starter);

        var result = model.Execute(parseResult.ParsedCommand!, launchMode);

        Assert.True(result.Succeeded);
        Assert.True(starter.StartInfo!.UseShellExecute);
        Assert.Equal(expectedVerb, starter.StartInfo.Verb);
        Assert.Equal("\"C:\\work area\\file.txt\" \"--name value & whoami\"", starter.StartInfo.Arguments);
        Assert.Empty(starter.StartInfo.ArgumentList);
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
        public bool NewWindowRequested { get; private set; }

        public void Open(IEnumerable<UrlTarget> targets, bool newWindow = false)
        {
            Targets.AddRange(targets);
            NewWindowRequested = newWindow;
        }
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
