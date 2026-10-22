using System;
using System.Collections.Generic;
using System.Diagnostics;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.DirectFiles;
using LaunchPad.Ignite.App.Services.DirectFiles.MacOS;

namespace LaunchPad.Ignite.Tests;

public class MacOSDirectFilePathPlatformTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Users/dev/Projects")]
    [InlineData("/Volumes/Work/shared file.txt")]
    public void RecognizesAbsolutePosixPaths(string path)
    {
        var platform = new MacOSDirectFilePathPlatform();

        Assert.True(platform.IsDirectPath(path));
    }

    [Theory]
    [InlineData("relative/file.txt")]
    [InlineData("C:\\work\\file.txt")]
    [InlineData(" /Users/dev")]
    public void DoesNotRecognizeRelativeWindowsOrPaddedPaths(string path)
    {
        var platform = new MacOSDirectFilePathPlatform();

        Assert.False(platform.IsDirectPath(path));
    }

    [Fact]
    public void GetSuggestionsReturnsMatchingFoldersFirstUsingPosixSeparators()
    {
        string? requestedDirectory = null;
        var platform = new MacOSDirectFilePathPlatform(
            directory =>
            {
                requestedDirectory = directory;
                return ["/Users/dev/alpha", "/Users/dev/Apple.txt", "/Users/dev/archive"];
            },
            path => path.EndsWith("alpha", StringComparison.Ordinal)
                || path.EndsWith("archive", StringComparison.Ordinal));

        var suggestions = platform.GetSuggestions("/Users/dev/a");

        Assert.Equal("/Users/dev/", requestedDirectory);
        Assert.Equal(["/Users/dev/alpha/", "/Users/dev/archive/"], suggestions);
    }

    [Fact]
    public void TryAcceptNextPathSegmentAdvancesByPosixDirectorySegment()
    {
        var platform = new MacOSDirectFilePathPlatform();
        const string suggestion = "/Users/dev/Projects/LaunchPad/";

        Assert.True(platform.TryAcceptNextPathSegment("/Users/de", suggestion, out var firstPath));
        Assert.Equal("/Users/dev/", firstPath);
        Assert.True(platform.TryAcceptNextPathSegment(firstPath, suggestion, out var secondPath));
        Assert.Equal("/Users/dev/Projects/", secondPath);
        Assert.True(platform.TryAcceptNextPathSegment(secondPath, suggestion, out var thirdPath));
        Assert.Equal(suggestion, thirdPath);
    }

    [Fact]
    public void PathPrefixComparisonUsesPosixCaseSensitivity()
    {
        var platform = new MacOSDirectFilePathPlatform();

        Assert.True(platform.IsPathPrefix("/Users/dev", "/Users"));
        Assert.False(platform.IsPathPrefix("/Users/dev", "/users"));
    }

    [Fact]
    public void CreatesFinderOpenRequestForFolders()
    {
        var platform = new MacOSDirectFilePathPlatform();

        var startInfo = platform.CreateOpenPathStartInfo("/Users/dev/Projects", isDirectory: true);

        Assert.Equal("/usr/bin/open", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["/Users/dev/Projects"], startInfo.ArgumentList);
    }

    [Fact]
    public void CreatesFinderRevealRequestForFiles()
    {
        var platform = new MacOSDirectFilePathPlatform();

        var startInfo = platform.CreateOpenPathStartInfo("/Users/dev/Projects/file.txt", isDirectory: false);

        Assert.Equal("/usr/bin/open", startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["-R", "/Users/dev/Projects/file.txt"], startInfo.ArgumentList);
    }

    [Fact]
    public void HandlerUsesMacOSPlatformToOpenPath()
    {
        var starter = new RecordingProcessStarter();
        var platform = new MacOSDirectFilePathPlatform();
        var handler = new DirectFilePathHandler(
            starter,
            platform,
            fileExists: path => path == "/Users/dev/file.txt",
            directoryExists: _ => false);

        var handled = handler.TryOpen("/Users/dev/file.txt", out var result);

        Assert.True(handled);
        Assert.True(result.Succeeded);
        Assert.Equal("Opened file.", result.Message);
        Assert.Equal("/usr/bin/open", starter.StartInfo!.FileName);
        Assert.Equal(["-R", "/Users/dev/file.txt"], starter.StartInfo.ArgumentList);
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo) => StartInfo = startInfo;
    }
}
