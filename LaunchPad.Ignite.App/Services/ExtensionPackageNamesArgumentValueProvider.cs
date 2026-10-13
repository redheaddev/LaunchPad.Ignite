using System;
using System.Collections.Generic;
using LaunchPad.Ignite.App.Commands;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services;

public sealed class ExtensionPackageNamesArgumentValueProvider : IArgumentValueProvider
{
    public const string Reference = "list:extensionPackages";

    private readonly ExtensionPackageManager _packageManager;

    public ExtensionPackageNamesArgumentValueProvider(ExtensionPackageManager packageManager)
    {
        _packageManager = packageManager;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, Reference, StringComparison.OrdinalIgnoreCase)
            ? _packageManager.GetAvailablePackageNames()
            : Array.Empty<string>();
}
