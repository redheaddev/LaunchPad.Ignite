using System.Collections.Generic;

namespace LaunchPad.Ignite.App.Models;

public sealed record SnippetMetadata(
    string Id,
    string Name,
    string Type,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> AllowedSecrets);

public sealed record SnippetDefinition(SnippetMetadata Metadata, string Script);
