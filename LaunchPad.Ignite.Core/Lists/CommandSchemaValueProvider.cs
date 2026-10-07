using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Core.Lists;

public sealed class CommandSchemaValueProvider : IArgumentValueProvider
{
    private readonly CommandSchema _schema;
    private readonly IArgumentValueProvider? _dynamicProvider;

    public CommandSchemaValueProvider(CommandSchema schema, IArgumentValueProvider? dynamicProvider = null)
    {
        _schema = schema;
        _dynamicProvider = dynamicProvider;
    }

    public IReadOnlyList<string> GetValues(string reference)
    {
        var separator = reference.IndexOf(':');
        if (separator <= 0 || separator == reference.Length - 1)
        {
            return Array.Empty<string>();
        }

        var type = reference[..separator];
        var name = reference[(separator + 1)..];
        if (string.Equals(type, "enum", StringComparison.OrdinalIgnoreCase))
        {
            return _schema.Lists.FirstOrDefault(list =>
                string.Equals(list.Type, "enum", StringComparison.OrdinalIgnoreCase)
                && string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase))?.Values
                ?? Array.Empty<string>();
        }

        return string.Equals(type, "list", StringComparison.OrdinalIgnoreCase)
            ? _dynamicProvider?.GetValues(reference) ?? Array.Empty<string>()
            : Array.Empty<string>();
    }
}
