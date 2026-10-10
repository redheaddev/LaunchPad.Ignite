using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed record CommandPackManifest(
    string Id,
    string? Namespace,
    CommandSchema Schema);
