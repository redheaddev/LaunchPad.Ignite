using System;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Services.Profiles;

namespace LaunchPad.Ignite.App.Services.Secrets;

public sealed class SecretActivityTrackingStore(
    ISecretStore innerStore,
    ProfileSettingsStore settingsStore) : ISecretStore
{
    public bool IsAvailable => innerStore.IsAvailable;

    public string? UnavailableReason => innerStore.UnavailableReason;

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
