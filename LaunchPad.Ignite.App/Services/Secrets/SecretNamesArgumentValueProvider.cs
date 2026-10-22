using System;
using System.Collections.Generic;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Secrets;

public sealed class SecretNamesArgumentValueProvider : IArgumentValueProvider
{
    private readonly ISecretStore _secretStore;

    public SecretNamesArgumentValueProvider(ISecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, "list:secrets", StringComparison.OrdinalIgnoreCase)
            ? _secretStore.GetNames()
            : Array.Empty<string>();
}
