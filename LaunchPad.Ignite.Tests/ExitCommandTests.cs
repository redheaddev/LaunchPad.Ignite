using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class ExitCommandTests
{
    [Fact]
    public void ExitCommandInvokesShutdownAction()
    {
        var catalog = new CommandCatalog(new CommandSchema([], [ExitCommandModel.Definition]), ["exit"]);
        var parser = new CommandLineParser(catalog, new CommandSchemaValueProvider(new CommandSchema([], [])));
        var shutdownCalled = false;
        var model = new ExitCommandModel(() => shutdownCalled = true);
        var parseResult = parser.Parse("exit");

        Assert.Equal(CommandParseStatus.Valid, parseResult.Status);
        var result = model.Execute(parseResult.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.True(shutdownCalled);
    }
}
