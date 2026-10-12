using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services;

public sealed class ThemeNamesArgumentValueProvider : IArgumentValueProvider
{
    private readonly ProfileSettingsStore _settingsStore;

    public ThemeNamesArgumentValueProvider(ProfileSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, "list:themes", StringComparison.OrdinalIgnoreCase)
            ? ColorThemeService.LoadThemes(_settingsStore).Select(theme => theme.Name).ToArray()
            : Array.Empty<string>();
}