using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Services.Terminals;

public interface ISecretTerminalLauncher
{
    void OpenShell(IReadOnlyDictionary<string, string> environment, string? workingDirectory);

    void RunScript(
        string scriptPath,
        string type,
        IReadOnlyDictionary<string, string> environment);
}
