using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed record ExtensionManifest(
    string Id,
    string Name,
    string Namespace,
    CommandSchema Schema,
    string? Assembly = null,
    string? Version = null);
