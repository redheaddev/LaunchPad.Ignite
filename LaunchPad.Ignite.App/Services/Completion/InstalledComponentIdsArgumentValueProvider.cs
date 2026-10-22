using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Completion;

public sealed class InstalledComponentIdsArgumentValueProvider : IArgumentValueProvider
{
    public const string EnableReference = "list:enableableComponents";
    public const string DisableReference = "list:disableableComponents";

    private readonly IReadOnlyList<string> _componentIds;
    private readonly ExtensionStateStore _stateStore;

    public InstalledComponentIdsArgumentValueProvider(
        IEnumerable<string> componentIds,
        ExtensionStateStore stateStore)
    {
        _componentIds = componentIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _stateStore = stateStore;
    }

    public IReadOnlyList<string> GetValues(string reference)
    {
        if (string.Equals(reference, EnableReference, StringComparison.OrdinalIgnoreCase))
        {
            return _componentIds.Where(id => !_stateStore.IsEnabled(id)).ToArray();
        }

        if (string.Equals(reference, DisableReference, StringComparison.OrdinalIgnoreCase))
        {
            return _componentIds.Where(_stateStore.IsEnabled).ToArray();
        }

        return Array.Empty<string>();
    }
}
