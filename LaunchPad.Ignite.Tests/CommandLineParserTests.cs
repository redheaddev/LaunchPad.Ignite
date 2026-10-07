using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class CommandLineParserTests
{
    private readonly CommandLineParser _parser = new(
        new CommandCatalog(TestCommandSchemas.Pipeline),
        TestCommandSchemas.CreatePipelineProvider());

    [Theory]
    [InlineData("pipeline claims", "claims", null)]
    [InlineData("pipeline architecture 1234,2345", "architecture", "1234,2345")]
    public void ParseAcceptsPipelineArguments(string input, string expectedProject, string? expectedIds)
    {
        var result = _parser.Parse(input);

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.NotNull(result.ParsedCommand);
        Assert.Equal(expectedProject, result.ParsedCommand.Arguments["project"]);
        Assert.Equal(expectedIds, result.ParsedCommand.Arguments.GetValueOrDefault("pipelineId"));
    }

    [Fact]
    public void ParseReturnsIncompleteForMissingRequiredProject()
    {
        var result = _parser.Parse("pipeline");

        Assert.Equal(CommandParseStatus.Incomplete, result.Status);
    }

    [Fact]
    public void ParseRejectsUnknownEnumValue()
    {
        var result = _parser.Parse("pipeline unknown");

        Assert.Equal(CommandParseStatus.Invalid, result.Status);
    }

    [Fact]
    public void ParseAcceptsMultipleDynamicListValues()
    {
        var schema = TestCommandSchemas.SecretList();
        var parser = new CommandLineParser(new CommandCatalog(schema), TestCommandSchemas.CreateSecretListProvider());

        var result = parser.Parse("secret shell token1,token2");

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.Equal("token1,token2", result.ParsedCommand!.Arguments["secrets"]);
    }
}
