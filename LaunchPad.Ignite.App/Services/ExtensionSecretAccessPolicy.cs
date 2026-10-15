using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LaunchPad.Ignite.App.Services;

public sealed class ExtensionSecretAccessPolicy(
    ProfileSettingsStore settingsStore,
    ISecretStore secretStore)
{
    private const string SettingPrefix = "secretPermissions:";

    public static string GetSettingName(string extensionNamespace)
        => $"{SettingPrefix}{extensionNamespace}";

    public bool IsAllowed(string extensionNamespace, string secretName)
    {
        if (string.IsNullOrWhiteSpace(extensionNamespace)
            || string.IsNullOrWhiteSpace(secretName))
        {
            return false;
        }

        var configured = settingsStore.GetValue(GetSettingName(extensionNamespace), "[]");
        try
        {
            var allowedNames = JsonSerializer.Deserialize<string[]>(configured) ?? [];
            return allowedNames.Contains(secretName, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public (bool Found, string Value) TryGetSecret(string extensionNamespace, string secretName)
    {
        if (!IsAllowed(extensionNamespace, secretName))
        {
            return (false, string.Empty);
        }

        var found = secretStore.TryGet(secretName, out var value);
        return (found, value);
    }

    public IReadOnlyDictionary<string, string> GetAllowedSecrets(
        string extensionNamespace,
        IEnumerable<string> secretNames,
        out string? deniedSecret)
    {
        var names = secretNames.ToArray();
        deniedSecret = names.FirstOrDefault(name => !IsAllowed(extensionNamespace, name));
        if (deniedSecret is not null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (!secretStore.TryGet(name, out var value))
            {
                deniedSecret = name;
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            secrets.Add(name, value);
        }

        return secrets;
    }
}