using Avalonia.Input;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;

namespace LaunchPad.Ignite.Tests;

public sealed class LauncherNavigationShortcutParserTests
{
    [Theory]
    [InlineData(Key.I, LauncherNavigationTarget.ImportExport)]
    [InlineData(Key.H, LauncherNavigationTarget.Help)]
    [InlineData(Key.OemPeriod, LauncherNavigationTarget.Settings)]
    public void ParsesNavigationShortcut(Key key, LauncherNavigationTarget expectedTarget)
    {
        var parsed = LauncherNavigationShortcutParser.TryParse(key, KeyModifiers.Control, out var target);

        Assert.True(parsed);
        Assert.Equal(expectedTarget, target);
    }

    [Fact]
    public void DoesNotTreatModifiedOrUnrecognizedKeysAsNavigationShortcuts()
    {
        Assert.False(LauncherNavigationShortcutParser.TryParse(Key.I, KeyModifiers.Control | KeyModifiers.Shift, out _));
        Assert.False(LauncherNavigationShortcutParser.TryParse(Key.A, KeyModifiers.Control, out _));
    }
}
