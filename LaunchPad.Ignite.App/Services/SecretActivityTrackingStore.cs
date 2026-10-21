using System;
using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services;

public sealed class SecretActivityTrackingStore(
    ISecretStore innerStore,
    ProfileSettingsStore settingsStore) : ISecretStore
{
    public IReadOnlyList<string> GetNames()
        => innerStore.GetNames();

    public bool TryGet(string name, out string value)
    {
        var found = innerStore.TryGet(name, out value);
        if (found)
        {
            settingsStore.TryRecordSecretLastAccessed(name, DateTimeOffset.UtcNow);
        }

        return found;
    }

    public void Set(string name, string value)
        => innerStore.Set(name, value);

    public bool Remove(string name)
        => innerStore.Remove(name);
}
