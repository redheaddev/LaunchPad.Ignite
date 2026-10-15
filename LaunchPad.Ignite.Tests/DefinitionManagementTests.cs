using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Schema;
using System;
using System.IO;

namespace LaunchPad.Ignite.Tests;

public sealed class DefinitionManagementTests
{
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void ParserReadsUrlCommandWithRequiredOptionalAndNamespacedEnumArgument()
    {
        var result = new DefinitionManagementParser().Parse(
            "launchpad command add url ado:workitem --target \"https://example.test/{project}/{id}\" --arg project:required:ref=enum:ado:project --arg id:optional");

        Assert.Equal(DefinitionManagementStatus.Valid, result.Status);
        Assert.Equal("ado:workitem", result.Request?.Name);
        Assert.Equal("https://example.test/{project}/{id}", result.Request?.Target);
        Assert.Collection(result.Request!.Arguments!,
            argument => { Assert.Equal("project", argument.Name); Assert.Equal("enum:ado:project", argument.Ref); Assert.False(argument.Optional); },
            argument => { Assert.Equal("id", argument.Name); Assert.True(argument.Optional); });
    }

    [Fact]
    public void ParserReadsShellTargetArgumentsAndEnumCsvValues()
    {
        var parser = new DefinitionManagementParser();
        var command = parser.Parse("launchpad command add shell vscode --target \"Code.exe\" --target-arg \"{file}\" --arg file:optional");
        var list = parser.Parse("launchpad list add enum projects --values \"architecture,claims,saif\" --default architecture");

        Assert.Equal(DefinitionManagementStatus.Valid, command.Status);
        Assert.Equal(["{file}"], command.Request?.TargetArguments);
        Assert.Equal(DefinitionManagementStatus.Valid, list.Status);
        Assert.Equal(["architecture", "claims", "saif"], list.Request?.Values);
        Assert.Equal("architecture", list.Request?.Default);
    }

    [Theory]
    [InlineData("launchpad command add url foo --target https://example.test --arg x")]
    [InlineData("launchpad command add shell foo --target app.exe --target-arg")]
    [InlineData("launchpad list add enum foo --values a,b")]
    [InlineData("launchpad list edit foo")]
    public void ParserRejectsMalformedManagementCommands(string input)
    {
        Assert.Equal(DefinitionManagementStatus.Invalid, new DefinitionManagementParser().Parse(input).Status);
    }

    [Theory]
    [InlineData("launchpad ", "launchpad command", "launchpad <command|list>")]
    [InlineData("launchpad command ", "launchpad command add", "launchpad command <add|edit|remove|show|list>")]
    [InlineData("launchpad command add ", "launchpad command add url", "launchpad command add <url|shell>")]
    [InlineData("launchpad command a", "launchpad command add ", "launchpad command <add|edit|remove|show|list>")]
    [InlineData("launchpad command add u", "launchpad command add url ", "launchpad command add <url|shell>")]
    [InlineData("launchpad comman", "launchpad command ", "launchpad <command|list>")]
    [InlineData("launchpad list add enum weather:cities ", "launchpad list add enum weather:cities --values", "--values")]
    public void ParserProvidesContextualHelpAndCompletionSuggestions(
        string input,
        string expectedSuggestion,
        string expectedUsageContent)
    {
        var help = new DefinitionManagementParser().GetHelp(input);

        Assert.Contains(help.Suggestions, suggestion => suggestion.StartsWith(expectedSuggestion, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(expectedUsageContent, help.Usage, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(help.Example));
    }

    [Theory]
    [InlineData("launchpad command ", "launchpad command add")]
    [InlineData("launchpad command add ", "launchpad command add url")]
    [InlineData("launchpad list add enum weather:cities ", "launchpad list add enum weather:cities --values")]
    [InlineData("launchpad command", "launchpad command add")]
    [InlineData("launchpad command a", "launchpad command add ")]
    [InlineData("launchpad command add u", "launchpad command add url ")]
    [InlineData("launchpad comman", "launchpad command ")]
    public void ParserSuggestionsAlwaysMatchTheTypedPrefix(string input, string expectedSuggestion)
    {
        var suggestions = new DefinitionManagementParser().GetHelp(input).Suggestions;

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, suggestion =>
            Assert.StartsWith(input, suggestion, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(suggestions, suggestion => suggestion.StartsWith(expectedSuggestion, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("launchpad comman")]
    [InlineData("launchpad li")]
    public void ParserRecognizesPartialEntityTokensAsManagementInput(string input)
    {
        Assert.True(new DefinitionManagementParser().IsManagementCommand(input));
    }

    [Theory]
    [InlineData("launchpad command", true)]
    [InlineData("launchpad list", true)]
    [InlineData("launchpad comman", false)]
    [InlineData("launchpad command ", false)]
    public void ParserRequestsTabSeparatorOnlyAfterAnExactEntityToken(string input, bool expected)
    {
        Assert.Equal(expected, new DefinitionManagementParser().ShouldAppendSeparatorOnTab(input));
    }

    [Theory]
    [InlineData("launchpad command", "launchpad command add ")]
    [InlineData("launchpad command add ", "launchpad command add url ")]
    public void TabAcceptedSuggestionPrefixHasNextLevelCandidates(string acceptedPrefix, string expectedNextCandidate)
    {
        var nextHelp = new DefinitionManagementParser().GetHelp(acceptedPrefix);

        Assert.Contains(nextHelp.Suggestions, suggestion =>
            suggestion.StartsWith(expectedNextCandidate, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ModelCreatesAndEditsThenListsUserCommand()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath);
        loader.Load();
        var refreshCount = 0;
        var model = new DefinitionManagementCommandModel(loader, () => refreshCount++);

        var added = model.Execute("launchpad command add url workitem --target \"https://example.test/{id}\" --arg id:required --description \"Open work item\"");
        var edited = model.Execute("launchpad command edit workitem --description \"Updated description\" --target \"https://example.test/items/{id}\"");
        var listed = model.Execute("launchpad command list");

        Assert.True(added.Succeeded, added.Message);
        Assert.True(edited.Succeeded, edited.Message);
        Assert.Contains("workitem [url/url] - Updated description", listed.Message);
        var command = Assert.Single(loader.GetUserCommands());
        Assert.Equal("https://example.test/items/<id>", command.Actions[0].TargetTemplate);
        Assert.Equal(2, refreshCount);
    }

    [Fact]
    public void ModelAddsEditsAndRemovesUserEnumListInSchema()
    {
        using var directory = new TemporaryDirectory();
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(new CommandSchema([], []), userPath);
        loader.Load();
        var model = new DefinitionManagementCommandModel(loader, () => { });

        var added = model.Execute("launchpad list add enum project --values \"zebra,claims,architecture\" --default claims");
        Assert.True(added.Succeeded, added.Message);
        Assert.Equal(["claims", "architecture", "zebra"], Assert.Single(loader.GetUserLists()).Values);

        var edited = model.Execute("launchpad list edit project --add beta --remove zebra --default beta");
        Assert.True(edited.Succeeded, edited.Message);
        Assert.Equal(["beta", "architecture", "claims"], Assert.Single(loader.GetUserLists()).Values);
        Assert.Equal("beta", Assert.Single(loader.GetUserLists()).Default);

        var removed = model.Execute("launchpad list remove project");
        Assert.True(removed.Succeeded, removed.Message);
        Assert.Empty(loader.GetUserLists());
    }

    [Fact]
    public void ModelRejectsOverwritingApplicationDefinitionsAndRemovingReferencedList()
    {
        using var directory = new TemporaryDirectory();
        var appList = new CommandListDefinition("enum", "builtIn", ["one"], "one");
        var appCommand = new CommandDefinition("builtIn", "url", "", [new CommandActionDefinition("default", "", "", "https://example.test", [])]);
        var appSchema = new CommandSchema([appList], [appCommand]);
        var userPath = Path.Combine(directory.Path, "user-commands.json");
        var loader = new CommandSchemaLoader(appSchema, userPath);
        loader.Load();
        var model = new DefinitionManagementCommandModel(loader, () => { });

        var commandCollision = model.Execute("launchpad command add url builtIn --target https://example.test");
        var listCollision = model.Execute("launchpad list add enum builtIn --values one --default one");
        var referencedList = loader.AddUserList(new CommandListDefinition("enum", "mine", ["one"], "one"));
        loader.AddUserCommand(new CommandDefinition("usesMine", "url", "", [
            new CommandActionDefinition("default", "", "", "https://example.test/<value>", [new CommandArgumentDefinition("value", Ref: "enum:mine")])
        ]));
        var removeReferenced = model.Execute("launchpad list remove mine");

        Assert.False(commandCollision.Succeeded);
        Assert.False(listCollision.Succeeded);
        Assert.False(removeReferenced.Succeeded);
        Assert.Contains("referenced", removeReferenced.Message, StringComparison.OrdinalIgnoreCase);
    }
}
