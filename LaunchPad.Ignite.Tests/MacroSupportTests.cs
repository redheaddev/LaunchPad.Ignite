using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class MacroSupportTests
{
    [Fact]
    public void StorePersistsAndFindsNamespacedMacros()
    {
        using var directory = new TemporaryDirectory();
        var store = new MacroStore(directory.Path);
        var macro = CreateMacro("deploy", "ops", [], "url https://example.test");

        var saved = store.Save(macro);

        Assert.Equal("ops:deploy", saved.Metadata.QualifiedName);
        Assert.Equal(saved.Metadata.Id, store.Find("ops:deploy")!.Metadata.Id);
        Assert.Equal("url https://example.test", store.Find("OPS:DEPLOY")!.Script);
        Assert.True(File.Exists(Path.Combine(directory.Path, "ops", "deploy", "metadata.json")));
        Assert.True(store.Remove("ops:deploy"));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void StoreRejectsInvalidMacroDefinitions()
    {
        using var directory = new TemporaryDirectory();
        var store = new MacroStore(directory.Path);

        Assert.Throws<ArgumentException>(() => store.Save(CreateMacro("bad name", null, [], "url https://example.test")));
        Assert.Throws<ArgumentException>(() => store.Save(CreateMacro("deploy", "bad namespace", [], "url https://example.test")));
        Assert.Throws<ArgumentException>(() => store.Save(CreateMacro(
            "deploy",
            null,
            [new MacroArgumentDefinition("arg", MacroArgumentType.List)],
            "url https://example.test")));
    }

    [Fact]
    public void ArgumentBinderSupportsQuotedNamedAndPositionalTypedValues()
    {
        var provider = new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
        {
            ["list:secrets"] = ["azdo_token", "github_token"],
            ["list:environments"] = ["dev", "prod"]
        });
        var binder = new MacroArgumentBinder(provider);
        var macro = CreateMacro("release", null,
        [
            new MacroArgumentDefinition("secrets", MacroArgumentType.SecretList),
            new MacroArgumentDefinition("project", MacroArgumentType.Variable),
            new MacroArgumentDefinition("label", MacroArgumentType.Value),
            new MacroArgumentDefinition("ids", MacroArgumentType.ValueList),
            new MacroArgumentDefinition("environment", MacroArgumentType.List, "environments", ListSelectionMode.Single)
        ], "url https://example.test");
        var variables = new[] { new CommandVariableDefinition("project", "team project") };

        var result = binder.Bind(
            "azdo_token,github_token $project \"value with spaces\" 1,2,3 environment=prod",
            macro,
            variables);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("azdo_token,github_token", result.Arguments["secrets"]);
        Assert.Equal("team project", result.Arguments["project"]);
        Assert.Equal("value with spaces", result.Arguments["label"]);
        Assert.Equal("1,2,3", result.Arguments["ids"]);
        Assert.Equal("prod", result.Arguments["environment"]);
    }

    [Fact]
    public void ArgumentBinderRejectsUnknownNamesDuplicatesAndInvalidListValues()
    {
        var binder = new MacroArgumentBinder(new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
        {
            ["list:secrets"] = ["token"]
        }));
        var macro = CreateMacro("deploy", null,
            [new MacroArgumentDefinition("token", MacroArgumentType.SecretList, ListMode: ListSelectionMode.Single)],
            "url https://example.test");

        Assert.False(binder.Bind("unknown=value", macro, []).Succeeded);
        Assert.False(binder.Bind("token=token token", macro, []).Succeeded);
        Assert.False(binder.Bind("missing", macro, []).Succeeded);
    }

    [Fact]
    public void MacroArgumentDefinitionParserReadsTypesListsAndCardinality()
    {
        var parsed = MacroArgumentDefinitionParser.TryParse(
            "arg1:secretList multiple\narg2:variable\narg3:value\narg4:valueList\narg5:list build-targets single",
            out var arguments,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal(5, arguments.Count);
        Assert.Equal(MacroArgumentType.List, arguments[4].Type);
        Assert.Equal("build-targets", arguments[4].ListName);
        Assert.Equal(ListSelectionMode.Single, arguments[4].ListMode);
    }

    [Fact]
    public void CompletionOffersPercentPrefixedNamespacedMacrosAndSecretArguments()
    {
        var macro = CreateMacro("deploy", "ops",
            [new MacroArgumentDefinition("token", MacroArgumentType.SecretList, ListMode: ListSelectionMode.Single)],
            "url https://example.test");
        var provider = new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
        {
            ["list:secrets"] = ["azdo_token", "github_token"]
        });
        var session = new MacroCompletionSession([macro], provider, [], []);

        var allMacrosState = session.UpdateInputText("%");
        Assert.True(allMacrosState.CanComplete);
        Assert.Contains(allMacrosState.Items, item => item.Value == "%ops:deploy");

        var nameState = session.UpdateInputText("%ops:dep");
        Assert.True(nameState.CanComplete);
        Assert.Contains(nameState.Items, item => item.Value == "%ops:deploy");
        var acceptedName = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("%ops:deploy ", acceptedName.InputText);

        var argumentState = session.UpdateInputText(acceptedName.InputText);
        Assert.Contains(argumentState.Items, item => item.Value == "azdo_token");
        Assert.Contains(argumentState.Items, item => item.Value == "github_token");
    }

    [Fact]
    public void UsageIncludesPercentPrefixedMacroNameAndRequiredArguments()
    {
        var macro = CreateMacro("deploy", "ops",
        [
            new MacroArgumentDefinition("project", MacroArgumentType.Variable),
            new MacroArgumentDefinition("pipelineId", MacroArgumentType.Value)
        ], "url https://example.test");
        var session = new MacroCompletionSession(
            [macro],
            new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>()),
            [],
            []);

        Assert.True(session.TryGetUsageText("%ops:deploy project=sample", out var usage));
        Assert.Equal("%ops:deploy <project> <pipelineId>", usage);
        Assert.False(session.TryGetUsageText("%ops:unknown", out _));
    }

    [Fact]
    public void SecretAccessDetectorFindsSecretArgumentsAndIgnoresOtherCommands()
    {
        var urlCommand = new CommandDefinition(
            "url",
            "url",
            "Open a URL",
            [new CommandActionDefinition("default", "Open a URL", "url <target>", "url", [])]);
        var catalog = new CommandCatalog(
            new CommandSchema([], [SecretCommandDefinition.Definition, urlCommand]),
            ["secret", "url"]);
        var detector = new MacroSecretAccessDetector(catalog);

        Assert.True(detector.UsesSecrets("secret show token"));
        Assert.False(detector.UsesSecrets("url https://example.test"));
    }

    [Fact]
    public void CompletionLoadsNamespacedMacrosFromNestedStoreDirectories()
    {
        using var directory = new TemporaryDirectory();
        var store = new MacroStore(directory.Path);
        store.Save(CreateMacro("recording", "qms", [], "url https://example.test"));
        var session = new MacroCompletionSession(
            store.GetAll(),
            new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>()),
            [],
            []);

        var state = session.UpdateInputText("%");

        Assert.Contains(state.Items, item => item.Value == "%qms:recording");
    }

    [Fact]
    public async Task ExecutionEngineHonorsBreakPolicyAndForwardsInvocationMode()
    {
        var macro = CreateMacro("deploy", null, [], "first\nsecond\nthird");
        var receivedLines = new List<string>();
        var engine = new MacroExecutionEngine();

        var result = await engine.ExecuteAsync(
            macro,
            new Dictionary<string, string>(),
            newWindow: true,
            CommandLaunchMode.Normal,
            (line, _, newWindow, _) =>
            {
                receivedLines.Add(line);
                Assert.True(newWindow);
                return Task.FromResult(new CommandExecutionResult(line != "second", line == "second" ? "failed" : "ok"));
            });

        Assert.False(result.Succeeded);
        Assert.Equal(["first", "second"], receivedLines);
        Assert.Contains("Line 2", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionEngineContinuesAndReportsFailuresWhenConfigured()
    {
        var original = CreateMacro("deploy", null, [], "first\nsecond\nthird");
        var macro = original with { Metadata = original.Metadata with { OnError = MacroErrorBehavior.Continue } };
        var receivedLines = new List<string>();
        var engine = new MacroExecutionEngine();

        var result = await engine.ExecuteAsync(
            macro,
            new Dictionary<string, string>(),
            newWindow: false,
            CommandLaunchMode.Normal,
            (line, _, _, _) =>
            {
                receivedLines.Add(line);
                return Task.FromResult(new CommandExecutionResult(line != "second", line == "second" ? "failed" : "ok"));
            });

        Assert.False(result.Succeeded);
        Assert.Equal(["first", "second", "third"], receivedLines);
        Assert.Contains("continued after errors", result.Message, StringComparison.Ordinal);
    }

    private static MacroDefinition CreateMacro(
        string name,
        string? macroNamespace,
        IReadOnlyList<MacroArgumentDefinition> arguments,
        string script)
        => new(new MacroMetadata(Guid.Empty.ToString("N"), name, macroNamespace, arguments, MacroErrorBehavior.Break), script);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
