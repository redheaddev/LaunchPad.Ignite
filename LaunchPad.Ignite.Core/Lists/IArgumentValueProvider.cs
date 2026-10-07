namespace LaunchPad.Ignite.Core.Lists;

public interface IArgumentValueProvider
{
    IReadOnlyList<string> GetValues(string reference);
}
