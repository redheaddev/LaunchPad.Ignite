using System.Collections.Generic;
using LaunchPad.Ignite.Core.Lists;

namespace LaunchPad.Ignite.App.Services;

public sealed class SnippetNamesArgumentValueProvider : IArgumentValueProvider
{
    private readonly SnippetStore _snippetStore;

    public SnippetNamesArgumentValueProvider(SnippetStore snippetStore)
    {
        _snippetStore = snippetStore;
    }

    public IReadOnlyList<string> GetValues(string reference)
        => string.Equals(reference, "list:snippets", System.StringComparison.OrdinalIgnoreCase)
            ? _snippetStore.GetNames()
            : System.Array.Empty<string>();
}
