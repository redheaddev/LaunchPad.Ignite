using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Services;

public sealed class ListDefaultOverrideStore(ProfileSettingsStore settingsStore)
{
    private const string SettingPrefix = "app:listDefault:";
    private const string ListOverridePrefix = "app:listOverride:";

    private sealed record ListOverride(IReadOnlyList<string> Values, string Default);

    public IReadOnlyList<CommandListDefinition> ApplyOverrides(IEnumerable<CommandListDefinition> lists)
        => lists.Select(list =>
        {
            var overrideKey = GetListOverrideSettingName(list);
            if (string.Equals(list.Type, "enum", StringComparison.OrdinalIgnoreCase)
                && settingsStore.HasValue(overrideKey))
            {
                try
                {
                    var listOverride = JsonSerializer.Deserialize<ListOverride>(settingsStore.GetValue(overrideKey, string.Empty));
                    if (listOverride is not null && IsValidOverride(listOverride))
                    {
                        var values = OrderValues(listOverride.Values, listOverride.Default);
                        return list with { Values = values, Default = listOverride.Default };
                    }
                }
                catch (JsonException)
                {
                    // Ignore invalid profile data and fall back to the schema definition.
                }
            }

            var settingName = GetSettingName(list);
            var value = settingsStore.GetValue(settingName, string.Empty);
            var effectiveList = list.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) is { } selected
                ? list with { Default = selected }
                : list;
            return string.Equals(effectiveList.Type, "enum", StringComparison.OrdinalIgnoreCase)
                ? effectiveList with { Values = OrderValues(effectiveList.Values, effectiveList.Default) }
                : effectiveList;
        }).ToArray();

    public void SetList(CommandListDefinition list, IEnumerable<string> values, string defaultValue)
    {
        if (!string.Equals(list.Type, "enum", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only enum lists can be edited.", nameof(list));
        }

        var normalizedValues = values
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var canonicalDefault = normalizedValues.FirstOrDefault(value =>
            string.Equals(value, defaultValue?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (canonicalDefault is null)
        {
            throw new ArgumentException("The default must be one of the list values.", nameof(defaultValue));
        }

        var orderedValues = OrderValues(normalizedValues, canonicalDefault);
        settingsStore.SetValue(
            GetListOverrideSettingName(list),
            JsonSerializer.Serialize(new ListOverride(orderedValues, canonicalDefault)));
    }

    public void ResetList(CommandListDefinition list)
    {
        settingsStore.RemoveValue(GetListOverrideSettingName(list));
        settingsStore.RemoveValue(GetSettingName(list));
    }

    private static bool IsValidOverride(ListOverride listOverride)
        => listOverride.Values is { Count: > 0 }
            && listOverride.Values.All(value => !string.IsNullOrWhiteSpace(value))
            && listOverride.Values.Any(value => string.Equals(value, listOverride.Default, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> OrderValues(IEnumerable<string> values, string? defaultValue)
    {
        var uniqueValues = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selectedDefault = uniqueValues.FirstOrDefault(value =>
            string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase));
        var remainingValues = uniqueValues
            .Where(value => !string.Equals(value, selectedDefault, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        return selectedDefault is null
            ? remainingValues.ToArray()
            : remainingValues.Prepend(selectedDefault).ToArray();
    }

    public string? GetDefaultOverride(CommandListDefinition list)
    {
        var value = settingsStore.GetValue(GetSettingName(list), string.Empty);
        return list.Values.FirstOrDefault(candidate =>
            string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
    }

    public void SetDefault(CommandListDefinition list, string? value)
    {
        var settingName = GetSettingName(list);
        if (string.IsNullOrWhiteSpace(value))
        {
            settingsStore.RemoveValue(settingName);
            return;
        }

        var canonicalValue = list.Values.FirstOrDefault(candidate =>
            string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
        if (canonicalValue is null)
        {
            throw new ArgumentException($"'{value}' is not a value in list '{list.Name}'.", nameof(value));
        }

        settingsStore.SetValue(settingName, canonicalValue);
    }

    public static string GetSettingName(CommandListDefinition list)
        => $"{SettingPrefix}{list.Type}:{list.QualifiedName}";

    public static string GetListOverrideSettingName(CommandListDefinition list)
        => $"{ListOverridePrefix}{list.Type}:{list.QualifiedName}";
}
