using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class CommandTextVariableExpanderTests
{
    [Fact]
    public void ExpandResolvesNamespacedVariablesWithoutCaseSensitivity()
    {
        var expander = new CommandTextVariableExpander(
            [new CommandVariableDefinition("orgUrl", "https://dev.azure.com/saifcorporation", "ado")]);

        var result = expander.Expand("open $ado:orgurl");

        Assert.Equal("open https://dev.azure.com/saifcorporation", result.ExpandedText);
        Assert.Equal(["ado:orgurl"], result.ResolvedVariables);
        Assert.Empty(result.UnresolvedVariables);
    }

    [Fact]
    public void ExpandResolvesBareVariablesAndCanRefreshValues()
    {
        var expander = new CommandTextVariableExpander();

        expander.UpdateVariables([new CommandVariableDefinition("region", "west")]);
        var result = expander.Expand("open $region");

        Assert.Equal("open west", result.ExpandedText);
        Assert.Equal(["region"], result.ResolvedVariables);
        Assert.Empty(result.UnresolvedVariables);
    }

    [Fact]
    public void ExpandLeavesUnknownVariablesInTextAndReportsThem()
    {
        var expander = new CommandTextVariableExpander(
            [new CommandVariableDefinition("orgUrl", "https://dev.azure.com/saifcorporation", "ado")]);

        var result = expander.Expand("open $ado:orgUrl $missing:value");

        Assert.Equal("open https://dev.azure.com/saifcorporation $missing:value", result.ExpandedText);
        Assert.Equal(["missing:value"], result.UnresolvedVariables);
    }

    [Fact]
    public void ExpandDoesNotTreatUncAdministrativeShareSuffixAsVariable()
    {
        var expander = new CommandTextVariableExpander();

        var result = expander.Expand(@"copy \\server\C$\folder");

        Assert.Equal(@"copy \\server\C$\folder", result.ExpandedText);
        Assert.Empty(result.ResolvedVariables);
        Assert.Empty(result.UnresolvedVariables);
    }

    [Fact]
    public void ExpandedCommandTextCanBeParsedAsRegularCommandInput()
    {
        var schema = new CommandSchema(
            [],
            [new CommandDefinition(
                "open",
                "url",
                "",
                [new CommandActionDefinition("default", "", "", "https://example.test/?target=<target>", [new CommandArgumentDefinition("target")])])]);
        var expander = new CommandTextVariableExpander(
            [new CommandVariableDefinition("orgUrl", "https://dev.azure.com/saifcorporation", "ado")]);
        var parser = new LaunchPad.Ignite.Core.Parsing.CommandLineParser(
            new CommandCatalog(schema),
            new LaunchPad.Ignite.Core.Lists.CommandSchemaValueProvider(schema));

        var result = parser.Parse(expander.Expand("open $ado:orgUrl").ExpandedText);

        Assert.Equal(LaunchPad.Ignite.Core.Parsing.CommandParseStatus.Valid, result.Status);
        Assert.Equal("https://dev.azure.com/saifcorporation", result.ParsedCommand!.Arguments["target"]);
    }
}
