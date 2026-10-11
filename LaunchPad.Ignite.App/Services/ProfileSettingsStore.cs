using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class ProfileSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly Dictionary<string, string> _values;

    public ProfileSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "settings.json"))
    {
    }

    public ProfileSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
        _values = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsPath), JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string GetValue(CommandSettingDefinition setting)
        => _values.TryGetValue(setting.QualifiedName, out var value) ? value : setting.DefaultValue;

    public string GetValue(string qualifiedName, string defaultValue)
        => _values.TryGetValue(qualifiedName, out var value) ? value : defaultValue;

    public bool HasValue(string qualifiedName)
        => _values.ContainsKey(qualifiedName);

    public IReadOnlyDictionary<string, string> GetValues()
        => new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);

    public void SetValue(string qualifiedName, string value)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            throw new ArgumentException("A setting name is required.", nameof(qualifiedName));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Setting values cannot be empty.", nameof(value));
        }

        _values[qualifiedName] = value;
        Save();
    }

    public void SetValues(IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Setting names and values must be non-empty.", nameof(values));
            }

            _values[name] = value;
        }

        Save();
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _settingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_values, JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
