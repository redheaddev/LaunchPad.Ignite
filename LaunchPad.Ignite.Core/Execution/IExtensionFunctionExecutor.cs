namespace LaunchPad.Ignite.Core.Execution;

public interface IExtensionFunctionExecutor : ICommandFunctionExecutor
{
    string FunctionName { get; }
}
