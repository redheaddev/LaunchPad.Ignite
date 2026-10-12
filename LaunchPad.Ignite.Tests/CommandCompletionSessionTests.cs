using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;
using LaunchPad.Ignite.App.Commands;

namespace LaunchPad.Ignite.Tests;

public class CommandCompletionSessionTests
{
    [Fact]
    public void TabCompletesCommandAndEnumWithoutAddingOptionalArgumentSpace()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider());

        var commandState = session.UpdateInputText("pipe");
        Assert.Equal("line", commandState.GhostText);

        var argumentState = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("pipeline ", argumentState.InputText);
        Assert.Equal("claims", argumentState.GhostText);

        session.UpdateInputText("pipeline cl");
        var projectState = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("pipeline claims", projectState.InputText);
        Assert.False(projectState.InputText.EndsWith(' '));
    }

    [Fact]
    public void CommandSuggestionsAreFilteredByTypedPrefix()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider());

        var state = session.UpdateInputText("a");

        Assert.Empty(state.Items);
        Assert.Empty(state.GhostText);
        Assert.False(state.CanComplete);
    }

    [Fact]
    public void PageAndBoundaryKeysMoveTheHighlightedCompletion()
    {
        var schema = new CommandSchema(
            [],
            Enumerable.Range(0, 12)
                .Select(index => new CommandDefinition(
                    $"command{index:D2}",
                    "url",
                    string.Empty,
                    [new CommandActionDefinition("default", string.Empty, string.Empty, "https://example.test", [])]))
                .ToArray());
        var session = new CommandCompletionSession(
            new CommandCatalog(schema),
            new CommandSchemaValueProvider(schema));

        var state = session.HandleKey(CommandInputKey.PageDown);
        Assert.Equal("command05", state.Items.Single(item => item.IsHighlighted).Value);

        state = session.HandleKey(CommandInputKey.PageUp);
        Assert.Equal("command00", state.Items.Single(item => item.IsHighlighted).Value);

        state = session.HandleKey(CommandInputKey.Last);
        Assert.Equal("command11", state.Items.Single(item => item.IsHighlighted).Value);

        state = session.HandleKey(CommandInputKey.First);
        Assert.Equal("command00", state.Items.Single(item => item.IsHighlighted).Value);
    }

    [Fact]
    public void NamespacedCommandSuggestionsMatchNamespaceOrCommandName()
    {
        var schema = new CommandSchema(
            [],
            [
                new CommandDefinition("pipeline", "url", "", [new CommandActionDefinition("default", "", "", "https://example.test", [])], "ado"),
                new CommandDefinition("status", "url", "", [new CommandActionDefinition("default", "", "", "https://example.test", [])], "git")
            ]);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema),
            new CommandSchemaValueProvider(schema));

        var state = session.UpdateInputText("pipe");
        Assert.Equal(["ado:pipeline"], state.Items.Select(item => item.Value));
        Assert.Equal("line", state.GhostText);

        state = session.UpdateInputText("git");
        Assert.Equal(["git:status"], state.Items.Select(item => item.Value));
        Assert.Equal(":status", state.GhostText);
    }

    [Fact]
    public void CommandNamePrefixShowsGhostTextWithoutNamespacePrefix()
    {
        var schema = new CommandSchema(
            [],
            [new CommandDefinition(
                "artifacts",
                "url",
                string.Empty,
                [new CommandActionDefinition("default", string.Empty, string.Empty, "https://example.test", [])],
                "ado")]);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema),
            new CommandSchemaValueProvider(schema));

        var state = session.UpdateInputText("ar");

        Assert.Equal(["ado:artifacts"], state.Items.Select(item => item.Value));
        Assert.Equal("tifacts", state.GhostText);
    }

    [Fact]
    public void ExactCommandPrefixStillShowsLongerMatchingCommands()
    {
        var schema = new CommandSchema(
            [],
            [
                new CommandDefinition("tfs", "url", "", [new CommandActionDefinition("default", "", "", "https://example.test", [])]),
                new CommandDefinition("tfsbld", "shell", "", [new CommandActionDefinition("default", "", "", "explorer.exe", [])])
            ]);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema, ["shell"]),
            new CommandSchemaValueProvider(schema));

        var state = session.UpdateInputText("tfs");

        Assert.Equal(["tfs", "tfsbld"], state.Items.Select(item => item.Value));
    }

    [Fact]
    public void TabCompletesNamespacedCommandUsingQualifiedName()
    {
        var schema = TestCommandSchemas.Pipeline;
        var namespacedSchema = schema with
        {
            Commands = [schema.Commands[0] with { Namespace = "ns" }]
        };
        var catalog = new CommandCatalog(namespacedSchema);
        var session = new CommandCompletionSession(catalog, new CommandSchemaValueProvider(namespacedSchema));

        var state = session.UpdateInputText("ns:pipe");
        Assert.Equal("line", state.GhostText);

        state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("ns:pipeline ", state.InputText);
        Assert.Equal("claims", state.GhostText);
    }

    [Fact]
    public void VariableSuggestionsAreFilteredAndCanBeAcceptedWithKeyboard()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider(),
            [
                new CommandVariableDefinition("orgUrl", "https://dev.azure.com/example", "ado"),
                new CommandVariableDefinition("account", "saif"),
                new CommandVariableDefinition("project", "architecture")
            ]);

        var state = session.UpdateInputText("$a");

        Assert.Equal(["$ado:orgUrl", "$account"], state.Items.Select(item => item.Value));
        Assert.Equal(
            ["$ado:orgUrl = https://dev.azure.com/example", "$account = saif"],
            state.Items.Select(item => item.DisplayText));
        Assert.Equal("do:orgUrl", state.GhostText);

        state = session.HandleKey(CommandInputKey.Down);
        Assert.Contains(state.Items, item => item.Value == "$account" && item.IsHighlighted);
        state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("$account", state.InputText);
    }

    [Fact]
    public void VariableSuggestionsRefreshForVariablesAddedAtRuntime()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider());
        Assert.Empty(session.UpdateInputText("$").Items);

        var state = session.UpdateVariables(
        [
            new CommandVariableDefinition("project", "architecture"),
            new CommandVariableDefinition("orgUrl", "https://dev.azure.com/example", "ado")
        ]);

        Assert.Equal(["$project", "$ado:orgUrl"], state.Items.Select(item => item.Value));
        Assert.Equal(["$project = architecture", "$ado:orgUrl = https://dev.azure.com/example"],
            state.Items.Select(item => item.DisplayText));
    }

    [Fact]
    public void VariableSuggestionsWorkInsideDirectUrlTokens()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider(),
            [new CommandVariableDefinition("project", "architecture")]);

        var state = session.UpdateInputText("https://dev.azure.com/saifcorporation/$pro");

        Assert.Equal(["$project"], state.Items.Select(item => item.Value));
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("https://dev.azure.com/saifcorporation/$project", state.InputText);
    }

    [Fact]
    public void FunctionSuggestionsUseAtPrefixShowSignatureAndInsertCallParenthesis()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider(),
            functions:
            [
                new CommandFunctionDefinition(
                    "forecast",
                    null,
                    [new("zip"), new("days", Optional: true)],
                    Namespace: "weather"),
                new CommandFunctionDefinition("temp", null, [new("zip")], Namespace: "weather")
            ]);

        var state = session.UpdateInputText("open https://example.test/@weather:fo");

        var item = Assert.Single(state.Items);
        Assert.Equal("@weather:forecast(", item.Value);
        Assert.Equal("@weather:forecast(zip, [days])", item.DisplayText);
        Assert.Equal("recast(", state.GhostText);

        state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("open https://example.test/@weather:forecast(", state.InputText);
    }

    [Fact]
    public void ExactCommandWaitsForSeparatorBeforeShowingNextArgumentSuggestion()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.Pipeline),
            TestCommandSchemas.CreatePipelineProvider());

        var state = session.UpdateInputText("pipeline");

        Assert.Empty(state.GhostText);
        Assert.Empty(state.Items);
        Assert.True(state.CanComplete);

        state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("pipeline ", state.InputText);
        Assert.Equal("claims", state.GhostText);

        state = session.UpdateInputText("pipeline ");
        Assert.Equal("claims", state.GhostText);
    }

    [Fact]
    public void NormalListChecklistCanSelectAndUnselectMultipleValues()
    {
        var session = CreateSecretListSession();
        session.UpdateInputText("secret shell ");

        Assert.True(session.State.IsChecklistActive);
        Assert.Equal("token1", session.State.GhostText);

        var state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1", state.InputText);
        Assert.Equal(",token2", state.GhostText);
        Assert.Contains(state.Items, item => item.Value == "token1" && item.IsSelected);

        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1,token2", state.InputText);
        Assert.Equal(",token3", state.GhostText);

        session.HandleKey(CommandInputKey.Up);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1", state.InputText);
        Assert.Equal(",token2", state.GhostText);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1,token2", state.InputText);
    }

    [Fact]
    public void AcceptingSingleListItemWithSpacesWrapsItInQuotes()
    {
        var session = CreateListSession(
            ListSelectionMode.Single,
            ["team project", "other"]);
        session.UpdateInputText("list ");

        var state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("list \"team project\"", state.InputText);
        Assert.False(state.IsChecklistActive);
    }

    [Fact]
    public void AcceptingMultipleListItemsWithSpacesQuotesEachCsvValue()
    {
        var session = CreateListSession(
            ListSelectionMode.Multiple,
            ["team project", "other value"]);
        session.UpdateInputText("list ");

        var state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("list \"team project\"", state.InputText);
        state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("list \"team project\",\"other value\"", state.InputText);
    }

    [Fact]
    public void TypingAsteriskImmediatelyShowsSecretChecklist()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(new CommandSchema([], [SecretListCommandDefinition.Definition]), ["secretList"]),
            TestCommandSchemas.CreateSecretListProvider());

        var state = session.UpdateInputText("*");

        Assert.True(state.IsChecklistActive);
        Assert.Equal(["token1", "token2", "token3"], state.Items.Select(item => item.Value));

        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("* token1", state.InputText);
        Assert.True(state.IsChecklistActive);
    }

    [Fact]
    public void ThemeCommandProvidesSingleChoiceListOfSavedThemeNames()
    {
        using var directory = new TemporaryDirectory();
        var store = new LaunchPad.Ignite.App.Services.ProfileSettingsStore(
            System.IO.Path.Combine(directory.Path, "settings.json"));
        store.SetTheme(LaunchPad.Ignite.App.Services.ColorThemeService.ToProfileTheme(
            LaunchPad.Ignite.App.Services.ColorTheme.Default with { Id = "custom-night", Name = "Night" }));
        var session = new CommandCompletionSession(
            new CommandCatalog(new CommandSchema([], [ThemeCommandDefinition.Definition]), ["theme"]),
            new LaunchPad.Ignite.App.Services.ThemeNamesArgumentValueProvider(store));

        var state = session.UpdateInputText("theme ");

        Assert.False(state.IsChecklistActive);
        Assert.Equal("Default", state.GhostText);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("theme Default", state.InputText);
        Assert.False(state.IsChecklistActive);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void SelectAllIncludesValuesHiddenByCurrentFilter()
    {
        var session = CreateSecretListSession();
        session.UpdateInputText("secret shell tok1");

        var state = session.HandleKey(CommandInputKey.SelectAll);

        Assert.Equal("secret shell token1,token2,token3", state.InputText);
        Assert.All(state.Items, item => Assert.True(item.IsSelected));

        state = session.HandleKey(CommandInputKey.DeselectAll);
        Assert.Equal("secret shell ", state.InputText);
    }

    [Fact]
    public void MinimalListAcceptsOneValueWithoutSuggestingAdditionalValues()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.SecretList(ListSelectionMode.Single)),
            TestCommandSchemas.CreateSecretListProvider());
        session.UpdateInputText("secret shell ");

        var state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("secret shell token1", state.InputText);
        Assert.Empty(state.GhostText);
        Assert.False(state.IsChecklistActive);
        Assert.DoesNotContain(state.Items, item => item.Value == "token1");
        Assert.Empty(state.Items);
    }

    private static CommandCompletionSession CreateSecretListSession()
        => new(
            new CommandCatalog(TestCommandSchemas.SecretList()),
            TestCommandSchemas.CreateSecretListProvider());

    private static CommandCompletionSession CreateListSession(ListSelectionMode mode, IReadOnlyList<string> values)
    {
        var schema = new CommandSchema(
            [],
            [new CommandDefinition("list", "url", "", [new CommandActionDefinition(
                "default", "", "list <list:values>", "https://example.test", [new CommandArgumentDefinition("values", Ref: "list:values", ListMode: mode)])])]);
        return new CommandCompletionSession(
            new CommandCatalog(schema),
            new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
            {
                ["list:values"] = values
            }));
    }
}
