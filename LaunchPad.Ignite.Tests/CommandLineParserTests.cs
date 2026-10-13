using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;
using LaunchPad.Ignite.App.Commands;

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

    [Theory]
    [InlineData("pipeline pipelineId=1234 project=claims")]
    [InlineData("pipeline pipelineId=1234 claims")]
    [InlineData("pipeline project=claims 1234")]
    public void ParseAcceptsNamedAndMixedArguments(string input)
    {
        var result = _parser.Parse(input);

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.Equal("claims", result.ParsedCommand!.Arguments["project"]);
        Assert.Equal("1234", result.ParsedCommand.Arguments["pipelineId"]);
    }

    [Theory]
    [InlineData("pipeline unknown=value")]
    [InlineData("pipeline project=claims project=architecture")]
    [InlineData("pipeline project=")]
    [InlineData("pipeline claims project=architecture")]
    public void ParseRejectsUnknownDuplicateOrEmptyNamedArguments(string input)
    {
        var result = _parser.Parse(input);

        Assert.Equal(CommandParseStatus.Invalid, result.Status);
    }

    [Fact]
    public void ParsePreservesEqualsAfterNamedArgumentSeparator()
    {
        var result = _parser.Parse("pipeline project=claims pipelineId=12=34");

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.Equal("12=34", result.ParsedCommand!.Arguments["pipelineId"]);
    }

    [Fact]
    public void ParseResolvesNamespacedCommandName()
    {
        var schema = TestCommandSchemas.Pipeline;
        var namespacedSchema = schema with
        {
            Commands = [schema.Commands[0] with { Namespace = "ns" }]
        };
        var parser = new CommandLineParser(
            new CommandCatalog(namespacedSchema),
            new CommandSchemaValueProvider(namespacedSchema));

        var result = parser.Parse("ns:pipeline claims");

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.Equal("ns:pipeline", result.ParsedCommand!.Definition.QualifiedName);
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
    public void ParseAcceptsCustomValueForEnumArgumentWhenEnabled()
    {
        var schema = TestCommandSchemas.Pipeline;
        var command = schema.Commands[0];
        var action = command.Actions[0];
        var projectArgument = action.Arguments[0] with { AllowCustomValues = true };
        var customSchema = schema with
        {
            Commands =
            [
                command with
                {
                    Actions =
                    [
                        action with
                        {
                            Arguments = [projectArgument, action.Arguments[1]]
                        }
                    ]
                }
            ]
        };
        var parser = new CommandLineParser(
            new CommandCatalog(customSchema),
            TestCommandSchemas.CreatePipelineProvider());

        var result = parser.Parse("pipeline x 1234");

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.Equal("x", result.ParsedCommand!.Arguments["project"]);
        Assert.Equal("1234", result.ParsedCommand.Arguments["pipelineId"]);
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

    [Theory]
    [InlineData("snippet run deploy PROJECT=ignite", "ignite")]
    [InlineData("snippet run deploy PROJECT=two=parts", "two=parts")]
    [InlineData("snippet run deploy PROJECT=\"team project\"", "team project")]
    public void ParseAcceptsAdditionalNamedArgumentsWhenEnabled(string input, string expectedValue)
    {
        var command = SnippetCommandDefinition.Definition;
        var runAction = command.Actions.Single(action => action.Name == "run");
        var schema = new CommandSchema([], [command]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["snippet"]),
            new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
            {
                ["list:snippets"] = ["deploy"]
            }));

        var result = parser.Parse(input);

        Assert.Equal(CommandParseStatus.Valid, result.Status);
        Assert.Equal(expectedValue, result.ParsedCommand!.Arguments["PROJECT"]);
        Assert.True(runAction.AllowAdditionalNamedArguments);
    }

    [Theory]
    [InlineData("snippet run deploy PROJECT=")]
    [InlineData("snippet run deploy PROJECT=one PROJECT=two")]
    public void ParseRejectsMalformedAdditionalNamedArguments(string input)
    {
        var command = SnippetCommandDefinition.Definition;
        var schema = new CommandSchema([], [command]);
        var parser = new CommandLineParser(
            new CommandCatalog(schema, ["snippet"]),
            new DictionaryArgumentValueProvider(new Dictionary<string, IReadOnlyList<string>>
            {
                ["list:snippets"] = ["deploy"]
            }));

        var result = parser.Parse(input);

        Assert.Equal(CommandParseStatus.Invalid, result.Status);
    }
}
