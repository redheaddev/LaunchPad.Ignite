namespace LaunchPad.Ignite.Core.Schema;

public sealed record CommandSchema(
    IReadOnlyList<CommandListDefinition> Lists,
    IReadOnlyList<CommandDefinition> Commands);

public sealed record CommandListDefinition(
    string Type,
    string Name,
    IReadOnlyList<string> Values);

public sealed record CommandDefinition(
    string Name,
    string Kind,
    string Description,
    IReadOnlyList<CommandActionDefinition> Actions,
    string? Namespace = null)
{
    public string QualifiedName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}:{Name}";
}

public sealed record CommandActionDefinition(
    string Name,
    string Description,
    string Usage,
    string TargetTemplate,
    IReadOnlyList<CommandArgumentDefinition> Arguments,
    CommandExecutionDefinition? Execution = null);

public sealed record CommandArgumentDefinition(
    string Name,
    bool Optional = false,
    string? Ref = null,
    string? InputType = null,
    ListSelectionMode ListMode = ListSelectionMode.Normal,
    bool AllowCustomValues = false);

public sealed record CommandExecutionDefinition(
    CommandExecutionMode Mode,
    IReadOnlyList<string> Arguments);
