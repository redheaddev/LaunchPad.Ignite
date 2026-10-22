using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services.Secrets;

public interface ISecretStore
{
    bool IsAvailable => true;

    string? UnavailableReason => null;

    IReadOnlyList<string> GetNames();

    bool TryGet(string name, out string value);

    void Set(string name, string value);

    bool Remove(string name);
}
