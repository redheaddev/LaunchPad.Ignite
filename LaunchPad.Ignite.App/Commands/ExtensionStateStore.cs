using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LaunchPad.Ignite.App.Commands;

public sealed class ExtensionStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _statePath;

    public ExtensionStateStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaunchPad.Ignite",
            "extension-state.json"))
    {
    }

    public ExtensionStateStore(string statePath)
    {
        _statePath = statePath;
    }

    public IReadOnlySet<string> GetDisabledExtensionIds()
    {
        if (!File.Exists(_statePath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var state = JsonSerializer.Deserialize<ExtensionState>(File.ReadAllText(_statePath), JsonOptions)
            ?? throw new InvalidDataException("The extension state file is invalid.");
        return new HashSet<string>(
            (state.DisabledExtensionIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsEnabled(string id)
        => !GetDisabledExtensionIds().Contains(id);

    public void SetEnabled(string id, bool enabled)
    {
        SetEnabled(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [id] = enabled
        });
    }

    public void SetEnabled(IEnumerable<string> ids, bool enabled)
        => SetEnabled(ids.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(id => id, _ => enabled, StringComparer.OrdinalIgnoreCase));

    public void SetEnabled(IReadOnlyDictionary<string, bool> states)
    {
        var disabledIds = GetDisabledExtensionIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, enabled) in states)
        {
            if (enabled)
            {
                disabledIds.Remove(id);
            }
            else
            {
                disabledIds.Add(id);
            }
        }

        SaveState(disabledIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public void Remove(string id)
    {
        var disabledIds = GetDisabledExtensionIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (disabledIds.Remove(id))
        {
            SaveState(disabledIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private void SaveState(IReadOnlyList<string> disabledExtensionIds)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _statePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new ExtensionState(disabledExtensionIds), JsonOptions));
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record ExtensionState(IReadOnlyList<string>? DisabledExtensionIds);
}
