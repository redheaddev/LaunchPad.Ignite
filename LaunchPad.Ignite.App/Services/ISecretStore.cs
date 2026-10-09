using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services;

public interface ISecretStore
{
    IReadOnlyList<string> GetNames();

    bool TryGet(string name, out string value);

    void Set(string name, string value);

    bool Remove(string name);
}
