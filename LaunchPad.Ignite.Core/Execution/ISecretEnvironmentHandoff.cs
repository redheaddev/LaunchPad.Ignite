namespace LaunchPad.Ignite.Core.Execution;

public interface ISecretEnvironmentHandoff
{
    void ConfigureExtensionRoot(string extensionRoot);

    Task<CommandExecutionResult> OpenAsync(
        IReadOnlyDictionary<string, string> environment,
        string? workingDirectory,
        string? terminalName);
}
