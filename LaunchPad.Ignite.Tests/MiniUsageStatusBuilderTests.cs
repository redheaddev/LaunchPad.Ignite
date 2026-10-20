using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.Tests;

public sealed class MiniUsageStatusBuilderTests
{
    [Fact]
    public void ExactCommandWithTrailingSpaceShowsUsageAndCompletionPosition()
    {
        var result = MiniUsageStatusBuilder.Build(
            "settings ",
            "Usage: settings",
            ["settings", "secret"],
            highlightedIndex: 1,
            hasSelectedCommandWithTrailingSpace: true);

        Assert.Equal("2/2 · Usage: settings", result);
    }

    [Fact]
    public void PartialCommandKeepsHighlightedCompletionAndPosition()
    {
        var result = MiniUsageStatusBuilder.Build(
            "sett",
            "Usage: settings",
            ["settings", "setup"],
            highlightedIndex: 1,
            hasSelectedCommandWithTrailingSpace: false);

        Assert.Equal("2/2 · setup · Usage: settings", result);
    }

    [Fact]
    public void HistoryCompletionUsesHistoryPositionInsteadOfCompletionListPosition()
    {
        var result = MiniUsageStatusBuilder.Build(
            "sett",
            "Usage: settings",
            ["↶ setup", "settings", "secret"],
            highlightedIndex: 0,
            hasSelectedCommandWithTrailingSpace: false,
            positionIndex: 3,
            positionCount: 5);

        Assert.Equal("4/5 · ↶ setup · Usage: settings", result);
    }

    [Fact]
    public void HistoryPositionRemainsVisibleBeforeUsageForSelectedCommand()
    {
        var result = MiniUsageStatusBuilder.Build(
            "settings ",
            "Usage: settings",
            ["↶ settings target"],
            highlightedIndex: 0,
            hasSelectedCommandWithTrailingSpace: true,
            positionIndex: 3,
            positionCount: 5);

        Assert.Equal("4/5 · ↶ settings target · Usage: settings", result);
    }

    [Fact]
    public void NonHistoryCompletionPositionCanExcludeHistoryRows()
    {
        var result = MiniUsageStatusBuilder.Build(
            "ado:pipeline ",
            "Usage: ado:pipeline",
            ["↶ old pipeline", "project1", "project2", "project3"],
            highlightedIndex: 2,
            hasSelectedCommandWithTrailingSpace: true,
            positionIndex: 1,
            positionCount: 3);

        Assert.Equal("2/3 · Usage: ado:pipeline", result);
    }

    [Fact]
    public void ListSelectionShowsPositionBeforeUsageForExactCommand()
    {
        var result = MiniUsageStatusBuilder.Build(
            "ado:pipeline ",
            "Usage: ado:pipeline",
            ["project1", "project2", "project3", "project4", "project5", "project6", "project7", "project8", "project9"],
            highlightedIndex: 1,
            hasSelectedCommandWithTrailingSpace: true);

        Assert.Equal("2/9 · Usage: ado:pipeline", result);
    }
}
