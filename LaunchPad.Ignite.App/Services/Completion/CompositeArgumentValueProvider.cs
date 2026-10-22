using System.Collections.Generic;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Completion;

public sealed class CompositeArgumentValueProvider : IArgumentValueProvider
{
    private readonly IReadOnlyList<IArgumentValueProvider> _providers;

    public CompositeArgumentValueProvider(params IArgumentValueProvider[] providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<string> GetValues(string reference)
    {
        foreach (var provider in _providers)
        {
            var values = provider.GetValues(reference);
            if (values.Count > 0)
            {
                return values;
            }
        }

        return [];
    }
}
