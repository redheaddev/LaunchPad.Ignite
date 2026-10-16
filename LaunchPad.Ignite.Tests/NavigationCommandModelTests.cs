using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class NavigationCommandModelTests
{
    [Theory]
    [InlineData("settings", LauncherNavigationTarget.Settings)]
    [InlineData("import-export", LauncherNavigationTarget.ImportExport)]
    [InlineData("help", LauncherNavigationTarget.Help)]
    public void ExecutesNavigationCommandAndRequestsMatchingScreen(
        string commandText,
        LauncherNavigationTarget expectedTarget)
    {
        var schema = new CommandSchema([], NavigationCommandModel.Definitions);
        var catalog = new CommandCatalog(schema, ["navigation"]);
        var parser = new CommandLineParser(catalog, new CommandSchemaValueProvider(schema));
        var parsed = parser.Parse(commandText);
        LauncherNavigationTarget? requestedTarget = null;
        var model = new NavigationCommandModel(target => requestedTarget = target);

        Assert.Equal(CommandParseStatus.Valid, parsed.Status);
        var result = model.Execute(parsed.ParsedCommand!);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedTarget, requestedTarget);
    }
}
