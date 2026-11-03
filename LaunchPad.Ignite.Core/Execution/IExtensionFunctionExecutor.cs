using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Execution;

public interface IExtensionFunctionExecutor : ICommandFunctionExecutor
{
    string FunctionName { get; }
}

public interface ICommandListFunctionExecutor
{
    Task<IReadOnlyList<CommandListItem>> ExecuteListAsync(
        CommandFunctionDefinition function,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public interface IExtensionListFunctionExecutor : ICommandListFunctionExecutor
{
    string FunctionName { get; }
}

public sealed record CommandListItem(string Value, string? DisplayText = null);

public interface IExtensionFunctionContextAware
{
    string Namespace { get; }

    void Configure(
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lists,
        Func<string, (bool Found, string Value)> tryGetSecret);
}

public interface IExtensionRootAware
{
    void ConfigureExtensionRoot(string extensionRoot);
}

public interface IExtensionShutdownAware
{
    void Shutdown();
}

public interface IAsyncCommandExecutor
{
    string Kind { get; }

    Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command);
}

public interface IAsyncCommandExecutorWithLaunchOptions : IAsyncCommandExecutor
{
    Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command, bool newWindow);
}

public interface IExtensionCommandContextAware
{
    string Namespace { get; }

    void Configure(
        IReadOnlyDictionary<string, string> settings,
        Func<string, (bool Found, string Value)> tryGetSecret,
        Action<string> openUrl);
}

public interface IExtensionCommandContextAwareWithLaunchOptions
{
    string Namespace { get; }

    void Configure(
        IReadOnlyDictionary<string, string> settings,
        Func<string, (bool Found, string Value)> tryGetSecret,
        Action<IReadOnlyList<string>, bool> openUrls);
}

public interface IExtensionTerminalContextAware
{
    string Namespace { get; }

    void ConfigureTerminal(Action<string> openTerminal);
}

public interface IExtensionSecretResolverAware
{
    string Namespace { get; }

    void ConfigureSecretResolver(
        Func<IReadOnlyList<string>, Task<ExtensionSecretResolutionResult>> resolveSecrets);
}

public sealed record ExtensionSecretResolutionResult(
    bool Succeeded,
    IReadOnlyDictionary<string, string> Secrets,
    string Message);
