using System;
using System.Linq;
using LaunchPad.Ignite.App.Services;
using LaunchPad.Ignite.App.Services.Profiles;
using LaunchPad.Ignite.App.Services.Themes;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.App.Models;

public sealed class ThemeCommandModel : ICommandModel
{
    private readonly ProfileSettingsStore _settingsStore;
    private readonly Action<ColorTheme> _applyTheme;

    public ThemeCommandModel(ProfileSettingsStore settingsStore, Action<ColorTheme> applyTheme)
    {
        _settingsStore = settingsStore;
        _applyTheme = applyTheme;
    }

    public string Kind => "theme";

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!string.Equals(command.Definition.ExecutionHandler, Kind, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandExecutionResult(false, $"Command handler '{command.Definition.ExecutionHandler}' is not supported.");
        }

        var name = command.Arguments.TryGetValue("name", out var requestedName) ? requestedName : string.Empty;
        var theme = ColorThemeService.LoadThemes(_settingsStore).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (theme is null)
        {
            return new CommandExecutionResult(false, $"Theme '{name}' was not found.");
        }

        _settingsStore.SetValue("app:colorTheme", theme.Id);
        _applyTheme(theme);
        return new CommandExecutionResult(true, $"Applied theme '{theme.Name}'.");
    }
}