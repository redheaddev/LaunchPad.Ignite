using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.DirectFiles;
using LaunchPad.Ignite.App.Services.DirectFiles.Windows;
using LaunchPad.Ignite.App.Services.UrlTargets;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class CommandInputRouterTests
{
    [Fact]
    public void PathApplicationIsResolvedBeforeBareDomainUrl()
    {
        var starter = new RecordingProcessStarter();
        var executable = Path.Combine("tools", "cmd.exe");
        var platform = new WindowsDirectFilePathPlatform(
            pathProvider: () => "tools",
            pathExtensionsProvider: () => ".EXE",
            fileExists: candidate => candidate == executable);
        var opener = new RecordingUrlTargetOpener();
        var router = CreateRouter(platform, starter, opener);

        var result = router.Route("cmd.exe", allowDirectExecution: true, newWindow: false, _ => false);

        Assert.NotNull(result.ExecutionResult);
        Assert.True(result.ExecutionResult.Succeeded);
        Assert.Equal(executable, starter.StartInfo!.FileName);
        Assert.Empty(opener.Targets);
    }

    [Fact]
    public void BareDomainFallsThroughToUrlWhenNoPathApplicationExists()
    {
        var opener = new RecordingUrlTargetOpener();
        var router = CreateRouter(
            new WindowsDirectFilePathPlatform(pathProvider: () => string.Empty),
            new RecordingProcessStarter(),
            opener);

        var result = router.Route("example.com", allowDirectExecution: true, newWindow: false, _ => false);

        Assert.NotNull(result.ExecutionResult);
        Assert.True(result.ExecutionResult.Succeeded);
        Assert.Equal("https://example.com/", Assert.Single(opener.Targets).Value);
    }

    [Fact]
    public void ParsedCommandIsNotTreatedAsPathApplication()
    {
        var starter = new RecordingProcessStarter();
        var executable = Path.Combine("tools", "tool.exe");
        var platform = new WindowsDirectFilePathPlatform(
            pathProvider: () => "tools",
            pathExtensionsProvider: () => ".EXE",
            fileExists: candidate => candidate == executable);
        var schema = new CommandSchema([], [
            new CommandDefinition("tool", "shell", "", [
                new CommandActionDefinition("default", "", "", "tool.exe", [])])]);
        var router = new CommandInputRouter(
            new CommandLineParser(
                new CommandCatalog(schema, ["shell"]),
                new CommandSchemaValueProvider(schema)),
            new DirectFilePathHandler(starter, platform),
            new UrlCommandModel(new RecordingUrlTargetOpener()));

        var result = router.Route("tool", allowDirectExecution: true, newWindow: false, _ => false);

        Assert.Null(result.ExecutionResult);
        Assert.Equal(CommandParseStatus.Valid, result.ParseResult.Status);
        Assert.Null(starter.StartInfo);
    }

    private static CommandInputRouter CreateRouter(
        WindowsDirectFilePathPlatform platform,
        RecordingProcessStarter starter,
        RecordingUrlTargetOpener opener)
    {
        var schema = new CommandSchema([], []);
        return new CommandInputRouter(
            new CommandLineParser(new CommandCatalog(schema), new CommandSchemaValueProvider(schema)),
            new DirectFilePathHandler(starter, platform),
            new UrlCommandModel(opener));
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }

    private sealed class RecordingUrlTargetOpener : IUrlTargetOpener
    {
        public List<UrlTarget> Targets { get; } = [];

        public void Open(IEnumerable<UrlTarget> targets, bool newWindow = false)
            => Targets.AddRange(targets);
    }
}
