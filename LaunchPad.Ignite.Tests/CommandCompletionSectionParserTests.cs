using LaunchPad.Ignite.Core.Completion;

namespace LaunchPad.Ignite.Tests;

public class CommandCompletionSectionParserTests
{
    [Theory]
    [InlineData("C:\\Users\\Alice", 5, "C:\\Users\\")]
    [InlineData("usr/local/bin", 0, "usr/")]
    [InlineData("file name/path", 0, "file ")]
    [InlineData("\"file name/path\" next", 0, "\"file name/path\" ")]
    [InlineData("'file name/path' next", 0, "'file name/path' ")]
    [InlineData("'file name/path next", 0, "'file name/path next")]
    public void FindsNextSectionEnd(string text, int startIndex, string expectedSection)
    {
        var endIndex = CommandCompletionSectionParser.FindNextSectionEnd(text, startIndex);

        Assert.Equal(expectedSection, text[..endIndex]);
    }

    [Theory]
    [InlineData("root/dir/file", "file", "dir/")]
    [InlineData("open \"My Documents/Draft\" next", "next", "\"My Documents/Draft\" ")]
    [InlineData("open 'My Documents/Draft' next", "next", "'My Documents/Draft' ")]
    [InlineData("open 'unfinished section", "'unfinished section", "open ")]
    public void FindsPreviousSectionsFromRightToLeft(
        string text,
        string expectedLastSection,
        string expectedPreviousSection)
    {
        var lastStart = CommandCompletionSectionParser.FindPreviousSectionStart(text, text.Length);

        Assert.Equal(expectedLastSection, text[lastStart..]);
        var previousStart = CommandCompletionSectionParser.FindPreviousSectionStart(text, lastStart);

        Assert.Equal(expectedPreviousSection, text[previousStart..lastStart]);
    }
}
