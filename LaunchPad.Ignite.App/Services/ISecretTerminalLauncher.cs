using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services;

public interface ISecretTerminalLauncher
{
    void OpenShell(IReadOnlyDictionary<string, string> environment, string? workingDirectory);
}
