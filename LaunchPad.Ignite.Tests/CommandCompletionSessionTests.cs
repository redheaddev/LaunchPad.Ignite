using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Schema;

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

        session.HandleKey(CommandInputKey.Down);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1,token2", state.InputText);

        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1", state.InputText);
        state = session.HandleKey(CommandInputKey.Tab);
        Assert.Equal("secret shell token1,token2", state.InputText);
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
    public void MinimalListAcceptsCurrentSuggestionAndAdvancesGhost()
    {
        var session = new CommandCompletionSession(
            new CommandCatalog(TestCommandSchemas.SecretList(ListSelectionMode.Minimal)),
            TestCommandSchemas.CreateSecretListProvider());
        session.UpdateInputText("secret shell ");

        var state = session.HandleKey(CommandInputKey.Tab);

        Assert.Equal("secret shell token1", state.InputText);
        Assert.Equal("token2", state.GhostText);
        Assert.DoesNotContain(state.Items, item => item.Value == "token1");
    }

    private static CommandCompletionSession CreateSecretListSession()
        => new(
            new CommandCatalog(TestCommandSchemas.SecretList()),
            TestCommandSchemas.CreateSecretListProvider());
}
