using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class CommandMetadataFilterTests
{
    [Fact]
    public void ParseInputSeparatesCompletedFilterFromCommandText()
    {
        var input = CommandMetadataFilter.ParseInput(">pack:np11,kind:url pipeline");

        Assert.True(input.IsFilterInput);
        Assert.True(input.IsComplete);
        Assert.Equal("pipeline", input.CommandText);
        Assert.NotNull(input.Filter);
        Assert.True(input.Filter.Matches(CreateCommand("pipeline", "url", "np11"), "np11"));
    }

    [Fact]
    public void ParseInputWaitsForWhitespaceAfterFilter()
    {
        var input = CommandMetadataFilter.ParseInput(">kind:url");

        Assert.True(input.IsFilterInput);
        Assert.False(input.IsComplete);
        Assert.Null(input.Filter);
        Assert.Empty(input.CommandText);
    }

    [Theory]
    [InlineData(">", "", 1)]
    [InlineData(">ki", "ki", 1)]
    [InlineData(">p", "p", 1)]
    public void ParseInputIdentifiesFilterFieldNameCompletion(string text, string prefix, int start)
    {
        var input = CommandMetadataFilter.ParseInput(text);

        Assert.True(input.IsFilterInput);
        Assert.True(input.IsEditingFieldName);
        Assert.Equal(prefix, input.FieldPrefix);
        Assert.Equal(start, input.EditingFieldStart);
    }

    [Fact]
    public void FilterMatchesFieldsCaseInsensitivelyAndCombinesDifferentFields()
    {
        Assert.True(CommandMetadataFilter.TryParse("PACK:NP11,Kind:URL", out var filter));

        Assert.True(filter.Matches(CreateCommand("pipeline", "url", "np11"), "np11"));
        Assert.False(filter.Matches(CreateCommand("pipeline", "url", "other"), "other"));
        Assert.False(filter.Matches(CreateCommand("terminal", "shell", "np11"), "np11"));
    }

    [Fact]
    public void ExtensionFilterMatchesCommandOriginIndependentlyFromHandler()
    {
        var command = new CommandDefinition(
            "pr",
            "system",
            string.Empty,
            [new CommandActionDefinition("default", string.Empty, string.Empty, string.Empty, [])],
            "ado",
            Handler: "adoPr");

        Assert.True(CommandMetadataFilter.TryParse("kind:system,extension:ado", out var filter));
        Assert.True(filter.Matches(command, null, "ado"));
        Assert.False(filter.Matches(command, null, "media"));
        Assert.False(filter.Matches(command, null, null));
    }

    [Fact]
    public void LegacyKindOnlyDefinitionMovesHandlerAndMapsCategory()
    {
        var command = new CommandDefinition(
            "pr",
            "adoPr",
            string.Empty,
            [new CommandActionDefinition("default", string.Empty, string.Empty, string.Empty, [])]);

        var normalized = command.NormalizeLegacyHandler();

        Assert.Equal("system", normalized.Kind);
        Assert.Equal("adoPr", normalized.Handler);
        Assert.Equal("adoPr", normalized.ExecutionHandler);
    }

    [Fact]
    public void RepeatedValuesForSameFieldAreAlternatives()
    {
        Assert.True(CommandMetadataFilter.TryParse("kind:url,kind:shell,pack:np11", out var filter));

        Assert.True(filter.Matches(CreateCommand("pipeline", "url", "np11"), "np11"));
        Assert.True(filter.Matches(CreateCommand("terminal", "shell", "np11"), "np11"));
        Assert.False(filter.Matches(CreateCommand("pipeline", "url", "other"), "other"));
    }

    [Fact]
    public void FilteredCompletionContainsOnlyCommandsMatchingKindAndPack()
    {
        var commands = new[]
        {
            CreateCommand("pipeline", "url", "np11"),
            CreateCommand("artifacts", "url", "np11"),
            CreateCommand("release", "url", "other"),
            CreateCommand("terminal", "shell", "np11")
        };
        var schema = new CommandSchema([], commands);
        var session = new CommandCompletionSession(
            new CommandCatalog(schema, ["url", "shell"]),
            new CommandSchemaValueProvider(schema));
        var packIds = commands.ToDictionary(command => command.QualifiedName, command => command.Namespace!);
        Assert.True(CommandMetadataFilter.TryParse("pack:np11,kind:url", out var filter));

        session.UpdateCommandFilter(command => filter.Matches(
            command,
            packIds.TryGetValue(command.QualifiedName, out var packId) ? packId : null));

        Assert.Equal(
            ["np11:artifacts", "np11:pipeline"],
            session.State.Items.Select(item => item.Value).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        var selectedBeforeDown = session.State.Items.Single(item => item.IsHighlighted).Value;
        var navigatedState = session.HandleKey(CommandInputKey.Down);
        var selectedAfterDown = navigatedState.Items.Single(item => item.IsHighlighted).Value;
        Assert.NotEqual(selectedBeforeDown, selectedAfterDown);
        Assert.Contains(selectedAfterDown, new[] { "np11:artifacts", "np11:pipeline" });

        var state = session.UpdateInputText("a");
        Assert.Equal(["np11:artifacts"], state.Items.Select(item => item.Value));
    }

    private static CommandDefinition CreateCommand(string name, string kind, string packId)
        => new(
            name,
            kind,
            string.Empty,
            [new CommandActionDefinition(
                "default",
                string.Empty,
                string.Empty,
                kind == "url" ? "https://example.test" : "cmd",
                [])],
            packId);
}
