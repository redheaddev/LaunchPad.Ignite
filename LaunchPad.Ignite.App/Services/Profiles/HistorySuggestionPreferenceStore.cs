using System;

namespace LaunchPad.Ignite.App.Services.Profiles;

public sealed class HistorySuggestionPreferenceStore(ProfileSettingsStore settingsStore)
{
    private const string SettingName = "app:historySuggestions";

    public bool IsEnabled => string.Equals(
        settingsStore.GetValue(SettingName, "On"),
        "On",
        StringComparison.OrdinalIgnoreCase);

    public bool ShouldSuggestForInput(string inputText)
        => IsEnabled || inputText.Trim().StartsWith('#');

    public bool Toggle()
    {
        var enabled = !IsEnabled;
        SetEnabled(enabled);
        return enabled;
    }

    public void SetEnabled(bool enabled)
        => settingsStore.SetValue(SettingName, enabled ? "On" : "Off");
}
