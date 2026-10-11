using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class ApplicationSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _settingsPath;

    public ApplicationSettingsLoader(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(AppContext.BaseDirectory, "app-settings.json");
    }

    public IReadOnlyList<CommandSettingDefinition> Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return [];
        }

        var settings = JsonSerializer.Deserialize<ApplicationSettingsDocument>(File.ReadAllText(_settingsPath), JsonOptions)?.Settings
            ?? throw new InvalidDataException($"Application settings file '{_settingsPath}' is invalid.");
        if (settings.Any(setting => setting is null || string.IsNullOrWhiteSpace(setting.Name)))
        {
            throw new InvalidDataException($"Application settings file '{_settingsPath}' contains an invalid setting.");
        }

        return settings;
    }

    private sealed record ApplicationSettingsDocument(IReadOnlyList<CommandSettingDefinition> Settings);
}
