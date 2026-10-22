using System;
using LaunchPad.Ignite.App.Services.Platform.DirectFiles.Linux;

namespace LaunchPad.Ignite.Tests;

public sealed class LinuxDirectFilePathPlatformTests
{
    [Fact]
    public void RecognizesAbsolutePosixPaths()
    {
        var platform = new LinuxDirectFilePathPlatform();

        Assert.True(platform.IsDirectPath("/home/dev/project"));
        Assert.False(platform.IsDirectPath("relative/file.txt"));
        Assert.False(platform.IsDirectPath(" /home/dev"));
    }

    [Fact]
    public void ReturnsMatchingDirectoriesBeforeFiles()
    {
        var platform = new LinuxDirectFilePathPlatform(
            _ => ["/home/dev/zeta", "/home/dev/alpha.txt", "/home/dev/alpha-dir"],
            path => path.EndsWith("alpha-dir", StringComparison.Ordinal));

        var suggestions = platform.GetSuggestions("/home/dev/a");

        Assert.Equal(["/home/dev/alpha-dir/", "/home/dev/alpha.txt"], suggestions);
    }

    [Fact]
    public void CreatesXdgOpenRequestForFilesAndDirectories()
    {
        var platform = new LinuxDirectFilePathPlatform();

        var fileStartInfo = platform.CreateOpenPathStartInfo("/home/dev/file.txt", isDirectory: false);
        var directoryStartInfo = platform.CreateOpenPathStartInfo("/home/dev", isDirectory: true);

        Assert.Equal("xdg-open", fileStartInfo.FileName);
        Assert.False(fileStartInfo.UseShellExecute);
        Assert.Equal(["/home/dev/file.txt"], fileStartInfo.ArgumentList);
        Assert.Equal(["/home/dev"], directoryStartInfo.ArgumentList);
    }
}
