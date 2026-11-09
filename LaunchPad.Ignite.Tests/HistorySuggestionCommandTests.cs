using System;
using System.Collections.Generic;
using System.IO;
using LaunchPad.Ignite.App.Models;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Tests;

public sealed class HistorySuggestionCommandTests
{
    [Fact]
    public void HistorySuggestionsDefaultOnAndDisabledPreferencePersistsWithoutAffectingHashHistory()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var preference = new HistorySuggestionPreferenceStore(new ProfileSettingsStore(settingsPath));

        Assert.True(preference.IsEnabled);
        Assert.True(preference.ShouldSuggestForInput("git status"));
        Assert.False(preference.Toggle());
        Assert.False(preference.ShouldSuggestForInput("git status"));
        Assert.True(preference.ShouldSuggestForInput(" #git"));

        var reloaded = new HistorySuggestionPreferenceStore(new ProfileSettingsStore(settingsPath));
        Assert.False(reloaded.IsEnabled);
        Assert.False(reloaded.ShouldSuggestForInput("git status"));
        Assert.True(reloaded.ShouldSuggestForInput("#git"));
    }

    [Fact]
    public void HistorySuggestionCommandCanToggleAndSetPreference()
    {
        using var directory = new TemporaryDirectory();
        var preference = new HistorySuggestionPreferenceStore(
            new ProfileSettingsStore(Path.Combine(directory.Path, "settings.json")));
        var model = new HistorySuggestionCommandModel(preference);

        var result = Execute(model, "off");

        Assert.True(result.Succeeded);
        Assert.False(preference.IsEnabled);
        Assert.Contains("disabled", result.Message, StringComparison.OrdinalIgnoreCase);

        result = Execute(model, "toggle");

        Assert.True(result.Succeeded);
        Assert.True(preference.IsEnabled);

        result = Execute(model, "off");
        Assert.True(result.Succeeded);
        result = Execute(model, "on");
        Assert.True(result.Succeeded);
        Assert.True(preference.IsEnabled);
    }

    [Fact]
    public void HistorySuggestionToggleUsesCtrlShiftHAndExecutesImmediately()
    {
        var resolver = new ShortcutResolver([HistorySuggestionCommandModel.Definition]);

        Assert.True(KeyboardShortcutChord.TryParse("ctrl+shift+h", out var chord));
        Assert.True(resolver.TryExpandChord(chord, out var expansion, out var executeImmediately));

        Assert.Equal("history-suggestions toggle ", expansion);
        Assert.True(executeImmediately);
    }

    private static CommandExecutionResult Execute(HistorySuggestionCommandModel model, string actionName)
    {
        var definition = HistorySuggestionCommandModel.Definition;
        var action = Assert.Single(definition.Actions, candidate => candidate.Name == actionName);
        return model.Execute(new ParsedCommand(definition, action, new Dictionary<string, string>()));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
