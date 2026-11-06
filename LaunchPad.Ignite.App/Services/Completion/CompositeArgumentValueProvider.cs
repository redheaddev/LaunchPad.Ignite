using System.Collections.Generic;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Completion;

public sealed class CompositeArgumentValueProvider : IArgumentValueProvider, IArgumentValueDisplayProvider
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

    public IReadOnlyList<string> GetDisplayValues(string reference)
    {
        foreach (var provider in _providers)
        {
            if (provider.GetValues(reference).Count > 0)
            {
                return provider is IArgumentValueDisplayProvider displayProvider
                    ? displayProvider.GetDisplayValues(reference)
                    : [];
            }
        }

        return [];
    }
}
