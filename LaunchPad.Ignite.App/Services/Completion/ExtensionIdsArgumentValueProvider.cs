using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services.Completion;

public sealed class ExtensionIdsArgumentValueProvider : IArgumentValueProvider
{
    public const string Reference = "list:installedExtensions";

    private readonly IReadOnlyList<string> _extensionIds;

    public ExtensionIdsArgumentValueProvider(IEnumerable<string> extensionIds)
    {
        _extensionIds = extensionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, Reference, StringComparison.OrdinalIgnoreCase)
            ? _extensionIds
            : Array.Empty<string>();
}
