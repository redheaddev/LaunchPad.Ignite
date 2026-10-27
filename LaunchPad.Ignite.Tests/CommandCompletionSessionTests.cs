using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Services.Completion;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.Services.Themes;

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
    public void RightAcceptsOneCommandTokenAndPreservesRemainingInput()
    {
        var schema = new CommandSchema(
            [],
            [new CommandDefinition("extension", "url", "", [new CommandActionDefinition("default", "", "", "https://example.test", [])])]);
        var session = new CommandCompletionSession(new CommandCatalog(schema), new CommandSchemaValueProvider(schema));
        session.UpdateInputText("ex install media-1.0.1,ado-1.0.1");

        var state = session.HandleKey(CommandInputKey.Right);

        Assert.Equal("extension install media-1.0.1,ado-1.0.1", state.InputText);
        Assert.Equal("extension ", state.InputText[..state.CaretIndex!.Value]);
    }

    [Fact]
    public void RightAcceptsNextTokenFromWholeLineSuggestion()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandText = "ext";

        viewModel.HandleInputKeyAsync(CommandInputKey.Right).GetAwaiter().GetResult();

        Assert.Equal("extension ", viewModel.CommandText);
    }

    [Fact]
    public async Task TabCompletionCanBeUndoneToThePreviousInput()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "ext";

        await viewModel.HandleInputKeyAsync(CommandInputKey.Tab);

        Assert.NotEqual("ext", viewModel.CommandInputText);
        Assert.True(viewModel.UndoCommandInput());
        Assert.Equal("ext", viewModel.CommandInputText);
    }

    [Fact]
    public async Task UndoTabRestoresTheCaretPositionBeforeCompletion()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "ext";
        viewModel.SetCommandInputCaretIndex(1);

        await viewModel.HandleInputKeyAsync(CommandInputKey.Tab);

        Assert.True(viewModel.UndoCommandInput(out var caretIndex));
        Assert.Equal("ext", viewModel.CommandInputText);
        Assert.Equal(1, caretIndex);
    }

    [Fact]
    public void MultipleInputChangesCanBeUndoneToTheBeginning()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "a";
        viewModel.CommandInputText = "ab";
        viewModel.CommandInputText = "abc";

        Assert.True(viewModel.UndoCommandInput());
        Assert.Equal("ab", viewModel.CommandInputText);
        Assert.True(viewModel.UndoCommandInput());
        Assert.Equal("a", viewModel.CommandInputText);
        Assert.True(viewModel.UndoCommandInput());
        Assert.Equal(string.Empty, viewModel.CommandInputText);
        Assert.False(viewModel.UndoCommandInput());
    }

    [Fact]
    public void UndoCanBeRedone()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "a";
        viewModel.CommandInputText = "ab";

        Assert.True(viewModel.UndoCommandInput());
        Assert.Equal("a", viewModel.CommandInputText);
        Assert.True(viewModel.RedoCommandInput());
        Assert.Equal("ab", viewModel.CommandInputText);
        Assert.False(viewModel.RedoCommandInput());
    }

    [Fact]
    public void RedoRestoresCaretPosition()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "a";
        viewModel.SetCommandInputCaretIndex(1);
        viewModel.CommandInputText = "ab";
        viewModel.SetCommandInputCaretIndex(2);

        Assert.True(viewModel.UndoCommandInput(out var undoCaretIndex));
        Assert.Equal(1, undoCaretIndex);
        Assert.True(viewModel.RedoCommandInput(out var redoCaretIndex));
        Assert.Equal("ab", viewModel.CommandInputText);
        Assert.Equal(2, redoCaretIndex);
    }

    [Fact]
    public void NewInputAfterUndoClearsRedoHistory()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "a";
        viewModel.CommandInputText = "ab";

        Assert.True(viewModel.UndoCommandInput());
        viewModel.CommandInputText = "ac";

        Assert.False(viewModel.RedoCommandInput());
    }

    [Theory]
    [InlineData(CommandInputKey.Enter)]
    [InlineData(CommandInputKey.Escape)]
    [InlineData(CommandInputKey.Clear)]
    public async Task SessionBoundaryKeyResetsTheUndoHistory(CommandInputKey boundaryKey)
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "not-a-command";

        await viewModel.HandleInputKeyAsync(boundaryKey);

        Assert.False(viewModel.UndoCommandInput());
        Assert.False(viewModel.RedoCommandInput());

        var nextSessionStart = viewModel.CommandInputText;
        viewModel.CommandInputText = $"{nextSessionStart} next";

        Assert.True(viewModel.UndoCommandInput());
        Assert.Equal(nextSessionStart, viewModel.CommandInputText);
        Assert.False(viewModel.UndoCommandInput());
    }

    [Fact]
    public async Task ClickingCompletionAcceptsTabTextAndRequiredArgumentSpace()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "ext";
        var completionIndex = viewModel.CompletionItems.ToList()
            .FindIndex(item => item.DisplayText == "extension");

        Assert.True(completionIndex >= 0);
        await viewModel.AcceptCompletionAtIndexAsync(completionIndex);

        Assert.Equal("extension ", viewModel.CommandInputText);
    }

    [Fact]
    public void CommandMetadataFilterPrefixIsRemovedFromCommandInput()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();

        viewModel.CommandInputText = ">kind:url pipeline";

        Assert.Equal("pipeline", viewModel.CommandText);
        Assert.Equal(">kind:url pipeline", viewModel.CommandInputText);
    }

    [Fact]
    public void CommandMetadataFilterRemainsActiveWhenCommandInputIsCleared()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = ">kind:system ";

        Assert.Equal(string.Empty, viewModel.CommandText);
        Assert.Equal(">kind:system ", viewModel.CommandInputText);
        Assert.NotEmpty(viewModel.CompletionGhostText);
        Assert.Contains(viewModel.CompletionItems, item => item.DisplayText == "exit");

        viewModel.HandleInputKeyAsync(CommandInputKey.Clear).GetAwaiter().GetResult();

        Assert.Equal(string.Empty, viewModel.CommandText);
        Assert.Equal(">kind:system ", viewModel.CommandInputText);
        Assert.Contains(viewModel.CompletionItems, item => item.DisplayText == "exit");
    }

    [Fact]
    public void RemovingCommandMetadataFilterRestoresUnfilteredCommands()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        var unfilteredCommandCount = viewModel.CompletionItems.Count;
        viewModel.CommandInputText = ">kind:url ";

        var filteredCommandCount = viewModel.CompletionItems.Count;
        Assert.True(filteredCommandCount < unfilteredCommandCount);

        viewModel.CommandInputText = string.Empty;

        Assert.Equal(unfilteredCommandCount, viewModel.CompletionItems.Count);
    }

    [Fact]
    public void KindFilterSuggestionsAppearAtColonAndTabOrSpaceAcceptsTheSelectedKind()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = ">kind:";

        Assert.NotEmpty(viewModel.CompletionItems);
        Assert.Single(viewModel.CompletionItems.Where(item => item.IsHighlighted));
        var kind = viewModel.CompletionItems.Single(item => item.IsHighlighted).DisplayText;

        viewModel.CommandInputText = $">kind:{kind}";
        viewModel.HandleInputKeyAsync(CommandInputKey.Tab).GetAwaiter().GetResult();

        Assert.Equal($">kind:{kind} ", viewModel.CommandInputText);
        Assert.Equal(string.Empty, viewModel.CommandText);
        Assert.NotEmpty(viewModel.CompletionGhostText);

        viewModel.CommandInputText = $">kind:{kind}";
        viewModel.HandleInputKeyAsync(CommandInputKey.Space).GetAwaiter().GetResult();

        Assert.Equal($">kind:{kind} ", viewModel.CommandInputText);
        Assert.Equal(string.Empty, viewModel.CommandText);
        Assert.NotEmpty(viewModel.CompletionGhostText);
    }

    [Fact]
    public void FilterFieldSuggestionsAreShownAndTabOrRightAcceptsFieldWithColon()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = ">";

        Assert.Equal(["kind", "pack", "extension"], viewModel.CompletionItems.Select(item => item.DisplayText));
        Assert.Equal("kind", viewModel.CompletionGhostText);

        viewModel.HandleInputKeyAsync(CommandInputKey.Tab).GetAwaiter().GetResult();
        Assert.Equal(">kind:", viewModel.CommandInputText);

        viewModel.CommandInputText = ">p";
        Assert.Equal(["pack"], viewModel.CompletionItems.Select(item => item.DisplayText));
        viewModel.HandleInputKeyAsync(CommandInputKey.Right).GetAwaiter().GetResult();
        Assert.Equal(">pack:", viewModel.CommandInputText);

        viewModel.CommandInputText = ">kind:";
        Assert.NotEmpty(viewModel.CompletionItems);
    }

    [Fact]
    public void FilterValueSuggestionsStartAtFirstItemAfterFieldSuggestions()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = ">";
        viewModel.HandleInputKeyAsync(CommandInputKey.Down).GetAwaiter().GetResult();

        viewModel.CommandInputText = ">kind:";

        Assert.NotEmpty(viewModel.CompletionItems);
        Assert.True(viewModel.CompletionItems[0].IsHighlighted);
        Assert.Single(viewModel.CompletionItems.Where(item => item.IsHighlighted));
    }

    [Fact]
    public void RightAcceptsMetadataFilterValueGhostText()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = ">kind:sy";

        Assert.Equal("stem", viewModel.CompletionGhostText);
        viewModel.HandleInputKeyAsync(CommandInputKey.Right).GetAwaiter().GetResult();

        Assert.Equal(">kind:system ", viewModel.CommandInputText);
        Assert.Equal(string.Empty, viewModel.CommandText);
        Assert.NotEmpty(viewModel.CompletionGhostText);
    }

    [Fact]
    public void RightAcceptsOneArgumentTokenAndPreservesTextAfterComma()
    {
        var session = CreateListSession(ListSelectionMode.Multiple, ["media-1.0.1", "ado-1.0.1", "weather-1.0.0"]);
        session.UpdateInputText("list media-1.0.1,ad,weather-1.0.0");

        var state = session.HandleKey(CommandInputKey.Right);

        Assert.Equal("list media-1.0.1,ado-1.0.1,weather-1.0.0", state.InputText);
        Assert.Equal("list media-1.0.1,ado-1.0.1", state.InputText[..state.CaretIndex!.Value]);
    }

    [Fact]
    public void RightAcceptsGhostCommaAndNextChecklistValueTogether()
    {
        var session = CreateListSession(ListSelectionMode.Multiple, ["media-1.0.1", "ado-1.0.1", "weather-1.0.0"]);
        session.UpdateInputText("list media-1.0.1");

        var state = session.HandleKey(CommandInputKey.Right);

        Assert.Equal("list media-1.0.1,ado-1.0.1", state.InputText);
        Assert.Equal(state.InputText.Length, state.CaretIndex);
    }

    [Fact]
    public void RightAcceptsQuotedChecklistValueContainingSpacesAsOneToken()
    {
        var session = CreateListSession(ListSelectionMode.Multiple, ["team project", "other value"]);
        session.UpdateInputText("list \"team ");

        var state = session.HandleKey(CommandInputKey.Right);

        Assert.Equal("list \"team project\"", state.InputText);
        Assert.Equal(state.InputText.Length, state.CaretIndex);
    }

    [Fact]
    public void RightCompletesPartialChecklistValueBeforeComma()
    {
        var session = CreateListSession(ListSelectionMode.Multiple, ["media-1.0.1", "ado-1.0.1", "weather-1.0.0"]);
        session.UpdateInputText("list media-1.0.1,ad,weather-1.0.0");

        var state = session.HandleKey(CommandInputKey.Right);

        Assert.Equal("list media-1.0.1,ado-1.0.1,weather-1.0.0", state.InputText);
        Assert.Equal("list media-1.0.1,ado-1.0.1", state.InputText[..state.CaretIndex!.Value]);
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
        Assert.Contains(state.Items, item => item.Value == "token1" && item.IsSelected);
        Assert.Contains(state.Items, item => item.Value == "token1" && item.IsHighlighted);

        state = session.HandleKey(CommandInputKey.Down);
        Assert.Contains(state.Items, item => item.Value == "token2" && item.IsHighlighted);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1,token2", state.InputText);
        Assert.Contains(state.Items, item => item.Value == "token2" && item.IsHighlighted);

        session.HandleKey(CommandInputKey.Up);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token2", state.InputText);
        Assert.Contains(state.Items, item => item.Value == "token1" && item.IsHighlighted);
    }

    [Fact]
    public async Task HistorySuggestionsDoNotAppearWhileMultiSelectChecklistIsActive()
    {
        var viewModel = new LaunchPad.Ignite.App.ViewModels.MainViewModel();
        viewModel.CommandInputText = "secret shell ";

        Assert.True(viewModel.IsChecklistActive);
        Assert.DoesNotContain(viewModel.CompletionItems, item => item.DisplayText.StartsWith("↶ ", StringComparison.Ordinal));

        await viewModel.HandleInputKeyAsync(CommandInputKey.Tab);

        Assert.True(viewModel.IsChecklistActive);
        Assert.DoesNotContain(viewModel.CompletionItems, item => item.DisplayText.StartsWith("↶ ", StringComparison.Ordinal));
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
        state = session.HandleKey(CommandInputKey.Down);
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
        var store = new LaunchPad.Ignite.App.Services.Profiles.ProfileSettingsStore(
            System.IO.Path.Combine(directory.Path, "settings.json"));
        store.SetTheme(LaunchPad.Ignite.App.Services.Themes.ColorThemeService.ToProfileTheme(
            LaunchPad.Ignite.App.Services.Themes.ColorTheme.Default with { Id = "custom-night", Name = "Night" }));
        var session = new CommandCompletionSession(
            new CommandCatalog(new CommandSchema([], [ThemeCommandDefinition.Definition]), ["theme"]),
            new LaunchPad.Ignite.App.Services.Themes.ThemeNamesArgumentValueProvider(store));

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

    [Fact]
    public void EditCommandSuggestsQualifiedCommandNamesAndParsesOptionalName()
    {
        var commands = new[]
        {
            new CommandDefinition("open", "url", "", [new CommandActionDefinition("default", "", "", "https://example.test", [])]),
            new CommandDefinition("build", "shell", "", [new CommandActionDefinition("default", "", "", "dotnet build", [])], "tools")
        };
        var schema = new CommandSchema([], commands.Append(EditCommandDefinition.Definition).ToArray());
        var provider = new LaunchPad.Ignite.App.Services.Completion.CommandNamesArgumentValueProvider(() => commands);
        var session = new CommandCompletionSession(new CommandCatalog(schema, ["shell", "edit"]), new CommandSchemaValueProvider(
            schema,
            provider));

        var suggestions = session.UpdateInputText("edit ");
        var parsed = new LaunchPad.Ignite.Core.Parsing.CommandLineParser(
            new CommandCatalog(schema, ["shell", "edit"]),
            new CommandSchemaValueProvider(schema, provider)).Parse("edit tools:build");
        var bare = new LaunchPad.Ignite.Core.Parsing.CommandLineParser(
            new CommandCatalog(schema, ["shell", "edit"]),
            new CommandSchemaValueProvider(schema, provider)).Parse("edit");

        Assert.Equal("open", suggestions.GhostText);
        Assert.Equal(["open", "tools:build"], provider.GetValues("list:commands"));
        Assert.Equal(LaunchPad.Ignite.Core.Parsing.CommandParseStatus.Valid, parsed.Status);
        Assert.Equal("tools:build", parsed.ParsedCommand?.Arguments["name"]);
        Assert.Equal(LaunchPad.Ignite.Core.Parsing.CommandParseStatus.Valid, bare.Status);
        Assert.Empty(bare.ParsedCommand!.Arguments);
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
