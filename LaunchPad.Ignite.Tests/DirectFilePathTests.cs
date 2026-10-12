using System;
using System.Collections.Generic;
using System.Diagnostics;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Completion;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public class DirectFilePathTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"c:/work/file.txt")]
    [InlineData(@"\\server\share\folder")]
    public void RecognizesAbsoluteWindowsPaths(string path)
    {
        var completion = new DirectFilePathCompletion();

        Assert.True(completion.IsDirectPath(path));
    }

    [Theory]
    [InlineData("relative\\file.txt")]
    [InlineData("https://example.test")]
    [InlineData(" C:\\work")]
    public void DoesNotRecognizeNonAbsolutePaths(string path)
    {
        var completion = new DirectFilePathCompletion();

        Assert.False(completion.IsDirectPath(path));
    }

    [Fact]
    public void GetSuggestionsReturnsMatchingFoldersFirst()
    {
        var completion = new DirectFilePathCompletion(
            _ => [@"C:\projects", @"C:\photo.txt", @"C:\Archive"],
            path => path == @"C:\projects" || path == @"C:\Archive");

        var suggestions = completion.GetSuggestions(@"C:\p");

        Assert.Equal([@"C:\projects\", @"C:\photo.txt"], suggestions);
    }

    [Fact]
    public void TrySelectSuggestionReturnsTheHighlightedPath()
    {
        var suggestions = new[] { @"C:\projects\", @"C:\photo.txt" };

        var selected = DirectFilePathCompletion.TrySelectSuggestion(suggestions, 1, out var path);

        Assert.True(selected);
        Assert.Equal(@"C:\photo.txt", path);
    }

    [Fact]
    public void FindsPathInPositionalArgumentAndReplacesOnlyThatArgument()
    {
        var completion = CreateArgumentPathCompletion();
        var input = @"copy C:\work\fi";

        var found = completion.TryGetContext(input, CreatePathCommandCatalog(), new CommandLineTokenizer(), out var context);

        Assert.True(found);
        Assert.Equal(@"C:\work\fi", context.Path);
        Assert.Equal(@"copy C:\work\file.txt", CommandArgumentPathCompletion.ReplaceValue(input, context, @"C:\work\file.txt"));
    }

    [Fact]
    public void QuotesExpandedPathWhenItContainsSpaces()
    {
        var completion = CreateArgumentPathCompletion();
        var input = @"copy C:\work\fi";

        Assert.True(completion.TryGetContext(input, CreatePathCommandCatalog(), new CommandLineTokenizer(), out var context));
        Assert.Equal(
            "copy \"C:\\work\\my file.txt\"",
            CommandArgumentPathCompletion.ReplaceValue(input, context, @"C:\work\my file.txt"));
    }

    [Fact]
    public void FindsPathInNamedArgumentAndPreservesQuotedValues()
    {
        var completion = CreateArgumentPathCompletion();
        var input = "copy destination=\"C:\\work\\my fi\"";

        var found = completion.TryGetContext(input, CreatePathCommandCatalog(), new CommandLineTokenizer(), out var context);

        Assert.True(found);
        Assert.Equal(@"C:\work\my fi", context.Path);
        Assert.Equal("copy destination=\"C:\\work\\my file.txt\"", CommandArgumentPathCompletion.ReplaceValue(input, context, @"C:\work\my file.txt"));
    }

    [Theory]
    [InlineData(@"copy relative\fi")]
    [InlineData(@"copy unknown=C:\work\fi")]
    public void DoesNotOfferPathCompletionForUnknownOrNonPathArguments(string input)
    {
        var completion = CreateArgumentPathCompletion();

        var found = completion.TryGetContext(input, CreatePathCommandCatalog(), new CommandLineTokenizer(), out _);

        Assert.False(found);
    }

    [Fact]
    public void TryOpenOpensDriveRootInExplorer()
    {
        var starter = new RecordingProcessStarter();
        var completion = new DirectFilePathCompletion(_ => Array.Empty<string>());
        var handler = new DirectFilePathHandler(
            starter,
            completion,
            _ => false,
            path => path == @"C:\");

        var handled = handler.TryOpen(@"C:\", out var result);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.Equal("Opened folder in Explorer.", result.Message);
        Assert.Equal("explorer.exe", starter.StartInfo!.FileName);
        Assert.True(starter.StartInfo.UseShellExecute);
        Assert.Equal([@"C:\"], starter.StartInfo.ArgumentList);
    }

    [Fact]
    public void TryOpenSelectsDirectFileInExplorer()
    {
        var starter = new RecordingProcessStarter();
        var handler = new DirectFilePathHandler(
            starter,
            fileExists: path => path == @"C:\work\file.txt",
            directoryExists: _ => false);

        var handled = handler.TryOpen(@"C:\work\file.txt", out var result);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.Equal("/select,C:\\work\\file.txt", Assert.Single(starter.StartInfo!.ArgumentList));
    }

    [Fact]
    public void TryOpenReportsMissingDirectPath()
    {
        var starter = new RecordingProcessStarter();
        var handler = new DirectFilePathHandler(
            starter,
            fileExists: _ => false,
            directoryExists: _ => false);

        var handled = handler.TryOpen(@"C:\missing", out var result);

        Assert.True(handled);
        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Message);
        Assert.Null(starter.StartInfo);
    }

    [Fact]
    public void TryOpenInTerminalOpensContainingFolderForFile()
    {
        var terminal = new RecordingTerminalLauncher();
        var handler = new DirectFilePathHandler(
            new RecordingProcessStarter(),
            fileExists: path => path == @"C:\work\file.txt",
            directoryExists: _ => false,
            terminalLauncher: terminal);

        var handled = handler.TryOpenInTerminal(@"C:\work\file.txt", out var result);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.Equal(@"C:\work", terminal.WorkingDirectory);
        Assert.Equal("Opened folder in terminal.", result.Message);
    }

    [Fact]
    public void TryOpenInTerminalOpensDirectoryPath()
    {
        var terminal = new RecordingTerminalLauncher();
        var handler = new DirectFilePathHandler(
            new RecordingProcessStarter(),
            fileExists: _ => false,
            directoryExists: path => path == @"C:\work",
            terminalLauncher: terminal);

        var handled = handler.TryOpenInTerminal(@"C:\work", out var result);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.Equal(@"C:\work", terminal.WorkingDirectory);
    }

    [Fact]
    public void TryOpenInTerminalReportsMissingPath()
    {
        var terminal = new RecordingTerminalLauncher();
        var handler = new DirectFilePathHandler(
            new RecordingProcessStarter(),
            fileExists: _ => false,
            directoryExists: _ => false,
            terminalLauncher: terminal);

        var handled = handler.TryOpenInTerminal(@"C:\missing", out var result);

        Assert.True(handled);
        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Message);
        Assert.Null(terminal.WorkingDirectory);
    }

    [Fact]
    public void TryOpenInTerminalDoesNotHandleOrdinaryText()
    {
        var terminal = new RecordingTerminalLauncher();
        var handler = new DirectFilePathHandler(new RecordingProcessStarter(), terminalLauncher: terminal);

        var handled = handler.TryOpenInTerminal("some command", out var result);

        Assert.False(handled);
        Assert.Empty(result.Message);
        Assert.Null(terminal.WorkingDirectory);
    }

    [Fact]
    public void PathSelectionPrefersSelectedText()
    {
        var text = @"open C:\work\file.txt now";

        var found = DirectFilePathSelection.TryGetPath(text, 5, 21, 0, out var path);

        Assert.True(found);
        Assert.Equal(@"C:\work\file.txt", path);
    }

    [Fact]
    public void PathSelectionReturnsQuotedTokenAtCaret()
    {
        var text = "open \"C:\\work area\\file.txt\" now";

        var found = DirectFilePathSelection.TryGetPath(text, 0, 0, text.IndexOf("file.txt", StringComparison.Ordinal), out var path);

        Assert.True(found);
        Assert.Equal("\"C:\\work area\\file.txt\"", path);
    }

    [Fact]
    public void PathSelectionReturnsUnquotedTokenAtCaret()
    {
        var text = @"open C:\work\file.txt now";

        var found = DirectFilePathSelection.TryGetPath(text, 0, 0, text.IndexOf("file.txt", StringComparison.Ordinal), out var path);

        Assert.True(found);
        Assert.Equal(@"C:\work\file.txt", path);
    }

    [Fact]
    public void PathSelectionDoesNotReturnWhitespace()
    {
        var found = DirectFilePathSelection.TryGetPath("   ", 0, 0, 1, out var path);

        Assert.False(found);
        Assert.Empty(path);
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }

    private sealed class RecordingTerminalLauncher : ISecretTerminalLauncher
    {
        public string? WorkingDirectory { get; private set; }

        public void OpenShell(IReadOnlyDictionary<string, string> environment, string? workingDirectory)
            => WorkingDirectory = workingDirectory;
    }

    private static CommandArgumentPathCompletion CreateArgumentPathCompletion()
        => new(new DirectFilePathHandler(new RecordingProcessStarter()));

    private static CommandCatalog CreatePathCommandCatalog()
    {
        var schema = new CommandSchema(
            [],
            [new CommandDefinition(
                "copy",
                "shell",
                string.Empty,
                [new CommandActionDefinition(
                    "default",
                    string.Empty,
                    string.Empty,
                    "copy {source} {destination}",
                    [new CommandArgumentDefinition("source"), new CommandArgumentDefinition("destination")])])]);
        return new CommandCatalog(schema, ["shell"]);
    }
}
