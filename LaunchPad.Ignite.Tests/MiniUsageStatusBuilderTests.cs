using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.Tests;

public sealed class MiniUsageStatusBuilderTests
{
    [Fact]
    public void ExactCommandWithTrailingSpaceShowsUsageInsteadOfCompletionList()
    {
        var result = MiniUsageStatusBuilder.Build(
            "settings ",
            "Usage: settings",
            ["settings", "secret"],
            highlightedIndex: 1,
            hasSelectedCommandWithTrailingSpace: true);

        Assert.Equal("Usage: settings", result);
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

        Assert.Equal("setup · Usage: settings · 2/2", result);
    }
}
