using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.Tests;

public class PathLevelSelectionTests
{
    [Theory]
    [InlineData(@"open C:\work\folder\fi", 8, true, 12)]
    [InlineData(@"open C:\work\folder\fi", 13, true, 19)]
    [InlineData(@"open C:\work\folder\fi", 20, false, 13)]
    [InlineData(@"open C:\work\folder\fi", 13, false, 8)]
    public void MovesByPathSegment(string input, int caret, bool moveRight, int expected)
    {
        var found = PathLevelSelection.TryGetBoundary(input, caret, moveRight, out var boundary);

        Assert.True(found);
        Assert.Equal(expected, boundary);
    }

    [Fact]
    public void CtrlShiftRightShrinksSelectAllPathFromDriveToNextDirectory()
    {
        const string path = @"c:\temp\LaunchPad.Ignite.App.Build\";

        var adjusted = PathLevelSelection.TryAdjustSelection(
            path,
            path.Length,
            0,
            path.Length,
            moveRight: true,
            out var start,
            out var end,
            out _);

        Assert.True(adjusted);
        Assert.Equal(2, start);
        Assert.Equal(path.Length, end);
        Assert.Equal(@"\temp\LaunchPad.Ignite.App.Build\", path[start..end]);
    }

    [Fact]
    public void CtrlShiftRightEventuallyCollapsesSelectionAtRightEdge()
    {
        const string path = @"c:\temp\LaunchPad.Ignite.App.Build\";
        var start = 0;
        var end = path.Length;

        var caret = end;
        while (PathLevelSelection.TryAdjustSelection(path, caret, start, end, true, out var nextStart, out var nextEnd, out var nextCaret))
        {
            start = nextStart;
            end = nextEnd;
            caret = nextCaret;
        }

        Assert.Equal(path.Length, start);
        Assert.Equal(path.Length, end);
    }

    [Fact]
    public void CtrlShiftLeftEventuallyCollapsesSelectionAtLeftEdge()
    {
        const string path = @"c:\temp\LaunchPad.Ignite.App.Build\";
        var start = 0;
        var end = path.Length;

        var caret = start;
        while (PathLevelSelection.TryAdjustSelection(path, caret, start, end, false, out var nextStart, out var nextEnd, out var nextCaret))
        {
            start = nextStart;
            end = nextEnd;
            caret = nextCaret;
        }

        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }

    [Fact]
    public void MovesAcrossDriveAndUncRootsBySeparator()
    {
        var drivePath = @"C:\work\file";
        Assert.True(PathLevelSelection.TryGetBoundary(drivePath, 3, true, out var driveBoundary));
        Assert.Equal(7, driveBoundary);

        var uncPath = @"\\server\share\file";
        Assert.True(PathLevelSelection.TryGetBoundary(uncPath, 2, true, out var uncBoundary));
        Assert.Equal(8, uncBoundary);
    }

    [Theory]
    [InlineData(@"c:\temp\LaunchPad.Ignite.App.Build\", 0)]
    [InlineData(@"\\server\path", 2)]
    public void RecognizesSelectedAbsolutePath(string path, int selectionStart)
    {
        Assert.True(PathLevelSelection.IsPathSelection(path, selectionStart, path.Length));
    }

    [Fact]
    public void DoesNotTreatAnOrdinaryTextSelectionAsAPath()
    {
        Assert.False(PathLevelSelection.IsPathSelection("open selected text", 5, 13));
    }

    [Theory]
    [InlineData(@"c:\temp\LaunchPad.Ignite.App.Build\", 0, 2)]
    [InlineData(@"\\server\share\file", 2, 8)]
    public void PlainRightMovesCaretToTheNextPathSeparator(
        string path,
        int caret,
        int expectedBoundary)
    {
        Assert.True(PathLevelSelection.IsPathSelection(path, caret, caret));

        var adjusted = PathLevelSelection.TryAdjustSelection(
            path,
            caret,
            caret,
            caret,
            moveRight: true,
            out var start,
            out var end,
            out var nextCaret);

        Assert.True(adjusted);
        Assert.Equal(caret, start);
        Assert.Equal(expectedBoundary, end);
        Assert.Equal(expectedBoundary, nextCaret);
    }

    [Fact]
    public void CtrlShiftRightStopsBeforeUncSeparatorThenConsumesItOnNextPress()
    {
        const string path = @"\\server\c$\path";

        var adjusted = PathLevelSelection.TryAdjustSelection(
            path,
            path.Length,
            2,
            path.Length,
            moveRight: true,
            out var start,
            out var end,
            out var caret);

        Assert.True(adjusted);
        Assert.Equal(8, start);
        Assert.Equal(path.Length, end);
        Assert.Equal(8, caret);
        Assert.Equal(@"\\server", path[..start]);

        Assert.True(PathLevelSelection.TryGetBoundary(path, caret, moveRight: true, out var nextBoundary));
        Assert.Equal(11, nextBoundary);
    }

    [Theory]
    [InlineData(@"c:\temp\LaunchPad.Ignite.App.Build\", 2, 2)]
    [InlineData(@"\\server\path", 8, 8)]
    public void RightSelectionAdjustmentHandlesReversedSelectionEndpoints(
        string path,
        int expectedStart,
        int expectedCaret)
    {
        var adjusted = PathLevelSelection.TryAdjustSelection(
            path,
            0,
            path.Length,
            0,
            moveRight: true,
            out var start,
            out var end,
            out var caret);

        Assert.True(adjusted);
        Assert.Equal(expectedStart, start);
        Assert.Equal(path.Length, end);
        Assert.Equal(expectedCaret, caret);
    }

    [Fact]
    public void MovesOrdinaryWordSelectionByCommandToken()
    {
        var found = PathLevelSelection.TryGetBoundary("open artifacts", 5, true, out var boundary);

        Assert.True(found);
        Assert.Equal("open artifacts".Length, boundary);
    }

    [Theory]
    [InlineData("ado:pipeline \"architecture\" 1234,1235", 0, true, 12)]
    [InlineData("ado:pipeline \"architecture\" 1234,1235", 12, true, 13)]
    [InlineData("ado:pipeline \"architecture\" 1234,1235", 26, true, 27)]
    [InlineData("ado:pipeline \"architecture\" 1234,1235", 31, true, 32)]
    [InlineData("ado:pipeline \"architecture\" 1234,1235", 37, false, 33)]
    [InlineData("ado:pipeline \"architecture\" 1234,1235", 26, false, 14)]
    public void MovesCommandSelectionByWhitespaceQuoteAndComma(string input, int caret, bool moveRight, int expected)
    {
        var found = PathLevelSelection.TryGetBoundary(input, caret, moveRight, out var boundary);

        Assert.True(found);
        Assert.Equal(expected, boundary);
    }

    [Fact]
    public void DoesNotStopAtNamespaceColon()
    {
        var found = PathLevelSelection.TryGetBoundary("ado:pipeline", 0, true, out var boundary);

        Assert.True(found);
        Assert.Equal("ado:pipeline".Length, boundary);
    }
}
