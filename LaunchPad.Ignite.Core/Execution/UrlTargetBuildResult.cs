namespace LaunchPad.Ignite.Core.Execution;

public sealed record UrlTargetBuildResult(
    IReadOnlyList<UrlTarget> Targets,
    string? Error = null)
{
    public bool Succeeded => Error is null;
}
