using LaunchPad.Ignite.Core.Parsing;

namespace LaunchPad.Ignite.Core.Execution;

public interface IExtensionFunctionExecutor : ICommandFunctionExecutor
{
    string FunctionName { get; }
}

public interface IAsyncCommandExecutor
{
    string Kind { get; }

    Task<CommandExecutionResult> ExecuteAsync(ParsedCommand command);
}

public interface IExtensionCommandContextAware
{
    string Namespace { get; }

    void Configure(
        IReadOnlyDictionary<string, string> settings,
        Func<string, (bool Found, string Value)> tryGetSecret,
        Action<string> openUrl);
}
