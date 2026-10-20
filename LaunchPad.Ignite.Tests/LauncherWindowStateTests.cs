using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.Core.Parsing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LaunchPad.Ignite.Tests;

public sealed class LauncherWindowStateTests
{
    [Theory]
    [InlineData("toggle", LauncherWindowMode.Main, LauncherWindowMode.Mini, true)]
    [InlineData("toggle", LauncherWindowMode.Mini, LauncherWindowMode.Main, true)]
    [InlineData("main", LauncherWindowMode.Mini, LauncherWindowMode.Main, true)]
    [InlineData("mini", LauncherWindowMode.Main, LauncherWindowMode.Mini, true)]
    [InlineData("invalid", LauncherWindowMode.Main, LauncherWindowMode.Main, false)]
    public void LauncherModeCommandParser_ParsesModeActions(
        string action,
        LauncherWindowMode currentMode,
        LauncherWindowMode expectedMode,
        bool expectedSuccess)
    {
        var success = LauncherModeCommandParser.TryParse(action, currentMode, out var targetMode);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedMode, targetMode);
    }

    [Fact]
    public void LauncherModeCommand_PersistsModeImmediately()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var stateStore = new LauncherWindowStateStore(new ProfileSettingsStore(settingsPath));
        var commandModel = new LauncherModeCommandModel(() => stateStore.Mode, stateStore.SetMode);
        var action = LauncherModeCommandModel.Definition.Actions.Single(item => item.Name == "mini");
        var command = new ParsedCommand(
            LauncherModeCommandModel.Definition,
            action,
            new Dictionary<string, string>());

        var result = commandModel.Execute(command);

        Assert.True(result.Succeeded);
        Assert.Equal(LauncherWindowMode.Mini, new LauncherWindowStateStore(new ProfileSettingsStore(settingsPath)).Mode);
    }

    [Fact]
    public void LauncherWindowStateStore_PersistsSeparatePositionsForEachMode()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var stateStore = new LauncherWindowStateStore(new ProfileSettingsStore(settingsPath));
        var mainPosition = new LauncherWindowPosition(-1920, 125);
        var miniPosition = new LauncherWindowPosition(240, 480);

        stateStore.SetPosition(LauncherWindowMode.Main, mainPosition);
        stateStore.SetPosition(LauncherWindowMode.Mini, miniPosition);

        var reloadedStore = new LauncherWindowStateStore(new ProfileSettingsStore(settingsPath));
        Assert.True(reloadedStore.TryGetPosition(LauncherWindowMode.Main, out var restoredMainPosition));
        Assert.True(reloadedStore.TryGetPosition(LauncherWindowMode.Mini, out var restoredMiniPosition));
        Assert.Equal(mainPosition, restoredMainPosition);
        Assert.Equal(miniPosition, restoredMiniPosition);
    }

    [Fact]
    public void LauncherWindowStateStore_PersistsSeparateSizesForEachMode()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var stateStore = new LauncherWindowStateStore(new ProfileSettingsStore(settingsPath));
        var mainSize = new LauncherWindowSize(920, 540);
        var miniSize = new LauncherWindowSize(580, 80);

        stateStore.SetSize(LauncherWindowMode.Main, mainSize);
        stateStore.SetSize(LauncherWindowMode.Mini, miniSize);

        var reloadedStore = new LauncherWindowStateStore(new ProfileSettingsStore(settingsPath));
        Assert.True(reloadedStore.TryGetSize(LauncherWindowMode.Main, out var restoredMainSize));
        Assert.True(reloadedStore.TryGetSize(LauncherWindowMode.Mini, out var restoredMiniSize));
        Assert.Equal(mainSize, restoredMainSize);
        Assert.Equal(miniSize, restoredMiniSize);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
