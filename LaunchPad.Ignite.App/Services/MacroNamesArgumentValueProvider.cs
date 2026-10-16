using System;
using System.Collections.Generic;
using System.Linq;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services;

public sealed class MacroNamesArgumentValueProvider : IArgumentValueProvider
{
    private readonly MacroStore _macroStore;

    public MacroNamesArgumentValueProvider(MacroStore macroStore)
    {
        _macroStore = macroStore;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, "list:macros", StringComparison.OrdinalIgnoreCase)
            ? _macroStore.GetAll().Select(macro => macro.Metadata.QualifiedName).ToArray()
            : Array.Empty<string>();
}
