using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class UrlTargetBuilderTests
{
    private readonly CommandLineParser _parser = new(
        new CommandCatalog(TestCommandSchemas.Pipeline),
        TestCommandSchemas.CreatePipelineProvider());
    private readonly UrlTargetBuilder _builder = new();

    [Fact]
    public void BuildCreatesOneUrlForEachCsvValue()
    {
        var parseResult = _parser.Parse("pipeline claims 1234,2345");

        var result = _builder.Build(parseResult);

        Assert.True(result.Succeeded);
        Assert.Collection(
            result.Targets,
            target => Assert.Equal("https://dev.azure.com/saifcorporation/claims/_build?definitionId=1234", target.Value),
            target => Assert.Equal("https://dev.azure.com/saifcorporation/claims/_build?definitionId=2345", target.Value));
    }

    [Fact]
    public void BuildOmitsQueryParameterForMissingOptionalCsvArgument()
    {
        var parseResult = _parser.Parse("pipeline claims");

        var result = _builder.Build(parseResult);

        Assert.True(result.Succeeded);
        var target = Assert.Single(result.Targets);
        Assert.Equal("https://dev.azure.com/saifcorporation/claims/_build", target.Value);
    }

    [Fact]
    public void BuildRejectsInvalidParseResult()
    {
        var result = _builder.Build(new CommandParseResult(CommandParseStatus.Incomplete));

        Assert.False(result.Succeeded);
        Assert.Empty(result.Targets);
    }
}
