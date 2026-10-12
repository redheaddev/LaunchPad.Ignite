using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class ProfileSettingsStore
{
    private const string ThemePrefix = "app:theme:";
    private const string CustomThemePrefix = "app:customTheme:";
    private const string LegacyThemeMigrationKey = "app:themes:legacyCharcoalMigrated";
    private const string ThemeMigrationMetadataKey = "legacyCharcoalMigrated";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly Dictionary<string, string> _values;
    private readonly Dictionary<string, ProfileThemeSettings> _themes = new(StringComparer.OrdinalIgnoreCase);
    private bool _legacyCharcoalMigrated;

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
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(settingsPath))
        {
            Load(File.ReadAllText(settingsPath));
        }
    }

    public string GetValue(CommandSettingDefinition setting)
        => _values.TryGetValue(setting.QualifiedName, out var value) ? value : setting.DefaultValue;

    public string GetValue(string qualifiedName, string defaultValue)
    {
        if (_values.TryGetValue(qualifiedName, out var value))
        {
            return value;
        }

        return TryGetLegacyThemeValue(qualifiedName, out value) ? value : defaultValue;
    }

    public bool HasValue(string qualifiedName)
        => _values.ContainsKey(qualifiedName) || TryGetLegacyThemeValue(qualifiedName, out _);

    public IReadOnlyDictionary<string, string> GetValues()
        => new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ProfileThemeSettings> GetThemes()
        => _themes.Values.Select(theme => theme with
        {
            Colors = new Dictionary<string, string>(theme.Colors, StringComparer.OrdinalIgnoreCase)
        }).ToArray();

    public bool IsLegacyCharcoalMigrated => _legacyCharcoalMigrated;

    public void MarkLegacyCharcoalMigrated()
    {
        _legacyCharcoalMigrated = true;
        Save();
    }

    public bool TryGetTheme(string id, out ProfileThemeSettings theme)
    {
        if (_themes.TryGetValue(id, out var stored))
        {
            theme = stored with { Colors = new Dictionary<string, string>(stored.Colors, StringComparer.OrdinalIgnoreCase) };
            return true;
        }

        theme = null!;
        return false;
    }

    public void SetTheme(ProfileThemeSettings theme)
    {
        _themes[theme.Id] = theme with
        {
            Colors = new Dictionary<string, string>(theme.Colors, StringComparer.OrdinalIgnoreCase)
        };
        Save();
    }

    public void RemoveTheme(string id)
    {
        if (_themes.Remove(id))
        {
            Save();
        }
    }

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

        if (TrySetLegacyThemeValue(qualifiedName, value))
        {
            Save();
            return;
        }

        _values[qualifiedName] = value;
        Save();
    }

    public void RemoveValue(string qualifiedName)
    {
        if (TryRemoveLegacyThemeValue(qualifiedName))
        {
            Save();
            return;
        }

        if (_values.Remove(qualifiedName))
        {
            Save();
        }
    }

    public void RemoveValuesWithPrefix(string keyPrefix)
    {
        if (keyPrefix.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var themeId = keyPrefix[ThemePrefix.Length..].TrimEnd(':');
            RemoveTheme(themeId);
            return;
        }

        var keysToRemove = _values.Keys
            .Where(key => key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (keysToRemove.Length == 0)
        {
            return;
        }

        foreach (var key in keysToRemove)
        {
            _values.Remove(key);
        }

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

            if (!TrySetLegacyThemeValue(name, value))
            {
                _values[name] = value;
            }
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
            var root = new JsonObject();
            foreach (var (key, value) in _values)
            {
                root[key] = value;
            }

            root["settings"] = new JsonObject
            {
                ["themes"] = JsonSerializer.SerializeToNode(_themes.Values, JsonOptions),
                [ThemeMigrationMetadataKey] = _legacyCharcoalMigrated
            };
            File.WriteAllText(temporaryPath, root.ToJsonString(JsonOptions));
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

    private void Load(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject();
        if (root is null)
        {
            return;
        }

        var removeLegacyMigrationMarker = false;
        foreach (var (key, node) in root)
        {
            if (string.Equals(key, "settings", StringComparison.OrdinalIgnoreCase))
            {
                _legacyCharcoalMigrated |= node?[ThemeMigrationMetadataKey]?.GetValue<bool>() ?? false;
                var themes = node?["themes"]?.Deserialize<List<ProfileThemeSettings>>(JsonOptions);
                if (themes is not null)
                {
                    foreach (var theme in themes)
                    {
                        _themes[theme.Id] = theme with { Colors = new Dictionary<string, string>(theme.Colors ?? [], StringComparer.OrdinalIgnoreCase) };
                    }
                }

                continue;
            }

            if (node is JsonValue valueNode && valueNode.TryGetValue<string>(out var value))
            {
                if (string.Equals(key, LegacyThemeMigrationKey, StringComparison.OrdinalIgnoreCase))
                {
                    _legacyCharcoalMigrated |= string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    removeLegacyMigrationMarker = true;
                }
                else if (!TrySetLegacyThemeValue(key, value))
                {
                    _values[key] = value;
                }
            }
        }

        if (removeLegacyMigrationMarker && _legacyCharcoalMigrated)
        {
            Save();
        }
    }

    private bool TrySetLegacyThemeValue(string key, string value)
    {
        if (key.StartsWith(CustomThemePrefix, StringComparison.OrdinalIgnoreCase) && key.EndsWith(":name", StringComparison.OrdinalIgnoreCase))
        {
            var id = key[CustomThemePrefix.Length..^":name".Length];
            var colors = _themes.TryGetValue(id, out var existing)
                ? new Dictionary<string, string>(existing.Colors, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _themes[id] = new ProfileThemeSettings(id, value, colors);
            return true;
        }

        if (key.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = key[ThemePrefix.Length..];
            var separator = remainder.IndexOf(':');
            if (separator > 0 && separator < remainder.Length - 1)
            {
                var id = remainder[..separator];
                var property = remainder[(separator + 1)..];
                var existing = _themes.TryGetValue(id, out var theme)
                    ? theme
                    : new ProfileThemeSettings(id, id, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                var colors = new Dictionary<string, string>(existing.Colors, StringComparer.OrdinalIgnoreCase) { [property] = value };
                _themes[id] = existing with { Colors = colors };
                return true;
            }
        }

        return false;
    }

    private bool TryRemoveLegacyThemeValue(string key)
    {
        if (key.StartsWith(CustomThemePrefix, StringComparison.OrdinalIgnoreCase) && key.EndsWith(":name", StringComparison.OrdinalIgnoreCase))
        {
            var id = key[CustomThemePrefix.Length..^":name".Length];
            return _themes.Remove(id);
        }

        if (!key.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = key[ThemePrefix.Length..];
        var separator = remainder.IndexOf(':');
        if (separator <= 0 || !_themes.TryGetValue(remainder[..separator], out var theme))
        {
            return false;
        }

        var colors = new Dictionary<string, string>(theme.Colors, StringComparer.OrdinalIgnoreCase);
        var removed = colors.Remove(remainder[(separator + 1)..]);
        _themes[theme.Id] = theme with { Colors = colors };
        return removed;
    }

    private bool TryGetLegacyThemeValue(string key, out string value)
    {
        value = string.Empty;
        if (key.StartsWith(CustomThemePrefix, StringComparison.OrdinalIgnoreCase) && key.EndsWith(":name", StringComparison.OrdinalIgnoreCase))
        {
            var id = key[CustomThemePrefix.Length..^":name".Length];
            if (_themes.TryGetValue(id, out var theme))
            {
                value = theme.Name;
                return true;
            }

            return false;
        }

        if (!key.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = key[ThemePrefix.Length..];
        var separator = remainder.IndexOf(':');
        if (separator > 0 && _themes.TryGetValue(remainder[..separator], out var storedTheme)
            && storedTheme.Colors.TryGetValue(remainder[(separator + 1)..], out value!))
        {
            return true;
        }

        return false;
    }

}

public sealed record ProfileThemeSettings(string Id, string Name, Dictionary<string, string> Colors, string? Url = null);
