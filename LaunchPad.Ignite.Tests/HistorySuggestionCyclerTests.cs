using System;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.Tests;

public sealed class HistorySuggestionCyclerTests
{
    [Fact]
    public void UpCyclesMatchingEntriesFromNewestToOldestAndWraps()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out var first));
        Assert.Equal("release recent", first.CommandText);
        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out var second));
        Assert.Equal("restore older", second.CommandText);
        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out var third));
        Assert.Equal("remove oldest", third.CommandText);
        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out var wrapped));
        Assert.Equal("release recent", wrapped.CommandText);
        Assert.True(cycler.TryGetSuggestions("r", out var suggestions));
        Assert.Single(suggestions);
        Assert.Equal("release recent", suggestions[0].CommandText);
    }

    [Fact]
    public void FirstUpAdvancesPastTheCurrentHistorySelection()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TryCycle(
            history,
            "r",
            "r",
            older: true,
            out var selected,
            currentSelection: history[0]));

        Assert.Equal("restore older", selected.CommandText);
    }

    [Fact]
    public void FirstDownAdvancesFromTheCurrentSelectionTowardRecent()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TryCycle(
            history,
            "r",
            "r",
            older: false,
            out var selected,
            currentSelection: history[0]));

        Assert.Equal("remove oldest", selected.CommandText);
    }

    [Fact]
    public void RetainingSelectionFiltersMatchesWithoutLosingSelectedEntry()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();
        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out var selected));

        Assert.True(cycler.TryRetainSelection(history, "release ", "release ", selected));
        Assert.True(cycler.TryGetSuggestions("release ", out var suggestions));

        Assert.Single(suggestions);
        Assert.Equal(selected, suggestions[0]);
        Assert.True(cycler.TryCycle(history, "release ", "release ", older: true, out var cycled));
        Assert.Equal(selected, cycled);
    }

    [Fact]
    public void RetainingPathSelectionKeepsTheSameHistoryItemAcrossPathSegments()
    {
        var cycler = new HistorySuggestionCycler();
        var selected = new CommandHistoryEntry(
            @"c:\temp\LaunchPad.Ignite.App.Build\",
            DateTimeOffset.UtcNow);
        var history = new[] { selected, new CommandHistoryEntry(@"c:\temp\other\", DateTimeOffset.UtcNow) };

        Assert.True(cycler.TryCycle(history, "c", "c", older: true, out var initiallySelected));
        Assert.Equal(selected, initiallySelected);

        Assert.True(cycler.TryRetainSelection(history, "c:", "c:", selected));
        Assert.True(cycler.TryGetSuggestions("c:", out var afterDrivePrefix));
        Assert.Equal([selected], afterDrivePrefix);

        Assert.True(cycler.TryRetainSelection(history, @"c:\temp", @"c:\temp", selected));
        Assert.True(cycler.TryGetSuggestions(@"c:\temp", out var afterDirectoryPrefix));
        Assert.Equal([selected], afterDirectoryPrefix);
    }

    [Fact]
    public void RetainingSelectionClearsItWhenSelectedEntryNoLongerMatchesPrefix()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();
        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out var selected));

        Assert.False(cycler.TryRetainSelection(history, "restore ", "restore ", selected));
        Assert.False(cycler.TryGetSuggestions("restore ", out _));
    }

    [Fact]
    public void BoundarySelectionChoosesNewestOrOldestMatchingEntry()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TrySelectBoundary(history, "r", "r", newest: false, out var oldest));
        Assert.Equal("remove oldest", oldest.CommandText);
        Assert.True(cycler.TrySelectBoundary(history, "r", "r", newest: true, out var newest));
        Assert.Equal("release recent", newest.CommandText);
        Assert.True(cycler.TryGetSuggestions("r", out var suggestions));
        Assert.Single(suggestions);
        Assert.Equal("release recent", suggestions[0].CommandText);
    }

    [Fact]
    public void DownStartsAtOldestAndCyclesTowardRecent()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TryCycle(history, "r", "r", older: false, out var first));
        Assert.Equal("remove oldest", first.CommandText);
        Assert.True(cycler.TryCycle(history, "r", "r", older: false, out var second));
        Assert.Equal("restore older", second.CommandText);
        Assert.True(cycler.TryCycle(history, "r", "r", older: false, out var third));
        Assert.Equal("release recent", third.CommandText);
        Assert.True(cycler.TryGetSuggestions("r", out var suggestions));
        Assert.Single(suggestions);
        Assert.Equal("release recent", suggestions[0].CommandText);
    }

    [Fact]
    public void ChangingTypedTextStartsANewPrefixCycle()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out _));
        Assert.True(cycler.TryCycle(history, "s", "s", older: true, out var selected));

        Assert.Equal("status check", selected.CommandText);
        Assert.False(cycler.TryGetSuggestions("r", out _));
        Assert.True(cycler.TryGetSuggestions("s", out var suggestions));
        Assert.Single(suggestions);
    }

    [Fact]
    public void ResetStartsNextUpCycleAtNewestEntry()
    {
        var cycler = new HistorySuggestionCycler();
        var history = CreateHistory();

        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out _));
        Assert.True(cycler.TryCycle(history, "r", "r", older: true, out _));

        CommandHistoryEntry[] updatedHistory =
        [
            new CommandHistoryEntry("run latest", DateTimeOffset.UtcNow),
            .. history
        ];
        cycler.Reset();

        Assert.Equal(-1, cycler.SelectedIndex);
        Assert.True(cycler.TryCycle(updatedHistory, "r", "r", older: true, out var newest));
        Assert.Equal("run latest", newest.CommandText);
    }

    [Fact]
    public void DoesNotCycleWhenPrefixHasNoHistoryMatches()
    {
        var cycler = new HistorySuggestionCycler();

        Assert.False(cycler.TryCycle(CreateHistory(), "x", "x", older: true, out _));
        Assert.False(cycler.TryGetSuggestions("x", out _));
    }

    private static IReadOnlyList<CommandHistoryEntry> CreateHistory()
        =>
        [
            new("release recent", DateTimeOffset.UtcNow),
            new("status check", DateTimeOffset.UtcNow),
            new("restore older", DateTimeOffset.UtcNow),
            new("remove oldest", DateTimeOffset.UtcNow)
        ];
}
