using LaunchPad.Ignite.Core.Execution;

namespace LaunchPad.Ignite.Core.Lists;

public interface IArgumentValueProvider
{
    IReadOnlyList<string> GetValues(string reference);
}

public interface IArgumentValueDisplayProvider
{
    IReadOnlyList<string> GetDisplayValues(string reference);
}

public interface ICommandListFunctionValueProvider
{
    event EventHandler? ValuesChanged;

    IReadOnlyList<CommandListItem> GetOrStart(string functionName, IReadOnlyList<string> arguments);
}
